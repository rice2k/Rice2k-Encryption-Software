using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Rice2k.Encryption.Models;
using Sodium;

namespace Rice2k.Encryption.Services;

/// <summary>
/// Password + key-file protection for .r2kenc files.
/// Uses a separate v2 magic/header so password-only v1 containers remain fully compatible.
/// </summary>
public sealed class KeyFileEncryptionService
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("R2KENC02");
    private static readonly byte[] KeyMixContext = Encoding.ASCII.GetBytes("Rice2k-R2KENC-KeyFile-v1");
    private const byte Version = 2;
    private const byte AlgorithmXChaCha20Poly1305 = 1;
    private const byte KdfArgon2Id = 1;
    private const byte ProtectionPasswordPlusKeyFile = 2;
    private const long DefaultOpsLimit = 4;
    private const int DefaultMemLimit = 64 * 1024 * 1024;
    private const int DefaultChunkSize = 4 * 1024 * 1024;
    private const int SaltSize = 16;
    private const int NonceSize = 24;
    private const int AuthenticationTagSize = 16;
    private const int MinimumEncryptionPasswordLength = 12;
    private const int MaximumOriginalNameCharacters = 1024;
    private const long MaximumSupportedOpsLimit = 10;
    private const int MaximumSupportedMemLimit = 256 * 1024 * 1024;
    private const int MaximumSupportedChunkSize = 64 * 1024 * 1024;
    private const int MaximumMetadataCipherLength = 64 * 1024;
    private const int MaximumMetadataPlainLength = MaximumMetadataCipherLength - AuthenticationTagSize;
    private const int MaximumFingerprintBytes = 64;

    private readonly AsyncPauseGate _pauseGate = new();

    private sealed record FileMetadata(
        string OriginalName,
        long OriginalLength,
        long ChunkCount,
        int ChunkSize,
        DateTimeOffset CreatedUtc,
        string RequiredKeyFingerprint);

    private sealed record Header(
        long OpsLimit,
        int MemLimit,
        int ChunkSize,
        byte[] Salt,
        string KeyFingerprint,
        byte[] MetadataNonce,
        byte[] MetadataCipher,
        byte[] HeaderAuthenticationData);

    public bool IsPaused => _pauseGate.IsPaused;
    public void Pause() => _pauseGate.Pause();
    public void Resume() => _pauseGate.Resume();

    public static bool IsKeyFileProtectedContainer(string path)
    {
        if (!File.Exists(path))
            return false;

        try
        {
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (input.Length < Magic.Length)
                return false;

            Span<byte> magic = stackalloc byte[Magic.Length];
            return input.Read(magic) == Magic.Length && magic.SequenceEqual(Magic);
        }
        catch
        {
            return false;
        }
    }

    public static string? TryGetRequiredKeyFingerprint(string path)
    {
        if (!IsKeyFileProtectedContainer(path))
            return null;

        try
        {
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new BinaryReader(input, Encoding.UTF8, leaveOpen: true);
            _ = reader.ReadBytes(Magic.Length);
            var version = reader.ReadByte();
            var algorithm = reader.ReadByte();
            var kdf = reader.ReadByte();
            var protection = reader.ReadByte();
            if (version != Version || algorithm != AlgorithmXChaCha20Poly1305 || kdf != KdfArgon2Id || protection != ProtectionPasswordPlusKeyFile)
                return null;

            var ops = reader.ReadInt64();
            var mem = reader.ReadInt32();
            var chunk = reader.ReadInt32();
            if (ops < 3 || ops > MaximumSupportedOpsLimit ||
                mem < 8 * 1024 * 1024 || mem > MaximumSupportedMemLimit ||
                chunk < 64 * 1024 || chunk > MaximumSupportedChunkSize)
                return null;

            if (reader.ReadBytes(SaltSize).Length != SaltSize)
                return null;

            var fingerprintLength = reader.ReadByte();
            if (fingerprintLength == 0 || fingerprintLength > MaximumFingerprintBytes)
                return null;

            var fingerprintBytes = reader.ReadBytes(fingerprintLength);
            if (fingerprintBytes.Length != fingerprintLength)
                return null;

            return Encoding.ASCII.GetString(fingerprintBytes);
        }
        catch
        {
            return null;
        }
    }

    public async Task EncryptFileAsync(
        string sourcePath,
        string destinationPath,
        string password,
        ManagedKey managedKey,
        IProgress<CryptoProgress>? progress = null,
        CancellationToken cancellationToken = default,
        bool verifyAfterEncrypt = true)
    {
        ArgumentNullException.ThrowIfNull(managedKey);
        ValidateSourceAndDestination(sourcePath, destinationPath, password, requireStrongPassword: true);
        _pauseGate.Resume();

        var sourceInfo = new FileInfo(sourcePath);
        if (!IsValidOriginalName(sourceInfo.Name))
            throw new IOException("The source filename cannot be represented safely in Rice2k key-file encrypted metadata.");

        var tempPath = CreateUniqueTempPath(destinationPath);
        var passwordBytes = Encoding.UTF8.GetBytes(password);
        var keyFileSecret = managedKey.CopySecretKey();
        byte[]? key = null;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var salt = PasswordHash.ArgonGenerateSalt();
            key = DeriveCompositeKey(passwordBytes, keyFileSecret, salt, DefaultOpsLimit, DefaultMemLimit);

            var chunkCount = ComputeExpectedChunkCount(sourceInfo.Length, DefaultChunkSize);

            var metadata = new FileMetadata(
                sourceInfo.Name,
                sourceInfo.Length,
                chunkCount,
                DefaultChunkSize,
                DateTimeOffset.UtcNow,
                managedKey.Fingerprint);

            var headerAuth = BuildHeaderAuthenticationData(
                DefaultOpsLimit,
                DefaultMemLimit,
                DefaultChunkSize,
                salt,
                managedKey.Fingerprint);

            var metadataNonce = SecretAeadXChaCha20Poly1305.GenerateNonce();
            var metadataPlain = JsonSerializer.SerializeToUtf8Bytes(metadata);
            byte[] metadataCipher;
            try
            {
                if (metadataPlain.Length > MaximumMetadataPlainLength)
                    throw new InvalidDataException("The encrypted-file metadata is too large for the supported R2KENC02 format.");

                metadataCipher = SecretAeadXChaCha20Poly1305.Encrypt(metadataPlain, metadataNonce, key, headerAuth);
                if (metadataCipher.Length > MaximumMetadataCipherLength)
                    throw new InvalidOperationException("Rice2k generated R2KENC02 metadata outside its supported format limit.");
            }
            finally
            {
                CryptographicOperations.ZeroMemory(metadataPlain);
            }

            await using (var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, DefaultChunkSize, FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var output = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, DefaultChunkSize, FileOptions.Asynchronous | FileOptions.SequentialScan))
            using (var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true))
            {
                WriteHeader(writer, salt, managedKey.Fingerprint, metadataNonce, metadataCipher);
                writer.Flush();

                var buffer = new byte[DefaultChunkSize];
                long processed = 0;
                long index = 0;
                try
                {
                    while (true)
                    {
                        await _pauseGate.WaitIfPausedAsync(cancellationToken);
                        cancellationToken.ThrowIfCancellationRequested();

                        var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                        if (read == 0)
                            break;

                        var plainChunk = buffer.AsSpan(0, read).ToArray();
                        try
                        {
                            var nonce = SecretAeadXChaCha20Poly1305.GenerateNonce();
                            var aad = BuildChunkAad(headerAuth, index);
                            var cipher = SecretAeadXChaCha20Poly1305.Encrypt(plainChunk, nonce, key, aad);
                            writer.Write(index);
                            writer.Write(cipher.Length);
                            writer.Write(nonce);
                            writer.Write(cipher);
                            writer.Flush();

                            processed += read;
                            index++;
                            Report(progress, processed, sourceInfo.Length, "Encrypting with password + key file", stopwatch);
                        }
                        finally
                        {
                            CryptographicOperations.ZeroMemory(plainChunk);
                        }
                    }
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(buffer);
                }

                await _pauseGate.WaitIfPausedAsync(cancellationToken);
                await output.FlushAsync(cancellationToken);
                output.Flush(flushToDisk: true);
            }

            if (verifyAfterEncrypt)
            {
                await _pauseGate.WaitIfPausedAsync(cancellationToken);
                Report(progress, sourceInfo.Length, sourceInfo.Length, "Verifying encrypted data", stopwatch);
                await VerifyEncryptedFileAsync(tempPath, password, managedKey, cancellationToken);
            }

            await _pauseGate.WaitIfPausedAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(destinationPath))
                throw new IOException("The destination file already exists. Rice2k will not overwrite it automatically.");

            File.Move(tempPath, destinationPath);
            Report(progress, sourceInfo.Length, sourceInfo.Length, "Complete", stopwatch);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
        finally
        {
            _pauseGate.Resume();
            CryptographicOperations.ZeroMemory(passwordBytes);
            CryptographicOperations.ZeroMemory(keyFileSecret);
            if (key is not null)
                CryptographicOperations.ZeroMemory(key);
        }
    }

    public async Task DecryptFileAsync(
        string sourcePath,
        string destinationPath,
        string password,
        ManagedKey managedKey,
        IProgress<CryptoProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(managedKey);
        ValidateSourceAndDestination(sourcePath, destinationPath, password, requireStrongPassword: false);
        _pauseGate.Resume();
        var tempPath = CreateUniqueTempPath(destinationPath);

        try
        {
            await using (var output = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, DefaultChunkSize, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await DecryptCoreAsync(sourcePath, password, managedKey, output, progress, cancellationToken);
                await _pauseGate.WaitIfPausedAsync(cancellationToken);
                await output.FlushAsync(cancellationToken);
                output.Flush(flushToDisk: true);
            }

            await _pauseGate.WaitIfPausedAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(destinationPath))
                throw new IOException("The destination file already exists. Rice2k will not overwrite it automatically.");

            File.Move(tempPath, destinationPath);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
        finally
        {
            _pauseGate.Resume();
        }
    }

    public Task VerifyEncryptedFileAsync(string sourcePath, string password, ManagedKey managedKey, CancellationToken cancellationToken = default) =>
        DecryptCoreAsync(sourcePath, password, managedKey, Stream.Null, null, cancellationToken);

    private async Task DecryptCoreAsync(
        string sourcePath,
        string password,
        ManagedKey managedKey,
        Stream destination,
        IProgress<CryptoProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("The encrypted file could not be found.", sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        var passwordBytes = Encoding.UTF8.GetBytes(password);
        var keyFileSecret = managedKey.CopySecretKey();
        byte[]? key = null;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            await using var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, DefaultChunkSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var reader = new BinaryReader(input, Encoding.UTF8, leaveOpen: true);
            Header header;
            try
            {
                header = ReadHeader(reader);
            }
            catch (EndOfStreamException ex)
            {
                throw new InvalidDataException("The password + key-file encrypted header is truncated.", ex);
            }

            if (!string.Equals(header.KeyFingerprint, managedKey.Fingerprint, StringComparison.Ordinal))
                throw new CryptographicException($"This encrypted file requires key fingerprint {header.KeyFingerprint}. The selected key package provides {managedKey.Fingerprint}.");

            await _pauseGate.WaitIfPausedAsync(cancellationToken);
            key = DeriveCompositeKey(passwordBytes, keyFileSecret, header.Salt, header.OpsLimit, header.MemLimit);
            await _pauseGate.WaitIfPausedAsync(cancellationToken);

            FileMetadata metadata;
            try
            {
                var metadataPlain = SecretAeadXChaCha20Poly1305.Decrypt(header.MetadataCipher, header.MetadataNonce, key, header.HeaderAuthenticationData);
                try
                {
                    metadata = JsonSerializer.Deserialize<FileMetadata>(metadataPlain)
                        ?? throw new InvalidDataException("The encrypted file metadata is missing.");
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(metadataPlain);
                }
            }
            catch (CryptographicException ex)
            {
                throw new CryptographicException("Rice2k could not authenticate this file. The file password, key package, or key-package password may be wrong, or the encrypted file may have been modified.", ex);
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException("The encrypted file metadata is invalid.", ex);
            }

            ValidateMetadata(metadata, header);
            long expectedIndex = 0;
            long written = 0;

            while (input.Position < input.Length)
            {
                await _pauseGate.WaitIfPausedAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                long index;
                int cipherLength;
                byte[] nonce;
                byte[] cipher;
                try
                {
                    index = reader.ReadInt64();
                    cipherLength = reader.ReadInt32();
                    if (cipherLength < AuthenticationTagSize || cipherLength > metadata.ChunkSize + 64)
                        throw new InvalidDataException("The encrypted chunk length is invalid.");

                    nonce = reader.ReadBytes(NonceSize);
                    if (nonce.Length != NonceSize)
                        throw new EndOfStreamException("The encrypted file ended while reading a nonce.");

                    cipher = reader.ReadBytes(cipherLength);
                    if (cipher.Length != cipherLength)
                        throw new EndOfStreamException("The encrypted file ended while reading an encrypted chunk.");
                }
                catch (EndOfStreamException ex)
                {
                    throw new InvalidDataException("The encrypted file appears to be truncated.", ex);
                }

                if (index != expectedIndex)
                    throw new InvalidDataException("Encrypted chunks are missing, duplicated, or out of order.");

                byte[] plain;
                try
                {
                    var aad = BuildChunkAad(header.HeaderAuthenticationData, index);
                    plain = SecretAeadXChaCha20Poly1305.Decrypt(cipher, nonce, key, aad);
                }
                catch (CryptographicException ex)
                {
                    throw new CryptographicException("Encrypted data authentication failed. The file may be damaged or modified.", ex);
                }

                try
                {
                    await destination.WriteAsync(plain, cancellationToken);
                    written += plain.Length;
                    expectedIndex++;
                    Report(progress, written, metadata.OriginalLength, "Decrypting with password + key file", stopwatch);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(plain);
                }
            }

            await _pauseGate.WaitIfPausedAsync(cancellationToken);
            if (expectedIndex != metadata.ChunkCount || written != metadata.OriginalLength)
                throw new InvalidDataException("The encrypted file is incomplete or contains unexpected data.");

            Report(progress, written, metadata.OriginalLength, "Complete", stopwatch);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
            CryptographicOperations.ZeroMemory(keyFileSecret);
            if (key is not null)
                CryptographicOperations.ZeroMemory(key);
        }
    }

    private static void WriteHeader(BinaryWriter writer, byte[] salt, string fingerprint, byte[] metadataNonce, byte[] metadataCipher)
    {
        var fingerprintBytes = Encoding.ASCII.GetBytes(fingerprint);
        if (fingerprintBytes.Length == 0 || fingerprintBytes.Length > MaximumFingerprintBytes)
            throw new InvalidDataException("The selected key fingerprint cannot be represented in the encrypted header.");
        if (metadataCipher.Length < AuthenticationTagSize || metadataCipher.Length > MaximumMetadataCipherLength)
            throw new InvalidDataException("The encrypted metadata length is outside the supported R2KENC02 format range.");

        writer.Write(Magic);
        writer.Write(Version);
        writer.Write(AlgorithmXChaCha20Poly1305);
        writer.Write(KdfArgon2Id);
        writer.Write(ProtectionPasswordPlusKeyFile);
        writer.Write(DefaultOpsLimit);
        writer.Write(DefaultMemLimit);
        writer.Write(DefaultChunkSize);
        writer.Write(salt);
        writer.Write((byte)fingerprintBytes.Length);
        writer.Write(fingerprintBytes);
        writer.Write(metadataNonce);
        writer.Write(metadataCipher.Length);
        writer.Write(metadataCipher);
    }

    private static Header ReadHeader(BinaryReader reader)
    {
        var magic = reader.ReadBytes(Magic.Length);
        if (!magic.SequenceEqual(Magic))
            throw new InvalidDataException("This is not a Rice2k password + key-file encrypted container.");

        var version = reader.ReadByte();
        var algorithm = reader.ReadByte();
        var kdf = reader.ReadByte();
        var protection = reader.ReadByte();
        if (version != Version)
            throw new NotSupportedException($"Rice2k encrypted-file version {version} is not supported by this build.");
        if (algorithm != AlgorithmXChaCha20Poly1305 || kdf != KdfArgon2Id || protection != ProtectionPasswordPlusKeyFile)
            throw new NotSupportedException("This encrypted file uses an unsupported protection configuration.");

        var opsLimit = reader.ReadInt64();
        var memLimit = reader.ReadInt32();
        var chunkSize = reader.ReadInt32();
        if (opsLimit < 3 || opsLimit > MaximumSupportedOpsLimit)
            throw new InvalidDataException("The Argon2id operation limit is outside the supported range.");
        if (memLimit < 8 * 1024 * 1024 || memLimit > MaximumSupportedMemLimit)
            throw new InvalidDataException("The Argon2id memory limit is outside the supported range.");
        if (chunkSize < 64 * 1024 || chunkSize > MaximumSupportedChunkSize)
            throw new InvalidDataException("The encrypted file chunk size is outside the supported range.");

        var salt = reader.ReadBytes(SaltSize);
        if (salt.Length != SaltSize)
            throw new InvalidDataException("The encrypted file header is truncated.");

        var fingerprintLength = reader.ReadByte();
        if (fingerprintLength == 0 || fingerprintLength > MaximumFingerprintBytes)
            throw new InvalidDataException("The key fingerprint length is invalid.");
        var fingerprintBytes = reader.ReadBytes(fingerprintLength);
        if (fingerprintBytes.Length != fingerprintLength)
            throw new InvalidDataException("The encrypted file key fingerprint is truncated.");
        var fingerprint = Encoding.ASCII.GetString(fingerprintBytes);

        var metadataNonce = reader.ReadBytes(NonceSize);
        if (metadataNonce.Length != NonceSize)
            throw new InvalidDataException("The encrypted file header is truncated.");
        var metadataCipherLength = reader.ReadInt32();
        if (metadataCipherLength < AuthenticationTagSize || metadataCipherLength > MaximumMetadataCipherLength)
            throw new InvalidDataException("The encrypted metadata length is invalid.");
        var metadataCipher = reader.ReadBytes(metadataCipherLength);
        if (metadataCipher.Length != metadataCipherLength)
            throw new InvalidDataException("The encrypted file metadata is truncated.");

        var headerAuth = BuildHeaderAuthenticationData(opsLimit, memLimit, chunkSize, salt, fingerprint);
        return new Header(opsLimit, memLimit, chunkSize, salt, fingerprint, metadataNonce, metadataCipher, headerAuth);
    }

    private static void ValidateMetadata(FileMetadata metadata, Header header)
    {
        if (!IsValidOriginalName(metadata.OriginalName))
            throw new InvalidDataException("The encrypted file metadata contains an invalid original filename.");
        if (metadata.OriginalLength < 0 || metadata.ChunkCount < 0)
            throw new InvalidDataException("The encrypted file metadata contains invalid lengths.");
        if (metadata.ChunkSize != header.ChunkSize || !string.Equals(metadata.RequiredKeyFingerprint, header.KeyFingerprint, StringComparison.Ordinal))
            throw new InvalidDataException("The encrypted file metadata does not match its authenticated header.");

        var expectedChunkCount = ComputeExpectedChunkCount(metadata.OriginalLength, metadata.ChunkSize);
        if (metadata.ChunkCount != expectedChunkCount)
            throw new InvalidDataException("The encrypted file metadata contains an inconsistent chunk count.");
    }

    private static long ComputeExpectedChunkCount(long length, int chunkSize)
    {
        if (length < 0 || chunkSize <= 0)
            throw new InvalidDataException("The encrypted file metadata contains invalid chunk parameters.");
        return length == 0 ? 0 : 1 + ((length - 1) / chunkSize);
    }

    private static bool IsValidOriginalName(string? name) =>
        !string.IsNullOrWhiteSpace(name) &&
        name.Length <= MaximumOriginalNameCharacters &&
        name.IndexOf('/') < 0 &&
        name.IndexOf('\\') < 0;

    private static byte[] DeriveCompositeKey(byte[] passwordBytes, byte[] keyFileSecret, byte[] salt, long opsLimit, int memLimit)
    {
        byte[]? passwordKey = null;
        byte[]? mixInput = null;
        try
        {
            passwordKey = PasswordHash.ArgonHashBinary(passwordBytes, salt, opsLimit, memLimit, 32, PasswordHash.ArgonAlgorithm.Argon_2ID13);
            mixInput = new byte[KeyMixContext.Length + keyFileSecret.Length];
            KeyMixContext.CopyTo(mixInput, 0);
            keyFileSecret.CopyTo(mixInput, KeyMixContext.Length);
            using var hmac = new HMACSHA256(passwordKey);
            return hmac.ComputeHash(mixInput);
        }
        finally
        {
            if (passwordKey is not null)
                CryptographicOperations.ZeroMemory(passwordKey);
            if (mixInput is not null)
                CryptographicOperations.ZeroMemory(mixInput);
        }
    }

    private static byte[] BuildHeaderAuthenticationData(long opsLimit, int memLimit, int chunkSize, byte[] salt, string fingerprint)
    {
        var fingerprintBytes = Encoding.ASCII.GetBytes(fingerprint);
        var data = new byte[Magic.Length + 4 + sizeof(long) + sizeof(int) + sizeof(int) + SaltSize + 1 + fingerprintBytes.Length];
        var offset = 0;
        Magic.CopyTo(data, offset);
        offset += Magic.Length;
        data[offset++] = Version;
        data[offset++] = AlgorithmXChaCha20Poly1305;
        data[offset++] = KdfArgon2Id;
        data[offset++] = ProtectionPasswordPlusKeyFile;
        BinaryPrimitives.WriteInt64LittleEndian(data.AsSpan(offset, sizeof(long)), opsLimit);
        offset += sizeof(long);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset, sizeof(int)), memLimit);
        offset += sizeof(int);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset, sizeof(int)), chunkSize);
        offset += sizeof(int);
        salt.CopyTo(data, offset);
        offset += SaltSize;
        data[offset++] = (byte)fingerprintBytes.Length;
        fingerprintBytes.CopyTo(data, offset);
        return data;
    }

    private static byte[] BuildChunkAad(byte[] headerAuth, long index)
    {
        var headerHash = SHA256.HashData(headerAuth);
        try
        {
            var aad = new byte[headerHash.Length + sizeof(long)];
            headerHash.CopyTo(aad, 0);
            BinaryPrimitives.WriteInt64LittleEndian(aad.AsSpan(headerHash.Length, sizeof(long)), index);
            return aad;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(headerHash);
        }
    }

    private static void ValidateSourceAndDestination(string sourcePath, string destinationPath, string password, bool requireStrongPassword)
    {
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("The source file could not be found.", sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        if (requireStrongPassword && password.Length < MinimumEncryptionPasswordLength)
            throw new ArgumentException($"Encryption passwords must contain at least {MinimumEncryptionPasswordLength} characters.", nameof(password));
        if (string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(destinationPath), StringComparison.OrdinalIgnoreCase))
            throw new IOException("Source and destination must be different files.");
        if (File.Exists(destinationPath))
            throw new IOException("The destination file already exists. Rice2k will not overwrite it automatically.");
    }

    private static string CreateUniqueTempPath(string destinationPath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(destinationPath))
            ?? throw new DirectoryNotFoundException("The destination folder could not be determined.");
        var filename = Path.GetFileName(destinationPath);
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var suffix = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
            var candidate = Path.Combine(directory, $".{filename}.{suffix}.partial");
            if (!File.Exists(candidate))
                return candidate;
        }
        throw new IOException("Rice2k could not allocate a unique temporary output name. Try the operation again.");
    }

    private static void Report(IProgress<CryptoProgress>? progress, long processed, long total, string stage, Stopwatch stopwatch)
    {
        if (progress is null)
            return;
        var seconds = Math.Max(stopwatch.Elapsed.TotalSeconds, 0.001);
        progress.Report(new CryptoProgress(processed, total, stage, stopwatch.Elapsed, processed / seconds));
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
            // Best effort cleanup. The original source is never removed here.
        }
    }
}
