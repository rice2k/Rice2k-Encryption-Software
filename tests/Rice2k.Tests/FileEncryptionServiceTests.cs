using System.Buffers.Binary;
using System.Security.Cryptography;
using Rice2k.Encryption.Models;
using Rice2k.Encryption.Services;

namespace Rice2k.Tests;

public sealed class FileEncryptionServiceTests
{
    private const string Password = "correct horse battery staple 2026";
    private const int DefaultChunkSize = 4 * 1024 * 1024;
    private const int NonceSize = 24;
    private const int OperationsLimitOffset = 8 + 1 + 1 + 1;
    private const int MemLimitOffset = OperationsLimitOffset + sizeof(long);
    private const int ChunkSizeOffset = MemLimitOffset + sizeof(int);
    private const int MetadataCipherLengthOffset = ChunkSizeOffset + sizeof(int) + 16 + 24;
    private readonly FileEncryptionService _service = new();

    [Fact]
    public async Task EmptyFile_RoundTrip_RestoresZeroLengthFile()
    {
        using var temp = new TempDirectory();
        var source = temp.PathFor("empty.bin");
        var encrypted = temp.PathFor("empty.bin.r2kenc");
        var restored = temp.PathFor("empty.restored.bin");
        await File.WriteAllBytesAsync(source, []);

        await _service.EncryptFileAsync(source, encrypted, Password);
        await _service.DecryptFileAsync(encrypted, restored, Password);

        Assert.True(File.Exists(encrypted));
        Assert.True(File.Exists(restored));
        Assert.Equal(0, new FileInfo(restored).Length);
        Assert.Equal(0, new FileInfo(source).Length);
    }

    [Fact]
    public async Task TinyFile_RoundTrip_RestoresSingleByte()
    {
        using var temp = new TempDirectory();
        var source = temp.PathFor("tiny.bin");
        var encrypted = temp.PathFor("tiny.bin.r2kenc");
        var restored = temp.PathFor("tiny.restored.bin");
        await File.WriteAllBytesAsync(source, [0x52]);

        await _service.EncryptFileAsync(source, encrypted, Password, verifyAfterEncrypt: false);
        await _service.DecryptFileAsync(encrypted, restored, Password);

        Assert.Equal(new byte[] { 0x52 }, await File.ReadAllBytesAsync(restored));
    }

    [Fact]
    public async Task File_RoundTrip_PreservesContentAndOriginalSource()
    {
        using var temp = new TempDirectory();
        var source = temp.PathFor("source.dat");
        var encrypted = temp.PathFor("source.dat.r2kenc");
        var restored = temp.PathFor("restored.dat");
        var original = RandomNumberGenerator.GetBytes(256 * 1024 + 37);
        await File.WriteAllBytesAsync(source, original);
        var sourceHashBefore = SHA256.HashData(original);

        await _service.EncryptFileAsync(source, encrypted, Password);
        await _service.DecryptFileAsync(encrypted, restored, Password);

        var restoredBytes = await File.ReadAllBytesAsync(restored);
        var sourceBytesAfter = await File.ReadAllBytesAsync(source);

        Assert.Equal(original, restoredBytes);
        Assert.Equal(sourceHashBefore, SHA256.HashData(sourceBytesAfter));
        Assert.NotEqual(original, await File.ReadAllBytesAsync(encrypted));
    }

    [Fact]
    public async Task ExactChunkFile_RoundTrip_RestoresAllBytes()
    {
        using var temp = new TempDirectory();
        var source = temp.PathFor("exact-chunk.bin");
        var encrypted = temp.PathFor("exact-chunk.bin.r2kenc");
        var restored = temp.PathFor("exact-chunk.restored.bin");
        var original = RandomNumberGenerator.GetBytes(DefaultChunkSize);
        await File.WriteAllBytesAsync(source, original);

        await _service.EncryptFileAsync(source, encrypted, Password, verifyAfterEncrypt: false);
        await _service.DecryptFileAsync(encrypted, restored, Password);

        Assert.Equal(original, await File.ReadAllBytesAsync(restored));
    }

    [Fact]
    public async Task MultiChunkFile_RoundTrip_RestoresAllBytes()
    {
        using var temp = new TempDirectory();
        var source = temp.PathFor("multi.bin");
        var encrypted = temp.PathFor("multi.bin.r2kenc");
        var restored = temp.PathFor("multi.restored.bin");
        var original = RandomNumberGenerator.GetBytes(DefaultChunkSize + 4097);
        await File.WriteAllBytesAsync(source, original);

        await _service.EncryptFileAsync(source, encrypted, Password, verifyAfterEncrypt: false);
        await _service.DecryptFileAsync(encrypted, restored, Password);

        Assert.Equal(original, await File.ReadAllBytesAsync(restored));
    }

