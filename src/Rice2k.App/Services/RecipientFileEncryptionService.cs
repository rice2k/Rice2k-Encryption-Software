using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Rice2k.Encryption.Models;
using Sodium;

namespace Rice2k.Encryption.Services;

public sealed class RecipientFileEncryptionService
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("R2KENC03");
    private const byte Version = 3;
    private const byte AlgorithmXChaCha20Poly1305 = 1;
    private const byte KeyWrapSealedBox = 1;
    private const int DefaultChunkSize = 4 * 1024 * 1024;
    private const int NonceSize = 24;
    private const int ContentKeySize = 32;
    private const int AuthenticationTagSize = 16;
    private const int MaximumRecipients = 64;
    private const int MaximumWrappedKeyLength = 256;
    private const int MaximumSupportedChunkSize = 64 * 1024 * 1024;
    private const int MaximumMetadataCipherLength = 128 * 1024;

    private sealed record RecipientMetadata(Guid Id, string Fingerprint);

    private sealed record FileMetadata(
        string OriginalName,
        long OriginalLength,
        long ChunkCount,
        int ChunkSize,
        DateTimeOffset CreatedUtc,
        IReadOnlyList<RecipientMetadata> Recipients);

    private sealed record Header(
        int ChunkSize,
        IReadOnlyList<byte[]> WrappedContentKeys,
        byte[] MetadataNonce,
        byte[] MetadataCipher,
        byte[] HeaderAuthenticationData);

    public Task EncryptForRecipientAsync(
        string sourcePath,
        string destinationPath,
        Rice2kPublicIdentity recipient,
        IProgress<CryptoProgress>? progress = null,
        CancellationToken cancellationToken = default,
        bool verifyAfterEncrypt = true) =>
        EncryptForRecipientsAsync(
            sourcePath,
            destinationPath,
            [recipient],
            progress,
            cancellationToken,
            verifyAfterEncrypt);

    public async Task EncryptForRecipientsAsync(
        string sourcePath,
        string destinationPath,
        IEnumerable<Rice2kPublicIdentity> recipients,
        IProgress<CryptoProgress>? progress = null,
        CancellationToken cancellationToken = default,
        bool verifyAfterEncrypt = true)
    {
        ValidateSourceAndDestination(sourcePath, destinationPath);
        ArgumentNullException.ThrowIfNull(recipients);

        var recipientList = recipients.ToArray();
        ValidateRecipients(recipientList);

        var sourceInfo = new FileInfo(sourcePath);
        var tempPath = CreateUniqueTempPath(destinationPath);
        var contentKey = RandomNumberGenerator.GetBytes(ContentKeySize);
        var stopwatch = Stopwatch.StartNew();
        var wrappedKeys = new List<byte[]>(recipientList.Length);

        try
        {
            foreach (var recipient in recipientList)
            {
                cancellationToken.ThrowIfCancellationRequested();
                wrappedKeys.Add(SealedPublicKeyBox.Create(contentKey, recipient.EncryptionPublicKey));
            }

            var headerAuth = BuildHeaderAuthenticationData(DefaultChunkSize, wrappedKeys);
            var chunkCount = sourceInfo.Length == 0
                ? 0
                : (sourceInfo.Length + DefaultChunkSize - 1) / DefaultChunkSize;
            var metadata = new FileMetadata(
                sourceInfo.Name,
                sourceInfo.Length,
                chunkCount,
                DefaultChunkSize,
                DateTimeOffset.UtcNow,
                recipientList.Select(recipient => new RecipientMetadata(recipient.Id, recipient.Fingerprint)).ToArray());

            var metadataNonce = SecretAeadXChaCha20Poly1305.GenerateNonce();
            var metadataPlain = JsonSerializer.SerializeToUtf8Bytes(metadata);
            byte[]? metadataCipher = null;

            try
            {
                metadataCipher = SecretAeadXChaCha20Poly1305.Encrypt(
                    metadataPlain,
                    metadataNonce,
                    contentKey,
                    headerAuth);

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
                    WriteHeader(writer, wrappedKeys, metadataNonce, metadataCipher);
                    writer.Flush();

                    var buffer = new byte[DefaultChunkSize];
                    long processed = 0;
                    long index = 0;
                    try
                    {
                        while (true)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                            if (read == 0)
                                break;

                            var plain = buffer.AsSpan(0, read).ToArray();
                            byte[]? cipher = null;
                            try
                            {
                                var nonce = SecretAeadXChaCha20Poly1305.GenerateNonce();
                                cipher = SecretAeadXChaCha20Poly1305.Encrypt(
                                    plain,
                                    nonce,
                                    contentKey,
                                    BuildChunkAad(headerAuth, index));

                                writer.Write(index);
                                writer.Write(cipher.Length);
                                writer.Write(nonce);
                                writer.Write(cipher);
                                writer.Flush();

                                processed += read;
                                index++;
                                Report(progress, processed, sourceInfo.Length, "Encrypting for recipient", stopwatch);
                            }
                            finally
                            {
                                CryptographicOperations.ZeroMemory(plain);
                                if (cipher is not null)
                                    CryptographicOperations.ZeroMemory(cipher);
                            }
                        }
                    }
                    finally
                    {
                        CryptographicOperations.ZeroMemory(buffer);
                    }

                    await output.FlushAsync(cancellationToken);
                    output.Flush(flushToDisk: true);
                }

                if (verifyAfterEncrypt)
                {
                    Report(progress, sourceInfo.Length, sourceInfo.Length, "Verifying recipient-encrypted data", stopwatch);
                    await VerifyWithContentKeyAsync(tempPath, contentKey, cancellationToken);
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(metadataPlain);
                if (metadataCipher is not null)
                    CryptographicOperations.ZeroMemory(metadataCipher);
            }

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
            CryptographicOperations.ZeroMemory(contentKey);
            foreach (var wrapped in wrappedKeys)
                CryptographicOperations.ZeroMemory(wrapped);
        }
    }

    public async Task DecryptAsync(
        string sourcePath,
        string destinationPath,
        Rice2kIdentity recipientIdentity,
        IProgress<CryptoProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recipientIdentity);
        ValidateSourceAndDestination(sourcePath, destinationPath, requireSourceOnly: false);

        var tempPath = CreateUniqueTempPath(destinationPath);
        var encryptionPrivate = recipientIdentity.CopyEncryptionPrivateKey();
        byte[]? contentKey = null;

        try
        {
            var header = await ReadHeaderAsync(sourcePath, cancellationToken);
            foreach (var wrappedKey in header.WrappedContentKeys)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var candidate = SealedPublicKeyBox.Open(
                        wrappedKey,
                        encryptionPrivate,
                        recipientIdentity.EncryptionPublicKey);
                    if (candidate.Length == ContentKeySize)
                    {
                        contentKey = candidate;
                        break;
                    }
                    CryptographicOperations.ZeroMemory(candidate);
                }
                catch (CryptographicException)
                {
                    // This wrapped key belongs to another recipient. Try the next one.
                }
            }

            if (contentKey is null)
                throw new CryptographicException("This Rice2k recipient-encrypted file was not encrypted for the selected private identity.");

            await using (var output = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                DefaultChunkSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var metadata = await DecryptCoreWithContentKeyAsync(
                    sourcePath,
                    contentKey,
                    output,
                    progress,
                    cancellationToken);

                if (!metadata.Recipients.Any(recipient =>
                        recipient.Id == recipientIdentity.Id &&
                        string.Equals(recipient.Fingerprint, recipientIdentity.Fingerprint, StringComparison.Ordinal)))
                {
                    throw new CryptographicException("The authenticated recipient list does not contain the selected Rice2k identity.");
                }

                await output.FlushAsync(cancellationToken);
                output.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(destinationPath))
                throw new IOException("A file appeared at the selected restore destination before Rice2k could finalize it.");
            File.Move(tempPath, destinationPath);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encryptionPrivate);
            if (contentKey is not null)
                CryptographicOperations.ZeroMemory(contentKey);
        }
    }

    public async Task<RecipientContainerInfo> InspectAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        var header = await ReadHeaderAsync(sourcePath, cancellationToken);
        return new RecipientContainerInfo(
            header.WrappedContentKeys.Count,
            header.ChunkSize,
            new FileInfo(sourcePath).Length);
    }

    private async Task VerifyWithContentKeyAsync(
        string sourcePath,
        byte[] contentKey,
        CancellationToken cancellationToken)
    {
        _ = await DecryptCoreWithContentKeyAsync(
            sourcePath,
            contentKey,
            Stream.Null,
            null,
            cancellationToken);
    }

    private async Task<FileMetadata> DecryptCoreWithContentKeyAsync(
        string sourcePath,
        byte[] contentKey,
        Stream destination,
        IProgress<CryptoProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("The recipient-encrypted file could not be found.", sourcePath);
        if (contentKey.Length != ContentKeySize)
            throw new CryptographicException("The recipient-encrypted file could not be unlocked with a valid content key.");

        var stopwatch = Stopwatch.StartNew();
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
            throw new InvalidDataException("The recipient-encrypted file header is truncated.", ex);
        }

        FileMetadata metadata;
        try
        {
            var metadataPlain = SecretAeadXChaCha20Poly1305.Decrypt(
                header.MetadataCipher,
                header.MetadataNonce,
                contentKey,
                header.HeaderAuthenticationData);
            try
            {
                metadata = JsonSerializer.Deserialize<FileMetadata>(metadataPlain)
                    ?? throw new InvalidDataException("The recipient-encrypted metadata is missing.");
            }
            finally
            {
                CryptographicOperations.ZeroMemory(metadataPlain);
            }
        }
        catch (CryptographicException ex)
        {
            throw new CryptographicException("Recipient-encrypted metadata authentication failed. The container may be damaged or the content key is incorrect.", ex);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("The recipient-encrypted metadata is malformed.", ex);
        }

        ValidateMetadata(metadata, header);
        long expectedIndex = 0;
        long written = 0;

        while (input.Position < input.Length)
        {
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
                    throw new InvalidDataException("The recipient-encrypted chunk length is invalid.");
                nonce = reader.ReadBytes(NonceSize);
                if (nonce.Length != NonceSize)
                    throw new EndOfStreamException();
                cipher = reader.ReadBytes(cipherLength);
                if (cipher.Length != cipherLength)
                    throw new EndOfStreamException();
            }
            catch (EndOfStreamException ex)
            {
                throw new InvalidDataException("The recipient-encrypted file appears to be truncated.", ex);
            }

            if (index != expectedIndex)
                throw new InvalidDataException("Recipient-encrypted chunks are missing, duplicated, or out of order.");

            byte[] plain;
            try
            {
                plain = SecretAeadXChaCha20Poly1305.Decrypt(
                    cipher,
                    nonce,
                    contentKey,
                    BuildChunkAad(header.HeaderAuthenticationData, index));
            }
            catch (CryptographicException ex)
            {
                throw new CryptographicException("Recipient-encrypted file data authentication failed. The file may be damaged or modified.", ex);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(cipher);
            }

            try
            {
                await destination.WriteAsync(plain, cancellationToken);
                written += plain.Length;
                expectedIndex++;
                Report(progress, written, metadata.OriginalLength, "Decrypting recipient file", stopwatch);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plain);
            }
        }

        if (expectedIndex != metadata.ChunkCount || written != metadata.OriginalLength)
            throw new InvalidDataException("The recipient-encrypted file is incomplete or contains unexpected data.");

        Report(progress, written, metadata.OriginalLength, "Complete", stopwatch);
        return metadata;
    }

    private static void WriteHeader(
        BinaryWriter writer,
        IReadOnlyList<byte[]> wrappedKeys,
        byte[] metadataNonce,
        byte[] metadataCipher)
    {
        writer.Write(Magic);
        writer.Write(Version);
        writer.Write(AlgorithmXChaCha20Poly1305);
        writer.Write(KeyWrapSealedBox);
        writer.Write(DefaultChunkSize);
        writer.Write(wrappedKeys.Count);
        foreach (var wrappedKey in wrappedKeys)
        {
            writer.Write(wrappedKey.Length);
            writer.Write(wrappedKey);
        }
        writer.Write(metadataNonce);
        writer.Write(metadataCipher.Length);
        writer.Write(metadataCipher);
    }

    private static Header ReadHeader(BinaryReader reader)
    {
        var magic = reader.ReadBytes(Magic.Length);
        if (!magic.SequenceEqual(Magic))
            throw new InvalidDataException("This is not a Rice2k recipient-encrypted R2KENC03 file.");

        var version = reader.ReadByte();
        var algorithm = reader.ReadByte();
        var keyWrap = reader.ReadByte();
        if (version != Version)
            throw new NotSupportedException($"Recipient-encrypted format version {version} is not supported.");
        if (algorithm != AlgorithmXChaCha20Poly1305)
            throw new NotSupportedException("The recipient-encrypted file uses an unsupported content cipher.");
        if (keyWrap != KeyWrapSealedBox)
            throw new NotSupportedException("The recipient-encrypted file uses an unsupported key-wrapping method.");

        var chunkSize = reader.ReadInt32();
        if (chunkSize < 64 * 1024 || chunkSize > MaximumSupportedChunkSize)
            throw new InvalidDataException("The recipient-encrypted chunk size is outside the supported range.");

        var recipientCount = reader.ReadInt32();
        if (recipientCount < 1 || recipientCount > MaximumRecipients)
            throw new InvalidDataException("The recipient-encrypted file contains an invalid recipient count.");

        var wrappedKeys = new List<byte[]>(recipientCount);
        for (var index = 0; index < recipientCount; index++)
        {
            var length = reader.ReadInt32();
            if (length < ContentKeySize + 48 || length > MaximumWrappedKeyLength)
                throw new InvalidDataException("A wrapped recipient content key has an invalid length.");
            var wrapped = reader.ReadBytes(length);
            if (wrapped.Length != length)
                throw new InvalidDataException("The recipient-encrypted header is truncated while reading a wrapped key.");
            wrappedKeys.Add(wrapped);
        }

        var metadataNonce = reader.ReadBytes(NonceSize);
        if (metadataNonce.Length != NonceSize)
            throw new InvalidDataException("The recipient-encrypted header is truncated while reading metadata parameters.");

        var metadataCipherLength = reader.ReadInt32();
        if (metadataCipherLength < AuthenticationTagSize || metadataCipherLength > MaximumMetadataCipherLength)
            throw new InvalidDataException("The recipient-encrypted metadata length is invalid.");
        var metadataCipher = reader.ReadBytes(metadataCipherLength);
        if (metadataCipher.Length != metadataCipherLength)
            throw new InvalidDataException("The recipient-encrypted metadata is truncated.");

        return new Header(
            chunkSize,
            wrappedKeys,
            metadataNonce,
            metadataCipher,
            BuildHeaderAuthenticationData(chunkSize, wrappedKeys));
    }

    private static async Task<Header> ReadHeaderAsync(string sourcePath, CancellationToken cancellationToken)
    {
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("The recipient-encrypted file could not be found.", sourcePath);

        await using var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new BinaryReader(input, Encoding.UTF8, leaveOpen: true);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return ReadHeader(reader);
        }
        catch (EndOfStreamException ex)
        {
            throw new InvalidDataException("The recipient-encrypted file header is truncated.", ex);
        }
    }

    private static byte[] BuildHeaderAuthenticationData(int chunkSize, IReadOnlyList<byte[]> wrappedKeys)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(Magic);
        writer.Write(Version);
        writer.Write(AlgorithmXChaCha20Poly1305);
        writer.Write(KeyWrapSealedBox);
        writer.Write(chunkSize);
        writer.Write(wrappedKeys.Count);
        foreach (var wrapped in wrappedKeys)
        {
            writer.Write(wrapped.Length);
            writer.Write(wrapped);
        }
        writer.Flush();
        return stream.ToArray();
    }

    private static byte[] BuildChunkAad(byte[] headerAuth, long index)
    {
        var hash = SHA256.HashData(headerAuth);
        try
        {
            var aad = new byte[hash.Length + sizeof(long)];
            hash.CopyTo(aad, 0);
            BinaryPrimitives.WriteInt64LittleEndian(aad.AsSpan(hash.Length, sizeof(long)), index);
            return aad;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(hash);
        }
    }

    private static void ValidateMetadata(FileMetadata metadata, Header header)
    {
        if (string.IsNullOrWhiteSpace(metadata.OriginalName))
            throw new InvalidDataException("Recipient-encrypted metadata does not contain an original filename.");
        if (metadata.OriginalLength < 0 || metadata.ChunkCount < 0)
            throw new InvalidDataException("Recipient-encrypted metadata contains invalid file lengths.");
        if (metadata.ChunkSize != header.ChunkSize)
            throw new InvalidDataException("Recipient-encrypted metadata does not match the authenticated chunk size.");
        if (metadata.Recipients is null || metadata.Recipients.Count != header.WrappedContentKeys.Count)
            throw new InvalidDataException("Recipient-encrypted metadata does not match the authenticated recipient count.");
        if (metadata.Recipients.Count < 1 || metadata.Recipients.Count > MaximumRecipients)
            throw new InvalidDataException("Recipient-encrypted metadata contains an invalid recipient list.");
        if (metadata.Recipients.Any(recipient => recipient.Id == Guid.Empty || string.IsNullOrWhiteSpace(recipient.Fingerprint)))
            throw new InvalidDataException("Recipient-encrypted metadata contains an invalid recipient identity.");
        if (metadata.Recipients.Select(recipient => recipient.Fingerprint).Distinct(StringComparer.Ordinal).Count() != metadata.Recipients.Count)
            throw new InvalidDataException("Recipient-encrypted metadata contains duplicate recipient fingerprints.");

        var expectedChunks = metadata.OriginalLength == 0
            ? 0
            : (metadata.OriginalLength + metadata.ChunkSize - 1) / metadata.ChunkSize;
        if (metadata.ChunkCount != expectedChunks)
            throw new InvalidDataException("Recipient-encrypted metadata contains an inconsistent chunk count.");
    }

    private static void ValidateRecipients(IReadOnlyList<Rice2kPublicIdentity> recipients)
    {
        if (recipients.Count < 1 || recipients.Count > MaximumRecipients)
            throw new ArgumentException($"Choose between 1 and {MaximumRecipients} recipients.");
        if (recipients.Any(recipient => recipient is null))
            throw new ArgumentException("The recipient list contains an invalid identity.");
        if (recipients.Any(recipient => recipient.Id == Guid.Empty || recipient.EncryptionPublicKey.Length != 32 || recipient.SigningPublicKey.Length != 32))
            throw new ArgumentException("One or more recipient identities contain invalid public keys.");
        if (recipients.Select(recipient => recipient.Fingerprint).Distinct(StringComparer.Ordinal).Count() != recipients.Count)
            throw new ArgumentException("The same recipient identity was selected more than once.");
    }

    private static void ValidateSourceAndDestination(
        string sourcePath,
        string destinationPath,
        bool requireSourceOnly = true)
    {
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("The source file could not be found.", sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        if (string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(destinationPath), StringComparison.OrdinalIgnoreCase))
            throw new IOException("Source and destination must be different files.");
        if (File.Exists(destinationPath))
            throw new IOException("The destination file already exists. Rice2k will not overwrite it automatically.");

        var directory = Path.GetDirectoryName(Path.GetFullPath(destinationPath));
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            throw new DirectoryNotFoundException("The selected destination folder does not exist.");
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
            // Best-effort cleanup. Source data is never removed here.
        }
    }
}
