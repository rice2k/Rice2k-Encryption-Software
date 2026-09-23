using System.Buffers.Binary;
using Rice2k.Encryption.Services;

namespace Rice2k.Tests;

public sealed class SecureVaultParserSafetyTests
{
    private const string Password = "correct horse battery staple 2026";
    private const int OpsLimitOffset = 8 + 1 + 1 + 1;
    private const int MemLimitOffset = OpsLimitOffset + sizeof(long);
    private const int ChunkSizeOffset = MemLimitOffset + sizeof(int);
    private const int VaultIdOffset = ChunkSizeOffset + sizeof(int) + 16;
    private const int ManifestCipherLengthOffset = VaultIdOffset + 16 + 24;

    [Fact]
    public async Task Unlock_UnsupportedVersion_IsRejected()
    {
        using var temp = new TempDirectory();
        var service = new SecureVaultService();
        var vaultPath = temp.PathFor("version.r2kvault");
        using (await service.CreateAsync(vaultPath, Password))
        {
        }

        var bytes = await File.ReadAllBytesAsync(vaultPath);
        bytes[8] = 99;
        await File.WriteAllBytesAsync(vaultPath, bytes);

        await Assert.ThrowsAsync<NotSupportedException>(() => service.UnlockAsync(vaultPath, Password));
    }

    [Fact]
    public async Task Unlock_ExcessiveArgonOperations_IsRejectedBeforeKdf()
    {
        using var temp = new TempDirectory();
        var service = new SecureVaultService();
        var vaultPath = temp.PathFor("ops.r2kvault");
        using (await service.CreateAsync(vaultPath, Password))
        {
        }

        var bytes = await File.ReadAllBytesAsync(vaultPath);
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(OpsLimitOffset, sizeof(long)), 999);
        await File.WriteAllBytesAsync(vaultPath, bytes);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => service.UnlockAsync(vaultPath, Password));
        Assert.Contains("operation limit", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Unlock_ExcessiveArgonMemory_IsRejectedBeforeKdf()
    {
        using var temp = new TempDirectory();
        var service = new SecureVaultService();
        var vaultPath = temp.PathFor("memory.r2kvault");
        using (await service.CreateAsync(vaultPath, Password))
        {
        }

        var bytes = await File.ReadAllBytesAsync(vaultPath);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(MemLimitOffset, sizeof(int)), int.MaxValue);
        await File.WriteAllBytesAsync(vaultPath, bytes);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => service.UnlockAsync(vaultPath, Password));
        Assert.Contains("memory limit", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Unlock_ExcessiveChunkSize_IsRejectedBeforeAllocation()
    {
        using var temp = new TempDirectory();
        var service = new SecureVaultService();
        var vaultPath = temp.PathFor("chunk.r2kvault");
        using (await service.CreateAsync(vaultPath, Password))
        {
        }

        var bytes = await File.ReadAllBytesAsync(vaultPath);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(ChunkSizeOffset, sizeof(int)), int.MaxValue);
        await File.WriteAllBytesAsync(vaultPath, bytes);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => service.UnlockAsync(vaultPath, Password));
        Assert.Contains("chunk size", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Unlock_EmptyVaultIdentifier_IsRejectedBeforeKdf()
    {
        using var temp = new TempDirectory();
        var service = new SecureVaultService();
        var vaultPath = temp.PathFor("empty-id.r2kvault");
        using (await service.CreateAsync(vaultPath, Password))
        {
        }

        var bytes = await File.ReadAllBytesAsync(vaultPath);
        bytes.AsSpan(VaultIdOffset, 16).Clear();
        await File.WriteAllBytesAsync(vaultPath, bytes);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => service.UnlockAsync(vaultPath, Password));
        Assert.Contains("identifier", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Unlock_ExcessiveManifestCipherLength_IsRejectedBeforeRead()
    {
        using var temp = new TempDirectory();
        var service = new SecureVaultService();
        var vaultPath = temp.PathFor("manifest-length.r2kvault");
        using (await service.CreateAsync(vaultPath, Password))
        {
        }

        var bytes = await File.ReadAllBytesAsync(vaultPath);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(ManifestCipherLengthOffset, sizeof(int)), int.MaxValue);
        await File.WriteAllBytesAsync(vaultPath, bytes);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => service.UnlockAsync(vaultPath, Password));
        Assert.Contains("manifest length", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Unlock_TruncatedHeader_IsRejectedAsInvalidData()
    {
        using var temp = new TempDirectory();
        var service = new SecureVaultService();
        var vaultPath = temp.PathFor("truncated.r2kvault");
        using (await service.CreateAsync(vaultPath, Password))
        {
        }

        await using (var stream = new FileStream(vaultPath, FileMode.Open, FileAccess.Write, FileShare.None))
            stream.SetLength(20);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => service.UnlockAsync(vaultPath, Password));
        Assert.Contains("truncated", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Unlock_ExcessiveRecordPayloadLength_IsRejected()
    {
        using var temp = new TempDirectory();
        var service = new SecureVaultService();
        var vaultPath = temp.PathFor("record-length.r2kvault");
        var source = temp.PathFor("one.txt");
        await File.WriteAllTextAsync(source, "record parser test");

        using (var session = await service.CreateAsync(vaultPath, Password))
            await service.AddFileAsync(session, source, "one.txt");

        var bytes = await File.ReadAllBytesAsync(vaultPath);
        var manifestLength = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(ManifestCipherLengthOffset, sizeof(int)));
        var recordStart = checked(ManifestCipherLengthOffset + sizeof(int) + manifestLength);
        var recordPayloadLengthOffset = checked(recordStart + 16);
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(recordPayloadLengthOffset, sizeof(long)), long.MaxValue);
        await File.WriteAllBytesAsync(vaultPath, bytes);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => service.UnlockAsync(vaultPath, Password));
        Assert.Contains("payload length", error.Message, StringComparison.OrdinalIgnoreCase);
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
