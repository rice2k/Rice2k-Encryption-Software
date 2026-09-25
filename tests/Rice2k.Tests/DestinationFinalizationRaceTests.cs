using System.Security.Cryptography;
using Rice2k.Encryption.Models;
using Rice2k.Encryption.Services;

namespace Rice2k.Tests;

public sealed class DestinationFinalizationRaceTests
{
    private const string Password = "correct horse battery staple 2026";
    private const string Sentinel = "destination created by another process";

    [Fact]
    public async Task R2kenc01_Encrypt_LateDestinationCollisionPreservesOtherFile()
    {
        using var temp = new TempDirectory();
        var source = temp.PathFor("v1-source.bin");
        var destination = temp.PathFor("v1-output.r2kenc");
        await File.WriteAllBytesAsync(source, RandomNumberGenerator.GetBytes(128 * 1024 + 17));
        var service = new FileEncryptionService();
        var progress = CreateLateCollisionProgress(destination, "Encrypting");

        await Assert.ThrowsAsync<IOException>(() =>
            service.EncryptFileAsync(source, destination, Password, progress, verifyAfterEncrypt: false));

        Assert.Equal(Sentinel, await File.ReadAllTextAsync(destination));
        Assert.True(File.Exists(source));
        Assert.Empty(Directory.GetFiles(temp.DirectoryPath, "*.partial"));
    }

    [Fact]
    public async Task R2kenc01_Decrypt_LateDestinationCollisionPreservesOtherFile()
    {
        using var temp = new TempDirectory();
        var source = temp.PathFor("v1-source.bin");
        var encrypted = temp.PathFor("v1-source.bin.r2kenc");
        var destination = temp.PathFor("v1-restored.bin");
        await File.WriteAllBytesAsync(source, RandomNumberGenerator.GetBytes(128 * 1024 + 19));
        var service = new FileEncryptionService();
        await service.EncryptFileAsync(source, encrypted, Password, verifyAfterEncrypt: false);
        var progress = CreateCompletionCollisionProgress(destination);

        await Assert.ThrowsAsync<IOException>(() =>
            service.DecryptFileAsync(encrypted, destination, Password, progress));

        Assert.Equal(Sentinel, await File.ReadAllTextAsync(destination));
        Assert.True(File.Exists(encrypted));
        Assert.Empty(Directory.GetFiles(temp.DirectoryPath, "*.partial"));
    }

    [Fact]
    public async Task R2kenc02_Encrypt_LateDestinationCollisionPreservesOtherFile()
    {
        using var temp = new TempDirectory();
        var source = temp.PathFor("v2-source.bin");
        var destination = temp.PathFor("v2-output.r2kenc");
        await File.WriteAllBytesAsync(source, RandomNumberGenerator.GetBytes(128 * 1024 + 23));
        var keys = new KeyManagerService();
        using var key = keys.Generate("Race key");
        var service = new KeyFileEncryptionService();
        var progress = CreateLateCollisionProgress(destination, "Encrypting with password + key file");

        await Assert.ThrowsAsync<IOException>(() =>
            service.EncryptFileAsync(source, destination, Password, key, progress, verifyAfterEncrypt: false));

        Assert.Equal(Sentinel, await File.ReadAllTextAsync(destination));
        Assert.True(File.Exists(source));
        Assert.Empty(Directory.GetFiles(temp.DirectoryPath, "*.partial"));
    }

    [Fact]
    public async Task R2kenc02_Decrypt_LateDestinationCollisionPreservesOtherFile()
    {
        using var temp = new TempDirectory();
        var source = temp.PathFor("v2-source.bin");
        var encrypted = temp.PathFor("v2-source.bin.r2kenc");
        var destination = temp.PathFor("v2-restored.bin");
        await File.WriteAllBytesAsync(source, RandomNumberGenerator.GetBytes(128 * 1024 + 29));
        var keys = new KeyManagerService();
        using var key = keys.Generate("Race key");
        var service = new KeyFileEncryptionService();
        await service.EncryptFileAsync(source, encrypted, Password, key, verifyAfterEncrypt: false);
        var progress = CreateCompletionCollisionProgress(destination);

        await Assert.ThrowsAsync<IOException>(() =>
            service.DecryptFileAsync(encrypted, destination, Password, key, progress));

        Assert.Equal(Sentinel, await File.ReadAllTextAsync(destination));
        Assert.True(File.Exists(encrypted));
        Assert.Empty(Directory.GetFiles(temp.DirectoryPath, "*.partial"));
    }

    [Fact]
    public async Task R2kenc03_Encrypt_LateDestinationCollisionPreservesOtherFile()
    {
        using var temp = new TempDirectory();
        var source = temp.PathFor("v3-source.bin");
        var destination = temp.PathFor("v3-output.r2kenc");
        await File.WriteAllBytesAsync(source, RandomNumberGenerator.GetBytes(128 * 1024 + 31));
        var identities = new IdentityService();
        using var identity = identities.Generate("Race recipient");
        var service = new RecipientFileEncryptionService();
        var progress = CreateLateCollisionProgress(destination, "Encrypting for recipient");

        await Assert.ThrowsAsync<IOException>(() =>
            service.EncryptForRecipientAsync(
                source,
                destination,
                identity.ToPublicIdentity(),
                progress,
                verifyAfterEncrypt: false));

        Assert.Equal(Sentinel, await File.ReadAllTextAsync(destination));
        Assert.True(File.Exists(source));
        Assert.Empty(Directory.GetFiles(temp.DirectoryPath, "*.partial"));
    }

    [Fact]
    public async Task R2kenc03_Decrypt_LateDestinationCollisionPreservesOtherFile()
    {
        using var temp = new TempDirectory();
        var source = temp.PathFor("v3-source.bin");
        var encrypted = temp.PathFor("v3-source.bin.r2kenc");
        var destination = temp.PathFor("v3-restored.bin");
        await File.WriteAllBytesAsync(source, RandomNumberGenerator.GetBytes(128 * 1024 + 37));
        var identities = new IdentityService();
        using var identity = identities.Generate("Race recipient");
        var service = new RecipientFileEncryptionService();
        await service.EncryptForRecipientAsync(source, encrypted, identity.ToPublicIdentity(), verifyAfterEncrypt: false);
        var progress = CreateCompletionCollisionProgress(destination);

        await Assert.ThrowsAsync<IOException>(() =>
            service.DecryptAsync(encrypted, destination, identity, progress));

        Assert.Equal(Sentinel, await File.ReadAllTextAsync(destination));
        Assert.True(File.Exists(encrypted));
        Assert.Empty(Directory.GetFiles(temp.DirectoryPath, "*.partial"));
    }

    private static IProgress<CryptoProgress> CreateLateCollisionProgress(string destination, string stage) =>
        new ImmediateProgress<CryptoProgress>(value =>
        {
            if (value.Stage == stage &&
                value.TotalBytes > 0 &&
                value.BytesProcessed >= value.TotalBytes &&
                !File.Exists(destination))
            {
                File.WriteAllText(destination, Sentinel);
            }
        });

    private static IProgress<CryptoProgress> CreateCompletionCollisionProgress(string destination) =>
        new ImmediateProgress<CryptoProgress>(value =>
        {
            if (value.Stage == "Complete" && !File.Exists(destination))
                File.WriteAllText(destination, Sentinel);
        });

    private sealed class ImmediateProgress<T> : IProgress<T>
    {
        private readonly Action<T> _callback;

        public ImmediateProgress(Action<T> callback) => _callback = callback;

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
