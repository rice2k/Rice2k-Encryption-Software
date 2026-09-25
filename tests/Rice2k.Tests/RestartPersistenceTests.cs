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

    [Fact]
    public async Task R2kkey_FreshServiceImportsPersistedKeyPackage()
    {
        using var temp = new TempDirectory();
        var package = temp.PathFor("restart-key.r2kkey");
        Guid keyId;
        string fingerprint;

        var firstService = new KeyManagerService();
        using (var key = firstService.Generate("Persisted restart key"))
        {
            keyId = key.Id;
            fingerprint = key.Fingerprint;
            await firstService.ExportAsync(key, package, PackagePassword);
        }

        var secondService = new KeyManagerService();
        using var reopened = await secondService.ImportAsync(package, PackagePassword);

        Assert.Equal(keyId, reopened.Id);
        Assert.Equal(fingerprint, reopened.Fingerprint);
    }

    [Fact]
    public async Task R2krecovery_FreshServiceRecoversPersistedRecoveryPackage()
    {
        using var temp = new TempDirectory();
        var package = temp.PathFor("restart-key.r2krecovery");
        Guid keyId;
        string fingerprint;

        var keys = new KeyManagerService();
        var firstRecovery = new RecoveryPackageService();
        using (var key = keys.Generate("Persisted recovery key"))
        {
            keyId = key.Id;
            fingerprint = key.Fingerprint;
            await firstRecovery.CreateAsync(key, package, PackagePassword);
        }

        var secondRecovery = new RecoveryPackageService();
        using var reopened = await secondRecovery.OpenAsync(package, PackagePassword);

        Assert.Equal(keyId, reopened.Id);
        Assert.Equal(fingerprint, reopened.Fingerprint);
    }

    [Fact]
    public async Task R2kid_FreshServiceImportsPersistedPrivateIdentity()
    {
        using var temp = new TempDirectory();
        var package = temp.PathFor("restart-identity.r2kid");
        Guid identityId;
        string fingerprint;

        var firstService = new IdentityService();
        using (var identity = firstService.Generate("Persisted private identity"))
        {
            identityId = identity.Id;
            fingerprint = identity.Fingerprint;
            await firstService.ExportPrivateAsync(identity, package, PackagePassword);
        }

        var secondService = new IdentityService();
        using var reopened = await secondService.ImportPrivateAsync(package, PackagePassword);

        Assert.Equal(identityId, reopened.Id);
        Assert.Equal(fingerprint, reopened.Fingerprint);
    }

    [Fact]
    public async Task R2kpub_FreshServiceImportsPersistedPublicIdentity()
    {
        using var temp = new TempDirectory();
        var publicCard = temp.PathFor("restart-identity.r2kpub");
        Guid identityId;
        string fingerprint;

        var firstService = new IdentityService();
        using (var identity = firstService.Generate("Persisted public identity"))
        {
            identityId = identity.Id;
            fingerprint = identity.Fingerprint;
            await firstService.ExportPublicAsync(identity, publicCard);
        }

        var secondService = new IdentityService();
        var reopened = await secondService.ImportPublicAsync(publicCard);

        Assert.Equal(identityId, reopened.Id);
        Assert.Equal(fingerprint, reopened.Fingerprint);
    }

    [Fact]
    public async Task R2ksig_FreshServicesReloadPublicIdentityAndVerifyPersistedSignature()
    {
        using var temp = new TempDirectory();
        var source = temp.PathFor("signed-restart.bin");
        var signature = temp.PathFor("signed-restart.bin.r2ksig");
        var publicCard = temp.PathFor("signer.r2kpub");
        await File.WriteAllBytesAsync(source, RandomNumberGenerator.GetBytes((128 * 1024) + 29));

        var firstIdentities = new IdentityService();
        var firstSignatures = new FileSignatureService();
        using (var identity = firstIdentities.Generate("Restart signer"))
        {
            await firstIdentities.ExportPublicAsync(identity, publicCard);
            await firstSignatures.SignAsync(source, signature, identity);
        }

        var secondIdentities = new IdentityService();
        var secondSignatures = new FileSignatureService();
        var expectedSigner = await secondIdentities.ImportPublicAsync(publicCard);
        var result = await secondSignatures.VerifyAsync(source, signature, expectedSigner);

        Assert.True(result.CryptographicSignatureValid);
        Assert.True(result.FileContentMatches);
        Assert.True(result.MatchesExpectedIdentity);
        Assert.True(result.IsValid);
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
