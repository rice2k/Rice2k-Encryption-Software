using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Rice2k.Encryption.Models;
using Sodium;

namespace Rice2k.Encryption.Services;

public sealed partial class SecureVaultService
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("R2KVAULT");
    private const byte Version = 1;
    private const byte AlgorithmXChaCha20Poly1305 = 1;
    private const byte KdfArgon2Id = 1;
    private const long DefaultOpsLimit = 4;
    private const int DefaultMemLimit = 64 * 1024 * 1024;
    private const int DefaultChunkSize = 4 * 1024 * 1024;
    private const int SaltSize = 16;
    private const int NonceSize = 24;
    private const int AuthenticationTagSize = 16;
    private const int MinimumPasswordLength = 12;
    private const long MaximumSupportedOpsLimit = 10;
    private const int MaximumSupportedMemLimit = 256 * 1024 * 1024;
    private const int MaximumSupportedChunkSize = 64 * 1024 * 1024;
    private const int MaximumManifestCipherLength = 16 * 1024 * 1024;
    private const int MaximumManifestPlainLength = MaximumManifestCipherLength - AuthenticationTagSize;
    private const long MaximumRecordPayloadLength = long.MaxValue / 4;

    private sealed record VaultManifest(
        Guid VaultId,
        DateTimeOffset CreatedUtc,
        DateTimeOffset UpdatedUtc,
        long Sequence,
        int ChunkSize,
        List<VaultEntryInfo> Entries);

    private sealed record VaultHeader(
        long OpsLimit,
        int MemLimit,
        int ChunkSize,
        byte[] Salt,
        Guid VaultId,
        byte[] ManifestNonce,
        byte[] ManifestCipher,
        byte[] HeaderAuthenticationData);

    private sealed record VaultRecordIndex(
        Guid EntryId,
        long RecordOffset,
        long PayloadOffset,
        long PayloadLength,
        long TotalLength);

    private sealed record UnlockedVaultState(
        VaultHeader Header,
        VaultManifest Manifest,
        IReadOnlyDictionary<Guid, VaultRecordIndex> Records);

    public async Task<SecureVaultSession> CreateAsync(
        string destinationPath,
        string password,
        CancellationToken cancellationToken = default)
    {
        ValidateNewVaultPath(destinationPath);
        ValidateNewPassword(password);

        var fullPath = Path.GetFullPath(destinationPath);
        var tempPath = CreateUniqueSiblingPath(fullPath, ".pending");
        var salt = PasswordHash.ArgonGenerateSalt();
        var passwordBytes = Encoding.UTF8.GetBytes(password);
        byte[]? key = null;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            key = DeriveKey(passwordBytes, salt, DefaultOpsLimit, DefaultMemLimit);
            var vaultId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;
            var manifest = new VaultManifest(vaultId, now, now, 0, DefaultChunkSize, []);
            var headerAuth = BuildHeaderAuthenticationData(DefaultOpsLimit, DefaultMemLimit, DefaultChunkSize, salt, vaultId);
            var manifestNonce = SecretAeadXChaCha20Poly1305.GenerateNonce();
            var manifestCipher = EncryptManifest(manifest, manifestNonce, key, headerAuth);

            try
            {
                await using (var output = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
                using (var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true))
                {
                    WriteHeader(writer, DefaultOpsLimit, DefaultMemLimit, DefaultChunkSize, salt, vaultId, manifestNonce, manifestCipher);
                    writer.Flush();
                    await output.FlushAsync(cancellationToken);
                    output.Flush(flushToDisk: true);
                }

                _ = await ReadStateWithKeyAsync(tempPath, key, verifyContents: true, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (File.Exists(fullPath))
                    throw new IOException("A vault already exists at the selected destination. Rice2k will not overwrite it automatically.");
                File.Move(tempPath, fullPath);

                return new SecureVaultSession(fullPath, vaultId, now, now, 0, key, Array.Empty<VaultEntryInfo>());
            }
            finally
            {
                CryptographicOperations.ZeroMemory(manifestCipher);
            }
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
            if (key is not null)
                CryptographicOperations.ZeroMemory(key);
        }
    }

    public async Task<SecureVaultSession> UnlockAsync(
        string vaultPath,
        string password,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vaultPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        if (!File.Exists(vaultPath))
            throw new FileNotFoundException("The selected .r2kvault file could not be found.", vaultPath);

        var passwordBytes = Encoding.UTF8.GetBytes(password);
        byte[]? key = null;
        try
        {
            await using var input = new FileStream(vaultPath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var reader = new BinaryReader(input, Encoding.UTF8, leaveOpen: true);
            var header = ReadHeader(reader);
            cancellationToken.ThrowIfCancellationRequested();
            key = DeriveKey(passwordBytes, header.Salt, header.OpsLimit, header.MemLimit);
            var manifest = DecryptManifest(header, key);
            var records = ScanRecordIndex(reader, input.Length);
            ValidateManifestAndRecords(manifest, header, records);

            return new SecureVaultSession(
                Path.GetFullPath(vaultPath),
                manifest.VaultId,
                manifest.CreatedUtc,
                manifest.UpdatedUtc,
                manifest.Sequence,
                key,
                manifest.Entries);
        }
        catch (CryptographicException ex)
        {
            throw new CryptographicException("Rice2k could not unlock this vault. The password may be wrong, or the vault may have been modified.", ex);
        }
        catch (EndOfStreamException ex)
        {
            throw new InvalidDataException("The vault appears to be truncated.", ex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
            if (key is not null)
                CryptographicOperations.ZeroMemory(key);
        }
    }

    public async Task VerifyAsync(SecureVaultSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        var key = session.CopyContentKey();
        try
        {
            var state = await ReadStateWithKeyAsync(session.VaultPath, key, verifyContents: true, cancellationToken);
            EnsureSessionMatches(session, state.Manifest);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    public async Task ExtractAsync(
        SecureVaultSession session,
        Guid entryId,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        if (File.Exists(destinationPath))
            throw new IOException("A file already exists at the selected restore path. Rice2k will not overwrite it automatically.");

        var destinationDirectory = Path.GetDirectoryName(Path.GetFullPath(destinationPath));
        if (string.IsNullOrWhiteSpace(destinationDirectory) || !Directory.Exists(destinationDirectory))
            throw new DirectoryNotFoundException("The selected restore folder does not exist.");

        var key = session.CopyContentKey();
        var tempPath = CreateUniqueSiblingPath(Path.GetFullPath(destinationPath), ".partial");
        try
        {
            var state = await ReadStateWithKeyAsync(session.VaultPath, key, verifyContents: false, cancellationToken);
            EnsureSessionMatches(session, state.Manifest);
            var entry = state.Manifest.Entries.SingleOrDefault(item => item.Id == entryId)
                ?? throw new FileNotFoundException("The selected vault entry no longer exists.");
            if (!state.Records.TryGetValue(entryId, out var record))
                throw new InvalidDataException("The vault manifest references file data that is missing.");

            await using (var output = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, DefaultChunkSize, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await ReadEntryRecordAsync(session.VaultPath, state.Header, record, entry, key, output, cancellationToken);
                await output.FlushAsync(cancellationToken);
                output.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(destinationPath))
                throw new IOException("A file appeared at the selected restore path before Rice2k could finalize the restored file.");
            File.Move(tempPath, destinationPath);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    public static string RecoveryBackupPath(string vaultPath) => Path.GetFullPath(vaultPath) + ".backup";
    public static bool HasRecoveryBackup(string vaultPath) => File.Exists(RecoveryBackupPath(vaultPath));

    internal static string NormalizeVaultPath(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Replace('\\', '/').Trim('/');
        if (normalized.Length == 0 || normalized.Length > 2048)
            throw new ArgumentException("Choose a valid relative path inside the vault.");
        if (Path.IsPathRooted(normalized) || normalized.Contains(':'))
            throw new ArgumentException("Vault paths must be relative and cannot contain a drive or URI prefix.");

        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts.Any(part => part is "." or ".." || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
            throw new ArgumentException("The vault path contains an invalid folder or filename component.");
        return string.Join('/', parts);
    }

    private async Task<UnlockedVaultState> ReadStateWithKeyAsync(
        string vaultPath,
        byte[] key,
        bool verifyContents,
        CancellationToken cancellationToken)
    {
        await using var input = new FileStream(vaultPath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new BinaryReader(input, Encoding.UTF8, leaveOpen: true);
        VaultHeader header;
        try
        {
            header = ReadHeader(reader);
        }
        catch (EndOfStreamException ex)
        {
            throw new InvalidDataException("The vault appears to be truncated.", ex);
        }

        VaultManifest manifest;
        try
        {
            manifest = DecryptManifest(header, key);
        }
        catch (CryptographicException ex)
        {
            throw new CryptographicException("Vault authentication failed. The vault may have been modified or the unlock key is no longer valid.", ex);
        }

        var records = ScanRecordIndex(reader, input.Length);
        ValidateManifestAndRecords(manifest, header, records);

        if (verifyContents)
        {
            foreach (var entry in manifest.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await ReadEntryRecordAsync(vaultPath, header, records[entry.Id], entry, key, Stream.Null, cancellationToken);
            }
        }

        return new UnlockedVaultState(header, manifest, records);
    }

    private static VaultHeader ReadHeader(BinaryReader reader)
    {
        var magic = reader.ReadBytes(Magic.Length);
        if (!magic.SequenceEqual(Magic))
            throw new InvalidDataException("This is not a supported Rice2k secure vault.");

        var version = reader.ReadByte();
        var algorithm = reader.ReadByte();
        var kdf = reader.ReadByte();
        if (version != Version)
            throw new NotSupportedException($"Rice2k vault version {version} is not supported by this build.");
        if (algorithm != AlgorithmXChaCha20Poly1305 || kdf != KdfArgon2Id)
            throw new NotSupportedException("This vault uses an unsupported cryptographic profile.");

        var opsLimit = reader.ReadInt64();
        var memLimit = reader.ReadInt32();
        var chunkSize = reader.ReadInt32();
        if (opsLimit < 3 || opsLimit > MaximumSupportedOpsLimit)
            throw new InvalidDataException("The vault Argon2id operation limit is outside the supported range.");
        if (memLimit < 8 * 1024 * 1024 || memLimit > MaximumSupportedMemLimit)
            throw new InvalidDataException("The vault Argon2id memory limit is outside the supported range.");
        if (chunkSize < 64 * 1024 || chunkSize > MaximumSupportedChunkSize)
            throw new InvalidDataException("The vault chunk size is outside the supported range.");

        var salt = reader.ReadBytes(SaltSize);
        var vaultIdBytes = reader.ReadBytes(16);
        var manifestNonce = reader.ReadBytes(NonceSize);
        if (salt.Length != SaltSize || vaultIdBytes.Length != 16 || manifestNonce.Length != NonceSize)
            throw new InvalidDataException("The vault header is truncated.");

        var vaultId = new Guid(vaultIdBytes);
        if (vaultId == Guid.Empty)
            throw new InvalidDataException("The vault header contains an invalid empty vault identifier.");

        var manifestCipherLength = reader.ReadInt32();
        if (manifestCipherLength < AuthenticationTagSize || manifestCipherLength > MaximumManifestCipherLength)
            throw new InvalidDataException("The encrypted vault manifest length is invalid.");
        var manifestCipher = reader.ReadBytes(manifestCipherLength);
        if (manifestCipher.Length != manifestCipherLength)
            throw new InvalidDataException("The encrypted vault manifest is truncated.");

        var headerAuth = BuildHeaderAuthenticationData(opsLimit, memLimit, chunkSize, salt, vaultId);
        return new VaultHeader(opsLimit, memLimit, chunkSize, salt, vaultId, manifestNonce, manifestCipher, headerAuth);
    }

    private static void WriteHeader(
        BinaryWriter writer,
        long opsLimit,
        int memLimit,
        int chunkSize,
        byte[] salt,
        Guid vaultId,
        byte[] manifestNonce,
        byte[] manifestCipher)
    {
        if (vaultId == Guid.Empty)
            throw new InvalidDataException("Rice2k cannot write a vault with an empty identifier.");
        if (manifestCipher.Length < AuthenticationTagSize || manifestCipher.Length > MaximumManifestCipherLength)
            throw new InvalidDataException("The encrypted vault manifest length is outside the supported format range.");

        writer.Write(Magic);
        writer.Write(Version);
        writer.Write(AlgorithmXChaCha20Poly1305);
        writer.Write(KdfArgon2Id);
        writer.Write(opsLimit);
        writer.Write(memLimit);
        writer.Write(chunkSize);
        writer.Write(salt);
        writer.Write(vaultId.ToByteArray());
        writer.Write(manifestNonce);
        writer.Write(manifestCipher.Length);
        writer.Write(manifestCipher);
    }

    private static VaultManifest DecryptManifest(VaultHeader header, byte[] key)
    {
        var plain = SecretAeadXChaCha20Poly1305.Decrypt(header.ManifestCipher, header.ManifestNonce, key, header.HeaderAuthenticationData);
        try
        {
            return JsonSerializer.Deserialize<VaultManifest>(plain)
                ?? throw new InvalidDataException("The decrypted vault manifest is missing.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("The decrypted vault manifest is invalid.", ex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    private static byte[] EncryptManifest(VaultManifest manifest, byte[] nonce, byte[] key, byte[] headerAuth)
    {
        if (manifest.VaultId == Guid.Empty)
            throw new InvalidDataException("Rice2k cannot encrypt a vault manifest with an empty identifier.");
        if (manifest.Entries is null)
            throw new InvalidDataException("Rice2k cannot encrypt a vault manifest with a missing entry list.");

        var plain = JsonSerializer.SerializeToUtf8Bytes(manifest);
        byte[]? cipher = null;
        try
        {
            if (plain.Length > MaximumManifestPlainLength)
                throw new InvalidDataException("The vault manifest is too large for the supported Rice2k vault format.");

            cipher = SecretAeadXChaCha20Poly1305.Encrypt(plain, nonce, key, headerAuth);
            if (cipher.Length > MaximumManifestCipherLength)
            {
                CryptographicOperations.ZeroMemory(cipher);
                cipher = null;
                throw new InvalidOperationException("Rice2k generated a vault manifest outside its supported format limit.");
            }
            return cipher;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    private static Dictionary<Guid, VaultRecordIndex> ScanRecordIndex(BinaryReader reader, long fileLength)
    {
        var records = new Dictionary<Guid, VaultRecordIndex>();
        while (reader.BaseStream.Position < fileLength)
        {
            var recordOffset = reader.BaseStream.Position;
            var idBytes = reader.ReadBytes(16);
            if (idBytes.Length != 16)
                throw new InvalidDataException("A vault entry record is truncated.");
            var entryId = new Guid(idBytes);
            if (entryId == Guid.Empty)
                throw new InvalidDataException("The vault contains an entry record with an empty identifier.");

            var payloadLength = reader.ReadInt64();
            if (payloadLength < sizeof(long) || payloadLength > MaximumRecordPayloadLength)
                throw new InvalidDataException("A vault entry record has an invalid payload length.");

            var payloadOffset = reader.BaseStream.Position;
            if (payloadLength > fileLength - payloadOffset)
                throw new InvalidDataException("A vault entry record extends beyond the end of the vault.");
            if (!records.TryAdd(entryId, new VaultRecordIndex(entryId, recordOffset, payloadOffset, payloadLength, 16 + sizeof(long) + payloadLength)))
                throw new InvalidDataException("The vault contains duplicate entry identifiers.");

            reader.BaseStream.Position = payloadOffset + payloadLength;
        }
        return records;
    }

    private static void ValidateManifestAndRecords(
        VaultManifest manifest,
        VaultHeader header,
        IReadOnlyDictionary<Guid, VaultRecordIndex> records)
    {
        if (manifest.VaultId == Guid.Empty || manifest.VaultId != header.VaultId)
            throw new InvalidDataException("The authenticated vault manifest does not match a valid public vault identifier.");
        if (manifest.ChunkSize != header.ChunkSize)
            throw new InvalidDataException("The authenticated vault manifest does not match the vault chunk size.");
        if (manifest.Sequence < 0)
            throw new InvalidDataException("The vault manifest contains an invalid sequence number.");
        if (manifest.Entries is null)
            throw new InvalidDataException("The vault manifest is missing its entry list.");

        var ids = new HashSet<Guid>();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in manifest.Entries)
        {
            if (entry.Id == Guid.Empty || !ids.Add(entry.Id))
                throw new InvalidDataException("The vault manifest contains a duplicate or invalid entry identifier.");
            if (entry.Length < 0)
                throw new InvalidDataException("The vault manifest contains an invalid file length.");
            var normalized = NormalizeVaultPath(entry.Path);
            if (!string.Equals(normalized, entry.Path, StringComparison.Ordinal) || !paths.Add(entry.Path))
                throw new InvalidDataException("The vault manifest contains an invalid or duplicate path.");
            if (!records.ContainsKey(entry.Id))
                throw new InvalidDataException("The vault manifest references file data that is missing.");
        }

        if (records.Count != manifest.Entries.Count)
            throw new InvalidDataException("The vault contains encrypted file records that are not present in the authenticated manifest.");
    }

    private async Task ReadEntryRecordAsync(
        string vaultPath,
        VaultHeader header,
        VaultRecordIndex record,
        VaultEntryInfo entry,
        byte[] key,
        Stream destination,
        CancellationToken cancellationToken)
    {
        await using var input = new FileStream(vaultPath, FileMode.Open, FileAccess.Read, FileShare.Read, DefaultChunkSize, FileOptions.Asynchronous | FileOptions.RandomAccess);
        input.Position = record.PayloadOffset;
        using var reader = new BinaryReader(input, Encoding.UTF8, leaveOpen: true);

        var chunkCount = reader.ReadInt64();
        var expectedChunkCount = ComputeExpectedChunkCount(entry.Length, header.ChunkSize);
        if (chunkCount != expectedChunkCount)
            throw new InvalidDataException("The vault entry contains an inconsistent encrypted chunk count.");

        long written = 0;
        for (long expectedIndex = 0; expectedIndex < chunkCount; expectedIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (input.Position >= record.PayloadOffset + record.PayloadLength)
                throw new InvalidDataException("The vault entry ended before all encrypted chunks were read.");

            var index = reader.ReadInt64();
            var cipherLength = reader.ReadInt32();
            if (index != expectedIndex)
                throw new InvalidDataException("Vault file chunks are missing, duplicated, or out of order.");
            if (cipherLength < AuthenticationTagSize || cipherLength > header.ChunkSize + 64)
                throw new InvalidDataException("A vault chunk has an invalid ciphertext length.");

            var nonce = reader.ReadBytes(NonceSize);
            var cipher = reader.ReadBytes(cipherLength);
            if (nonce.Length != NonceSize || cipher.Length != cipherLength)
                throw new InvalidDataException("A vault chunk is truncated.");

            byte[] plain;
            try
            {
                plain = SecretAeadXChaCha20Poly1305.Decrypt(cipher, nonce, key, BuildChunkAad(header.HeaderAuthenticationData, entry.Id, index));
            }
            catch (CryptographicException ex)
            {
                throw new CryptographicException($"Authentication failed for vault entry '{entry.Path}'. The vault may be damaged or modified.", ex);
            }

            try
            {
                await destination.WriteAsync(plain, cancellationToken);
                written += plain.Length;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plain);
            }
        }

        if (input.Position != record.PayloadOffset + record.PayloadLength || written != entry.Length)
            throw new InvalidDataException("The vault entry length does not match its authenticated manifest.");
    }

    private static long ComputeExpectedChunkCount(long length, int chunkSize)
    {
        if (length < 0 || chunkSize <= 0)
            throw new InvalidDataException("The vault contains invalid chunk parameters.");
        return length == 0 ? 0 : 1 + ((length - 1) / chunkSize);
    }

    private static byte[] DeriveKey(byte[] passwordBytes, byte[] salt, long opsLimit, int memLimit) =>
        PasswordHash.ArgonHashBinary(passwordBytes, salt, opsLimit, memLimit, 32, PasswordHash.ArgonAlgorithm.Argon_2ID13);

    private static byte[] BuildHeaderAuthenticationData(long opsLimit, int memLimit, int chunkSize, byte[] salt, Guid vaultId)
    {
        var data = new byte[Magic.Length + 3 + sizeof(long) + sizeof(int) + sizeof(int) + SaltSize + 16];
        var offset = 0;
        Magic.CopyTo(data, offset);
        offset += Magic.Length;
        data[offset++] = Version;
        data[offset++] = AlgorithmXChaCha20Poly1305;
        data[offset++] = KdfArgon2Id;
        BinaryPrimitives.WriteInt64LittleEndian(data.AsSpan(offset, sizeof(long)), opsLimit);
        offset += sizeof(long);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset, sizeof(int)), memLimit);
        offset += sizeof(int);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset, sizeof(int)), chunkSize);
        offset += sizeof(int);
        salt.CopyTo(data, offset);
        offset += SaltSize;
        vaultId.ToByteArray().CopyTo(data, offset);
        return data;
    }

    private static byte[] BuildChunkAad(byte[] headerAuth, Guid entryId, long chunkIndex)
    {
        var headerHash = SHA256.HashData(headerAuth);
        try
        {
            var aad = new byte[headerHash.Length + 16 + sizeof(long)];
            headerHash.CopyTo(aad, 0);
            entryId.ToByteArray().CopyTo(aad, headerHash.Length);
            BinaryPrimitives.WriteInt64LittleEndian(aad.AsSpan(headerHash.Length + 16, sizeof(long)), chunkIndex);
            return aad;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(headerHash);
        }
    }

    private static void EnsureSessionMatches(SecureVaultSession session, VaultManifest manifest)
    {
        if (manifest.VaultId != session.VaultId)
            throw new InvalidDataException("The vault file no longer matches the unlocked session.");
        if (manifest.Sequence != session.Sequence)
            throw new IOException("The vault changed after it was unlocked. Lock and reopen it before making further changes.");
    }

    private static void ValidateNewVaultPath(string destinationPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        var fullPath = Path.GetFullPath(destinationPath);
        if (File.Exists(fullPath))
            throw new IOException("A file already exists at the selected vault path. Rice2k will not overwrite it automatically.");
        var directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            throw new DirectoryNotFoundException("The selected vault folder does not exist.");
    }

    private static void ValidateNewPassword(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        if (password.Length < MinimumPasswordLength)
            throw new ArgumentException($"Vault passwords must contain at least {MinimumPasswordLength} characters.", nameof(password));
    }

    private static string CreateUniqueSiblingPath(string destinationPath, string suffix)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(destinationPath))
            ?? throw new DirectoryNotFoundException("The destination folder could not be determined.");
        var filename = Path.GetFileName(destinationPath);
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var random = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
            var candidate = Path.Combine(directory, $".{filename}.{random}{suffix}");
            if (!File.Exists(candidate))
                return candidate;
        }
        throw new IOException("Rice2k could not allocate a unique temporary vault filename. Try again.");
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best effort only. Never remove the user's source files here.
        }
    }
}
