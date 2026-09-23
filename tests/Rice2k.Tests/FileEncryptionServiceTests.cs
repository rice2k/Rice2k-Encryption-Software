using System.Buffers.Binary;
using System.Security.Cryptography;
using Rice2k.Encryption.Services;

namespace Rice2k.Tests;

public sealed class FileEncryptionServiceTests
{
    private const string Password = "correct horse battery staple 2026";
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
    public async Task MultiChunkFile_RoundTrip_RestoresAllBytes()
    {
        using var temp = new TempDirectory();
        var source = temp.PathFor("multi.bin");
        var encrypted = temp.PathFor("multi.bin.r2kenc");
        var restored = temp.PathFor("multi.restored.bin");
        var original = RandomNumberGenerator.GetBytes((4 * 1024 * 1024) + 4097);
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
