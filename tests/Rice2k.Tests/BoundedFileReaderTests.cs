using Rice2k.Encryption.Services;

namespace Rice2k.Tests;

public sealed class BoundedFileReaderTests
{
    [Fact]
    public async Task ReadAllBytesAsync_WithinLimit_ReturnsExactContent()
    {
        using var temp = new TempDirectory();
        var path = temp.PathFor("small.bin");
        var expected = Enumerable.Range(0, 257).Select(value => (byte)(value % 251)).ToArray();
        await File.WriteAllBytesAsync(path, expected);

        var actual = await BoundedFileReader.ReadAllBytesAsync(path, 1024, "test file");

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task ReadAllBytesAsync_EmptyFile_IsRejected()
    {
        using var temp = new TempDirectory();
        var path = temp.PathFor("empty.bin");
        await File.WriteAllBytesAsync(path, []);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            BoundedFileReader.ReadAllBytesAsync(path, 1024, "test file"));

        Assert.Contains("invalid size", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadAllBytesAsync_OverLimit_IsRejectedBeforeAllocation()
    {
        using var temp = new TempDirectory();
        var path = temp.PathFor("large.bin");
        await File.WriteAllBytesAsync(path, new byte[1025]);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            BoundedFileReader.ReadAllBytesAsync(path, 1024, "test file"));

        Assert.Contains("invalid size", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadAllBytesAsync_PreCancelled_IsCancelled()
    {
        using var temp = new TempDirectory();
        var path = temp.PathFor("cancel.bin");
        await File.WriteAllBytesAsync(path, new byte[128]);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            BoundedFileReader.ReadAllBytesAsync(path, 1024, "test file", cts.Token));
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
