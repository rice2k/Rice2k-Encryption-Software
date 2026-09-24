using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Rice2k.Encryption.Models;
using Sodium;

namespace Rice2k.Encryption.Services;

public sealed class FileEncryptionService
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("R2KENC01");
    private const byte Version = 1;
    private const byte AlgorithmXChaCha20Poly1305 = 1;
    private const byte KdfArgon2Id = 1;
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

    private readonly AsyncPauseGate _pauseGate = new();

    private sealed record FileMetadata(
        string OriginalName,
        long OriginalLength,
        long ChunkCount,
        int ChunkSize,
        DateTimeOffset CreatedUtc);

    private sealed record Header(
        long OpsLimit,
        int MemLimit,
        int ChunkSize,
        byte[] Salt,
        byte[] MetadataNonce,
        byte[] MetadataCipher,
        byte[] HeaderAuthenticationData);

    public bool IsPaused => _pauseGate.IsPaused;

    public void Pause() => _pauseGate.Pause();

    public void Resume() => _pauseGate.Resume();

    public async Task EncryptFileAsync(
        string sourcePath,
        string destinationPath,
        string password,
        IProgress<CryptoProgress>? progress = null,
        CancellationToken cancellationToken = default,
        bool verifyAfterEncrypt = true)
    {
        ValidateSourceAndDestination(sourcePath, destinationPath, password, requireStrongPassword: true);
        _pauseGate.Resume();

        var sourceInfo = new FileInfo(sourcePath);
        if (!IsValidOriginalName(sourceInfo.Name))
            throw new IOException("The source filename cannot be represented safely in Rice2k encrypted-file metadata.");

        var tempPath = CreateUniqueTempPath(destinationPath);
        byte[]? key = null;
        var passwordBytes = Encoding.UTF8.GetBytes(password);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var salt = PasswordHash.ArgonGenerateSalt();
            key = DeriveKey(passwordBytes, salt, DefaultOpsLimit, DefaultMemLimit);

            var chunkCount = ComputeExpectedChunkCount(sourceInfo.Length, DefaultChunkSize);

            var metadata = new FileMetadata(
                sourceInfo.Name,
                sourceInfo.Length,
                chunkCount,
                DefaultChunkSize,
                DateTimeOffset.UtcNow);

            var headerAuth = BuildHeaderAuthenticationData(
                DefaultOpsLimit,
                DefaultMemLimit,
                DefaultChunkSize,
                salt);

            var metadataNonce = SecretAeadXChaCha20Poly1305.GenerateNonce();
            var metadataPlain = JsonSerializer.SerializeToUtf8Bytes(metadata);
            byte[] metadataCipher;
            try
            {
                if (metadataPlain.Length > MaximumMetadataPlainLength)
                    throw new InvalidDataException("The encrypted-file metadata is too large for the supported R2KENC01 format.");

                metadataCipher = SecretAeadXChaCha20Poly1305.Encrypt(
                    metadataPlain,
                    metadataNonce,
                    key,
                    headerAuth);
                if (metadataCipher.Length > MaximumMetadataCipherLength)
                    throw new InvalidOperationException("Rice2k generated R2KENC01 metadata outside its supported format limit.");
            }
            finally
            {
                CryptographicOperations.ZeroMemory(metadataPlain);
            }

            await using (var input = new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                DefaultChunkSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var output = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                DefaultChunkSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            using (var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true))
            {
                WriteHeader(writer, salt, metadataNonce, metadataCipher);
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

                        var read = await ChunkReadHelper.ReadFullChunkAsync(input, buffer.AsMemory(), cancellationToken);
                        if (read == 0)
                            break;

                        var chunk = buffer.AsSpan(0, read).ToArray();
                        try
                        {
                            var nonce = SecretAeadXChaCha20Poly1305.GenerateNonce();
                            var aad = BuildChunkAad(headerAuth, index);
                            var cipher = SecretAeadXChaCha20Poly1305.Encrypt(chunk, nonce, key, aad);

                            writer.Write(index);
                            writer.Write(cipher.Length);
                            writer.Write(nonce);
                            writer.Write(cipher);
                            writer.Flush();

                            processed += read;
                            index++;
                            Report(progress, processed, sourceInfo.Length, "Encrypting", stopwatch);
                        }
                        finally
                        {
                            CryptographicOperations.ZeroMemory(chunk);
                        }
                    }
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(buffer);
                }

                if (processed != sourceInfo.Length || index != chunkCount)
                    throw new IOException($"'{sourceInfo.Name}' changed while Rice2k was reading it. The partial encrypted output will be discarded.");

                await _pauseGate.WaitIfPausedAsync(cancellationToken);
                await output.FlushAsync(cancellationToken);
                output.Flush(flushToDisk: true);
            }

            if (verifyAfterEncrypt)
            {
                await _pauseGate.WaitIfPausedAsync(cancellationToken);
                Report(progress, sourceInfo.Length, sourceInfo.Length, "Verifying encrypted data", stopwatch);
                await VerifyEncryptedFileAsync(tempPath, password, cancellationToken);
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
            if (key is not null)
                CryptographicOperations.ZeroMemory(key);
        }
    }

    public async Task DecryptFileAsync(
        string sourcePath,
        string destinationPath,
        string password,
        IProgress<CryptoProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ValidateSourceAndDestination(sourcePath, destinationPath, password, requireStrongPassword: false);
        _pauseGate.Resume();

        var tempPath = CreateUniqueTempPath(destinationPath);

        try
        {
            await using (var output = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                DefaultChunkSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await DecryptCoreAsync(sourcePath, password, output, progress, cancellationToken);
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

    public async Task VerifyEncryptedFileAsync(
        string sourcePath,
        string password,
        CancellationToken cancellationToken = default)
    {
        await DecryptCoreAsync(sourcePath, password, Stream.Null, null, cancellationToken);
    }

    private async Task DecryptCoreAsync(
        string sourcePath,
        string password,
        Stream destination,
        IProgress<CryptoProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("The encrypted file could not be found.", sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        var passwordBytes = Encoding.UTF8.GetBytes(password);
        byte[]? key = null;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            await using var input = new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                DefaultChunkSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var reader = new BinaryReader(input, Encoding.UTF8, leaveOpen: true);

            Header header;
            try
            {
                header = ReadHeader(reader);
            }
            catch (EndOfStreamException ex)
            {
                throw new InvalidDataException("The encrypted file header is truncated.", ex);
            }

            await _pauseGate.WaitIfPausedAsync(cancellationToken);
            key = DeriveKey(passwordBytes, header.Salt, header.OpsLimit, header.MemLimit);
            await _pauseGate.WaitIfPausedAsync(cancellationToken);

            FileMetadata metadata;
            try
            {
                var metadataPlain = SecretAeadXChaCha20Poly1305.Decrypt(
                    header.MetadataCipher,
                    header.MetadataNonce,
                    key,
                    header.HeaderAuthenticationData);
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
                throw new CryptographicException(
                    "Rice2k could not authenticate this file. The password may be wrong, or the encrypted file may have been modified.",
                    ex);
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
                    throw new CryptographicException(
                        "Encrypted data authentication failed. The file may be damaged or modified.",
                        ex);
                }

                try
                {
                    await destination.WriteAsync(plain, cancellationToken);
                    written += plain.Length;
                    expectedIndex++;
                    Report(progress, written, metadata.OriginalLength, "Decrypting", stopwatch);
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
            if (key is not null)
                CryptographicOperations.ZeroMemory(key);
        }
    }

    private static void WriteHeader(
        BinaryWriter writer,
        byte[] salt,
        byte[] metadataNonce,
        byte[] metadataCipher)
    {
        if (metadataCipher.Length < AuthenticationTagSize || metadataCipher.Length > MaximumMetadataCipherLength)
            throw new InvalidDataException("The encrypted metadata length is outside the supported R2KENC01 format range.");

        writer.Write(Magic);
        writer.Write(Version);
        writer.Write(AlgorithmXChaCha20Poly1305);
        writer.Write(KdfArgon2Id);
        writer.Write(DefaultOpsLimit);
        writer.Write(DefaultMemLimit);
        writer.Write(DefaultChunkSize);
        writer.Write(salt);
        writer.Write(metadataNonce);
        writer.Write(metadataCipher.Length);
        writer.Write(metadataCipher);
    }

    private static Header ReadHeader(BinaryReader reader)
    {
        var magic = reader.ReadBytes(Magic.Length);
        if (!magic.SequenceEqual(Magic))
            throw new InvalidDataException("This is not a supported Rice2k encrypted file.");

        var version = reader.ReadByte();
        var algorithm = reader.ReadByte();
        var kdf = reader.ReadByte();

        if (version != Version)
            throw new NotSupportedException($"Rice2k encrypted-file version {version} is not supported by this build.");
        if (algorithm != AlgorithmXChaCha20Poly1305)
            throw new NotSupportedException("This encrypted file uses an unsupported encryption algorithm.");
        if (kdf != KdfArgon2Id)
            throw new NotSupportedException("This encrypted file uses an unsupported password derivation method.");

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
        var metadataNonce = reader.ReadBytes(NonceSize);
        if (salt.Length != SaltSize || metadataNonce.Length != NonceSize)
            throw new InvalidDataException("The encrypted file header is truncated.");

        var metadataCipherLength = reader.ReadInt32();
        if (metadataCipherLength < AuthenticationTagSize || metadataCipherLength > MaximumMetadataCipherLength)
            throw new InvalidDataException("The encrypted metadata length is invalid.");

        var metadataCipher = reader.ReadBytes(metadataCipherLength);
        if (metadataCipher.Length != metadataCipherLength)
            throw new InvalidDataException("The encrypted file metadata is truncated.");

        var headerAuth = BuildHeaderAuthenticationData(opsLimit, memLimit, chunkSize, salt);
        return new Header(
            opsLimit,
            memLimit,
            chunkSize,
            salt,
            metadataNonce,
            metadataCipher,
            headerAuth);
    }

    private static void ValidateMetadata(FileMetadata metadata, Header header)
    {
        if (!IsValidOriginalName(metadata.OriginalName))
            throw new InvalidDataException("The encrypted file metadata contains an invalid original filename.");
        if (metadata.OriginalLength < 0 || metadata.ChunkCount < 0)
            throw new InvalidDataException("The encrypted file metadata contains invalid lengths.");
        if (metadata.ChunkSize != header.ChunkSize)
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

    private static byte[] DeriveKey(byte[] passwordBytes, byte[] salt, long opsLimit, int memLimit)
    {
        return PasswordHash.ArgonHashBinary(
            passwordBytes,
            salt,
            opsLimit,
            memLimit,
            32,
            PasswordHash.ArgonAlgorithm.Argon_2ID13);
    }

    private static byte[] BuildHeaderAuthenticationData(
        long opsLimit,
        int memLimit,
        int chunkSize,
        byte[] salt)
    {
        var data = new byte[Magic.Length + 3 + sizeof(long) + sizeof(int) + sizeof(int) + SaltSize];
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
        return data;
    }

    private static byte[] BuildChunkAad(byte[] headerAuth, long index)
    {
        var headerHash = SHA256.HashData(headerAuth);
        var aad = new byte[headerHash.Length + sizeof(long)];
        headerHash.CopyTo(aad, 0);
        BinaryPrimitives.WriteInt64LittleEndian(aad.AsSpan(headerHash.Length, sizeof(long)), index);
        CryptographicOperations.ZeroMemory(headerHash);
        return aad;
    }

    private static void ValidateSourceAndDestination(
        string sourcePath,
        string destinationPath,
        string password,
        bool requireStrongPassword)
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

    private static void Report(
        IProgress<CryptoProgress>? progress,
        long processed,
        long total,
        string stage,
        Stopwatch stopwatch)
    {
        if (progress is null)
            return;

        var seconds = Math.Max(stopwatch.Elapsed.TotalSeconds, 0.001);
        progress.Report(new CryptoProgress(
            processed,
            total,
            stage,
            stopwatch.Elapsed,
            processed / seconds));
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
