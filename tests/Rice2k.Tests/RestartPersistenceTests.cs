using System.Security.Cryptography;
using Rice2k.Encryption.Services;

namespace Rice2k.Tests;

public sealed class RestartPersistenceTests
{
    private const string FilePassword = "correct horse battery staple 2026";
    private const string PackagePassword = "separate persisted package password 2026";

    [Fact]
    public async Task R2kenc01_FreshServiceDecryptsPersistedContainer()
    {
        using var temp = new TempDirectory();
        var source = temp.PathFor("v1-source.bin");
        var encrypted = temp.PathFor("v1-source.bin.r2kenc");
        var restored = temp.PathFor("v1-restored.bin");
        var original = RandomNumberGenerator.GetBytes((512 * 1024) + 11);
        await File.WriteAllBytesAsync(source, original);

        var firstService = new FileEncryptionService();
        await firstService.EncryptFileAsync(source, encrypted, FilePassword);

        var secondService = new FileEncryptionService();
        await secondService.DecryptFileAsync(encrypted, restored, FilePassword);

        Assert.Equal(original, await File.ReadAllBytesAsync(restored));
        Assert.True(File.Exists(source));
    }

    [Fact]
    public async Task R2kenc02_FreshServicesReloadKeyPackageAndDecryptPersistedContainer()
    {
        using var temp = new TempDirectory();
        var source = temp.PathFor("v2-source.bin");
        var encrypted = temp.PathFor("v2-source.bin.r2kenc");
        var keyPackage = temp.PathFor("v2-key.r2kkey");
        var restored = temp.PathFor("v2-restored.bin");
        var original = RandomNumberGenerator.GetBytes((512 * 1024) + 23);
        await File.WriteAllBytesAsync(source, original);

        var firstKeys = new KeyManagerService();
        var firstCrypto = new KeyFileEncryptionService();
        using (var key = firstKeys.Generate("Restart v2 key"))
        {
            await firstKeys.ExportAsync(key, keyPackage, PackagePassword);
            await firstCrypto.EncryptFileAsync(source, encrypted, FilePassword, key);
        }

        var secondKeys = new KeyManagerService();
        var secondCrypto = new KeyFileEncryptionService();
        using var reloadedKey = await secondKeys.ImportAsync(keyPackage, PackagePassword);
        await secondCrypto.DecryptFileAsync(encrypted, restored, FilePassword, reloadedKey);

        Assert.Equal(original, await File.ReadAllBytesAsync(restored));
        Assert.True(File.Exists(source));
        Assert.True(File.Exists(keyPackage));
    }

    [Fact]
    public async Task R2kenc03_FreshServicesReloadPrivateIdentityAndDecryptPersistedContainer()
    {
        using var temp = new TempDirectory();
        var source = temp.PathFor("v3-source.bin");
        var encrypted = temp.PathFor("v3-source.bin.r2kenc");
        var identityPackage = temp.PathFor("recipient.r2kid");
        var restored = temp.PathFor("v3-restored.bin");
        var original = RandomNumberGenerator.GetBytes((512 * 1024) + 47);
        await File.WriteAllBytesAsync(source, original);

        var firstIdentities = new IdentityService();
        var firstCrypto = new RecipientFileEncryptionService();
        using (var identity = firstIdentities.Generate("Restart recipient"))
        {
            await firstIdentities.ExportPrivateAsync(identity, identityPackage, PackagePassword);
            await firstCrypto.EncryptForRecipientAsync(source, encrypted, identity.ToPublicIdentity());
        }

        var secondIdentities = new IdentityService();
        var secondCrypto = new RecipientFileEncryptionService();
        using var reloadedIdentity = await secondIdentities.ImportPrivateAsync(identityPackage, PackagePassword);
        await secondCrypto.DecryptAsync(encrypted, restored, reloadedIdentity);

        Assert.Equal(original, await File.ReadAllBytesAsync(restored));
        Assert.True(File.Exists(source));
        Assert.True(File.Exists(identityPackage));
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
