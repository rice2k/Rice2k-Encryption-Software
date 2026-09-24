using System.Security.Cryptography;
using Rice2k.Encryption.Models;
using Rice2k.Encryption.Services;

namespace Rice2k.Tests;

public sealed class FileEncryptionRaceSafetyTests
{
    private const string Password = "correct horse battery staple 2026";
    private const int DefaultChunkSize = 4 * 1024 * 1024;

    [Fact]
    public async Task Encrypt_DestinationAppearsDuringOperation_IsNeverOverwritten()
    {
        using var temp = new TempDirectory();
        var service = new FileEncryptionService();
        var source = temp.PathFor("source.bin");
        var destination = temp.PathFor("source.bin.r2kenc");
        var original = RandomNumberGenerator.GetBytes(DefaultChunkSize + 4096);
        await File.WriteAllBytesAsync(source, original);
        var sourceHash = SHA256.HashData(original);
        var competitor = "another process created this destination";
        var destinationCreated = false;
        var progress = new CallbackProgress<CryptoProgress>(value =>
        {
            if (!destinationCreated && value.Stage == "Encrypting" && value.BytesProcessed > 0)
            {
                File.WriteAllText(destination, competitor);
                destinationCreated = true;
            }
        });

        var error = await Assert.ThrowsAsync<IOException>(() =>
            service.EncryptFileAsync(
                source,
                destination,
                Password,
                progress,
                verifyAfterEncrypt: false));

        Assert.True(destinationCreated);
        Assert.Contains("already exists", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(competitor, await File.ReadAllTextAsync(destination));
        Assert.Equal(sourceHash, SHA256.HashData(await File.ReadAllBytesAsync(source)));
        Assert.Empty(Directory.GetFiles(temp.DirectoryPath, "*.partial"));
    }

    [Fact]
    public async Task Decrypt_ExistingDestination_IsNeverOverwritten()
    {
        using var temp = new TempDirectory();
        var service = new FileEncryptionService();
        var source = temp.PathFor("source.txt");
        var encrypted = temp.PathFor("source.txt.r2kenc");
        var restored = temp.PathFor("restored.txt");
        await File.WriteAllTextAsync(source, "encrypted source data");
        await service.EncryptFileAsync(source, encrypted, Password, verifyAfterEncrypt: false);
        await File.WriteAllTextAsync(restored, "keep this restore destination");
        var before = await File.ReadAllBytesAsync(restored);

        await Assert.ThrowsAsync<IOException>(() =>
            service.DecryptFileAsync(encrypted, restored, Password));

        Assert.Equal(before, await File.ReadAllBytesAsync(restored));
        Assert.Empty(Directory.GetFiles(temp.DirectoryPath, "*.partial"));
    }

    [Fact]
    public async Task Decrypt_DestinationAppearsDuringOperation_IsNeverOverwritten()
    {
        using var temp = new TempDirectory();
        var service = new FileEncryptionService();
        var source = temp.PathFor("large-source.bin");
        var encrypted = temp.PathFor("large-source.bin.r2kenc");
        var restored = temp.PathFor("late-restored.bin");
        await File.WriteAllBytesAsync(source, RandomNumberGenerator.GetBytes(DefaultChunkSize + 4096));
        await service.EncryptFileAsync(source, encrypted, Password, verifyAfterEncrypt: false);
        var encryptedHash = SHA256.HashData(await File.ReadAllBytesAsync(encrypted));
        var competitor = "late competing restore destination";
        var destinationCreated = false;
        var progress = new CallbackProgress<CryptoProgress>(value =>
        {
            if (!destinationCreated && value.Stage == "Decrypting" && value.BytesProcessed > 0)
            {
                File.WriteAllText(restored, competitor);
                destinationCreated = true;
            }
        });

        var error = await Assert.ThrowsAsync<IOException>(() =>
            service.DecryptFileAsync(encrypted, restored, Password, progress));

        Assert.True(destinationCreated);
        Assert.Contains("already exists", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(competitor, await File.ReadAllTextAsync(restored));
        Assert.Equal(encryptedHash, SHA256.HashData(await File.ReadAllBytesAsync(encrypted)));
        Assert.Empty(Directory.GetFiles(temp.DirectoryPath, "*.partial"));
    }

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