    [Fact]
    public async Task Decrypt_WrongPassword_FailsClosedAndCreatesNoFinalOutput()
    {
        using var temp = new TempDirectory();
        var source = temp.PathFor("secret.txt");
        var encrypted = temp.PathFor("secret.txt.r2kenc");
        var restored = temp.PathFor("should-not-exist.txt");
        await File.WriteAllTextAsync(source, "sensitive contents");
        await _service.EncryptFileAsync(source, encrypted, Password, verifyAfterEncrypt: false);

        await Assert.ThrowsAsync<CryptographicException>(() =>
            _service.DecryptFileAsync(encrypted, restored, "wrong password but long enough"));

        Assert.False(File.Exists(restored));
        Assert.Empty(Directory.GetFiles(temp.DirectoryPath, "*.partial"));
    }

    [Fact]
    public async Task Decrypt_ModifiedSupportedHeader_FailsMetadataAuthentication()
    {
        using var temp = new TempDirectory();
        var source = temp.PathFor("header.bin");
        var encrypted = temp.PathFor("header.bin.r2kenc");
        var restored = temp.PathFor("header.restored.bin");
        await File.WriteAllTextAsync(source, "authenticated header test");
        await _service.EncryptFileAsync(source, encrypted, Password, verifyAfterEncrypt: false);

        var bytes = await File.ReadAllBytesAsync(encrypted);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(ChunkSizeOffset, sizeof(int)), 64 * 1024);
        await File.WriteAllBytesAsync(encrypted, bytes);

        await Assert.ThrowsAsync<CryptographicException>(() =>
            _service.DecryptFileAsync(encrypted, restored, Password));

        Assert.False(File.Exists(restored));
        Assert.Empty(Directory.GetFiles(temp.DirectoryPath, "*.partial"));
    }

    [Fact]
    public async Task Decrypt_ModifiedMetadataCiphertext_FailsAuthentication()
    {
        using var temp = new TempDirectory();
        var source = temp.PathFor("metadata-tamper.bin");
        var encrypted = temp.PathFor("metadata-tamper.bin.r2kenc");
        var restored = temp.PathFor("metadata-tamper.restored.bin");
        await File.WriteAllTextAsync(source, "authenticated metadata test");
        await _service.EncryptFileAsync(source, encrypted, Password, verifyAfterEncrypt: false);

        var bytes = await File.ReadAllBytesAsync(encrypted);
        var metadataLength = BinaryPrimitives.ReadInt32LittleEndian(
            bytes.AsSpan(MetadataCipherLengthOffset, sizeof(int)));
        Assert.True(metadataLength > 0);
        bytes[MetadataCipherLengthOffset + sizeof(int)] ^= 0x20;
        await File.WriteAllBytesAsync(encrypted, bytes);

        await Assert.ThrowsAsync<CryptographicException>(() =>
            _service.DecryptFileAsync(encrypted, restored, Password));

        Assert.False(File.Exists(restored));
        Assert.Empty(Directory.GetFiles(temp.DirectoryPath, "*.partial"));
    }

    [Fact]
    public async Task Decrypt_ModifiedCiphertext_FailsAuthentication()
    {
        using var temp = new TempDirectory();
        var source = temp.PathFor("tamper.bin");
        var encrypted = temp.PathFor("tamper.bin.r2kenc");
        var restored = temp.PathFor("tamper.restored.bin");
        await File.WriteAllBytesAsync(source, RandomNumberGenerator.GetBytes(8192));
        await _service.EncryptFileAsync(source, encrypted, Password, verifyAfterEncrypt: false);

        var bytes = await File.ReadAllBytesAsync(encrypted);
        bytes[^1] ^= 0x40;
        await File.WriteAllBytesAsync(encrypted, bytes);

        await Assert.ThrowsAsync<CryptographicException>(() =>
            _service.DecryptFileAsync(encrypted, restored, Password));

        Assert.False(File.Exists(restored));
    }

    [Fact]
    public async Task Decrypt_RemovedFinalChunk_FailsAsIncompleteContainer()
    {
        using var temp = new TempDirectory();
        var source = temp.PathFor("removed-chunk.bin");
        var encrypted = temp.PathFor("removed-chunk.bin.r2kenc");
        var restored = temp.PathFor("removed-chunk.restored.bin");
        await File.WriteAllBytesAsync(source, RandomNumberGenerator.GetBytes(DefaultChunkSize + 4096));
        await _service.EncryptFileAsync(source, encrypted, Password, verifyAfterEncrypt: false);

        var bytes = await File.ReadAllBytesAsync(encrypted);
        var chunks = ReadChunkRecords(bytes);
        Assert.True(chunks.Count >= 2);
        Array.Resize(ref bytes, chunks[^1].Offset);
        await File.WriteAllBytesAsync(encrypted, bytes);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            _service.DecryptFileAsync(encrypted, restored, Password));

        Assert.Contains("incomplete", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(restored));
        Assert.Empty(Directory.GetFiles(temp.DirectoryPath, "*.partial"));
    }

    [Fact]
    public async Task Decrypt_DuplicateChunkIndex_IsRejected()
    {
        using var temp = new TempDirectory();
        var source = temp.PathFor("duplicate-chunk.bin");
        var encrypted = temp.PathFor("duplicate-chunk.bin.r2kenc");
        var restored = temp.PathFor("duplicate-chunk.restored.bin");
        await File.WriteAllBytesAsync(source, RandomNumberGenerator.GetBytes(DefaultChunkSize + 4096));
        await _service.EncryptFileAsync(source, encrypted, Password, verifyAfterEncrypt: false);

        var bytes = await File.ReadAllBytesAsync(encrypted);
        var chunks = ReadChunkRecords(bytes);
        Assert.True(chunks.Count >= 2);
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(chunks[1].Offset, sizeof(long)), 0);
        await File.WriteAllBytesAsync(encrypted, bytes);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            _service.DecryptFileAsync(encrypted, restored, Password));

        Assert.Contains("out of order", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(restored));
    }

    [Fact]
    public async Task Decrypt_ReorderedFirstChunkIndex_IsRejected()
    {
        using var temp = new TempDirectory();
        var source = temp.PathFor("reordered-chunk.bin");
        var encrypted = temp.PathFor("reordered-chunk.bin.r2kenc");
        var restored = temp.PathFor("reordered-chunk.restored.bin");
        await File.WriteAllBytesAsync(source, RandomNumberGenerator.GetBytes(DefaultChunkSize + 4096));
        await _service.EncryptFileAsync(source, encrypted, Password, verifyAfterEncrypt: false);

        var bytes = await File.ReadAllBytesAsync(encrypted);
        var chunks = ReadChunkRecords(bytes);
        Assert.True(chunks.Count >= 2);
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(chunks[0].Offset, sizeof(long)), 1);
        await File.WriteAllBytesAsync(encrypted, bytes);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            _service.DecryptFileAsync(encrypted, restored, Password));

        Assert.Contains("out of order", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(restored));
    }

    [Fact]
    public async Task Decrypt_UnexpectedTrailingData_IsRejected()
    {
        using var temp = new TempDirectory();
        var source = temp.PathFor("trailing.bin");
        var encrypted = temp.PathFor("trailing.bin.r2kenc");
        var restored = temp.PathFor("trailing.restored.bin");
        await File.WriteAllTextAsync(source, "trailing data test");
        await _service.EncryptFileAsync(source, encrypted, Password, verifyAfterEncrypt: false);

        await using (var stream = new FileStream(encrypted, FileMode.Append, FileAccess.Write, FileShare.None))
            await stream.WriteAsync(new byte[] { 0x52, 0x32, 0x4B });

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            _service.DecryptFileAsync(encrypted, restored, Password));

        Assert.False(File.Exists(restored));
        Assert.Empty(Directory.GetFiles(temp.DirectoryPath, "*.partial"));
    }

    [Fact]
    public async Task Decrypt_TruncatedContainer_IsRejected()
    {
        using var temp = new TempDirectory();
        var source = temp.PathFor("truncate.bin");
        var encrypted = temp.PathFor("truncate.bin.r2kenc");
        var restored = temp.PathFor("truncate.restored.bin");
        await File.WriteAllBytesAsync(source, RandomNumberGenerator.GetBytes(4096));
        await _service.EncryptFileAsync(source, encrypted, Password, verifyAfterEncrypt: false);

        await using (var stream = new FileStream(encrypted, FileMode.Open, FileAccess.Write, FileShare.None))
            stream.SetLength(Math.Max(0, stream.Length - 7));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            _service.DecryptFileAsync(encrypted, restored, Password));

        Assert.False(File.Exists(restored));
    }

    [Fact]
    public async Task Decrypt_UnsupportedVersion_IsRejected()
    {
        using var temp = new TempDirectory();
        var source = temp.PathFor("version.bin");
        var encrypted = temp.PathFor("version.bin.r2kenc");
        var restored = temp.PathFor("version.restored.bin");
        await File.WriteAllTextAsync(source, "version test");
        await _service.EncryptFileAsync(source, encrypted, Password, verifyAfterEncrypt: false);

        var bytes = await File.ReadAllBytesAsync(encrypted);
        bytes[8] = 99;
        await File.WriteAllBytesAsync(encrypted, bytes);

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            _service.DecryptFileAsync(encrypted, restored, Password));

        Assert.False(File.Exists(restored));
    }

    [Fact]
    public async Task Decrypt_ExcessiveKdfCostInHeader_IsRejectedBeforeKeyDerivation()
    {
        using var temp = new TempDirectory();
        var source = temp.PathFor("resource-limit.bin");
        var encrypted = temp.PathFor("resource-limit.bin.r2kenc");
        var restored = temp.PathFor("resource-limit.restored.bin");
        await File.WriteAllTextAsync(source, "resource limit test");
        await _service.EncryptFileAsync(source, encrypted, Password, verifyAfterEncrypt: false);

        var bytes = await File.ReadAllBytesAsync(encrypted);
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(OperationsLimitOffset, sizeof(long)), 999);
        await File.WriteAllBytesAsync(encrypted, bytes);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            _service.DecryptFileAsync(encrypted, restored, Password));

        Assert.Contains("operation limit", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(restored));
    }

    [Fact]
    public async Task Decrypt_ExcessiveMetadataLength_IsRejectedBeforeKeyDerivation()
    {
        using var temp = new TempDirectory();
        var source = temp.PathFor("metadata-limit.bin");
        var encrypted = temp.PathFor("metadata-limit.bin.r2kenc");
        var restored = temp.PathFor("metadata-limit.restored.bin");
        await File.WriteAllTextAsync(source, "metadata limit test");
        await _service.EncryptFileAsync(source, encrypted, Password, verifyAfterEncrypt: false);

        var bytes = await File.ReadAllBytesAsync(encrypted);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(MetadataCipherLengthOffset, sizeof(int)), int.MaxValue);
        await File.WriteAllBytesAsync(encrypted, bytes);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            _service.DecryptFileAsync(encrypted, restored, Password));

        Assert.Contains("metadata length", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(restored));
        Assert.Empty(Directory.GetFiles(temp.DirectoryPath, "*.partial"));
    }

    [Fact]
    public async Task Encrypt_ExistingDestination_IsNeverOverwritten()
    {
        using var temp = new TempDirectory();
        var source = temp.PathFor("source.txt");
        var destination = temp.PathFor("existing.r2kenc");
        await File.WriteAllTextAsync(source, "source data");
        await File.WriteAllTextAsync(destination, "do not replace me");
        var before = await File.ReadAllBytesAsync(destination);

        await Assert.ThrowsAsync<IOException>(() =>
            _service.EncryptFileAsync(source, destination, Password));

        Assert.Equal(before, await File.ReadAllBytesAsync(destination));
    }

    [Fact]
    public async Task Encrypt_SourceEqualsDestination_IsRejectedWithoutChangingSource()
    {
        using var temp = new TempDirectory();
        var source = temp.PathFor("same.txt");
        await File.WriteAllTextAsync(source, "keep this source unchanged");
        var before = await File.ReadAllBytesAsync(source);

        await Assert.ThrowsAsync<IOException>(() =>
            _service.EncryptFileAsync(source, source, Password));

        Assert.Equal(before, await File.ReadAllBytesAsync(source));
        Assert.Empty(Directory.GetFiles(temp.DirectoryPath, "*.partial"));
    }

    [Fact]
    public async Task Encrypt_ShortPassword_IsRejectedAtServiceBoundary()
    {
        using var temp = new TempDirectory();
        var source = temp.PathFor("source.txt");
        var destination = temp.PathFor("source.txt.r2kenc");
        await File.WriteAllTextAsync(source, "data");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.EncryptFileAsync(source, destination, "short"));

        Assert.False(File.Exists(destination));
    }

    [Fact]
    public async Task Encrypt_PreCancelledOperation_LeavesNoFinalOrPartialOutput()
    {
        using var temp = new TempDirectory();
        var source = temp.PathFor("cancel.bin");
        var destination = temp.PathFor("cancel.bin.r2kenc");
        await File.WriteAllBytesAsync(source, RandomNumberGenerator.GetBytes(1024 * 1024));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _service.EncryptFileAsync(source, destination, Password, cancellationToken: cts.Token));

        Assert.True(File.Exists(source));
        Assert.False(File.Exists(destination));
        Assert.Empty(Directory.GetFiles(temp.DirectoryPath, "*.partial"));
    }

    [Fact]
    public async Task Encrypt_CancelledAfterFirstChunk_LeavesSourceAndNoOutput()
    {
        using var temp = new TempDirectory();
        var source = temp.PathFor("cancel-during-encrypt.bin");
        var destination = temp.PathFor("cancel-during-encrypt.bin.r2kenc");
        var original = RandomNumberGenerator.GetBytes(DefaultChunkSize + 4096);
        await File.WriteAllBytesAsync(source, original);
        var sourceHash = SHA256.HashData(original);
        using var cts = new CancellationTokenSource();
        var progress = new CallbackProgress<CryptoProgress>(value =>
        {
            if (value.Stage == "Encrypting" && value.BytesProcessed > 0)
                cts.Cancel();
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _service.EncryptFileAsync(
                source,
                destination,
                Password,
                progress,
                cts.Token,
                verifyAfterEncrypt: false));

        Assert.Equal(sourceHash, SHA256.HashData(await File.ReadAllBytesAsync(source)));
        Assert.False(File.Exists(destination));
        Assert.Empty(Directory.GetFiles(temp.DirectoryPath, "*.partial"));
    }

    [Fact]
    public async Task Decrypt_CancelledAfterFirstChunk_LeavesEncryptedSourceAndNoOutput()
    {
        using var temp = new TempDirectory();
        var source = temp.PathFor("cancel-during-decrypt.bin");
        var encrypted = temp.PathFor("cancel-during-decrypt.bin.r2kenc");
        var restored = temp.PathFor("cancel-during-decrypt.restored.bin");
        await File.WriteAllBytesAsync(source, RandomNumberGenerator.GetBytes(DefaultChunkSize + 4096));
        await _service.EncryptFileAsync(source, encrypted, Password, verifyAfterEncrypt: false);
        var encryptedBefore = SHA256.HashData(await File.ReadAllBytesAsync(encrypted));
        using var cts = new CancellationTokenSource();
        var progress = new CallbackProgress<CryptoProgress>(value =>
        {
            if (value.Stage == "Decrypting" && value.BytesProcessed > 0)
                cts.Cancel();
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _service.DecryptFileAsync(
                encrypted,
                restored,
                Password,
                progress,
                cts.Token));

        Assert.Equal(encryptedBefore, SHA256.HashData(await File.ReadAllBytesAsync(encrypted)));
        Assert.False(File.Exists(restored));
        Assert.Empty(Directory.GetFiles(temp.DirectoryPath, "*.partial"));
    }

    private static List<ChunkRecord> ReadChunkRecords(byte[] container)
    {
        var metadataLength = BinaryPrimitives.ReadInt32LittleEndian(
            container.AsSpan(MetadataCipherLengthOffset, sizeof(int)));
        var offset = checked(MetadataCipherLengthOffset + sizeof(int) + metadataLength);
        var records = new List<ChunkRecord>();

        while (offset < container.Length)
        {
            if (container.Length - offset < sizeof(long) + sizeof(int) + NonceSize)
                throw new InvalidDataException("Test fixture contains a truncated chunk header.");

            var index = BinaryPrimitives.ReadInt64LittleEndian(container.AsSpan(offset, sizeof(long)));
            var cipherLength = BinaryPrimitives.ReadInt32LittleEndian(
                container.AsSpan(offset + sizeof(long), sizeof(int)));
            if (cipherLength < 16)
                throw new InvalidDataException("Test fixture contains an invalid chunk length.");

            var totalLength = checked(sizeof(long) + sizeof(int) + NonceSize + cipherLength);
            if (totalLength > container.Length - offset)
                throw new InvalidDataException("Test fixture contains a truncated encrypted chunk.");

            records.Add(new ChunkRecord(offset, index, cipherLength, totalLength));
            offset = checked(offset + totalLength);
        }

        return records;
    }

    private sealed record ChunkRecord(int Offset, long Index, int CipherLength, int TotalLength);

    private sealed class CallbackProgress<T> : IProgress<T>
    {
        private readonly Action<T> _callback;

        public CallbackProgress(Action<T> callback) => _callback = callback;

        public void Report(T value) => _callback(value);
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            DirectoryPath = Path.Combine(Path.GetTempPath(), "Rice2k.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DirectoryPath);
        }

        public string DirectoryPath { get; }
        public string PathFor(string fileName) => Path.Combine(DirectoryPath, fileName);

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(DirectoryPath))
                    Directory.Delete(DirectoryPath, recursive: true);
            }
            catch
            {
                // Test cleanup is best effort.
            }
        }
    }
}
