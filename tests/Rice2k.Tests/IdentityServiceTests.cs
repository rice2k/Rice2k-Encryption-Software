using System.Security.Cryptography;
using Rice2k.Encryption.Services;

namespace Rice2k.Tests;

public sealed class IdentityServiceTests
{
    private const string Password = "correct horse battery staple 2026";

    [Fact]
    public async Task PrivateIdentity_RoundTrip_PreservesFingerprintAndPublicKeys()
    {
        using var temp = new TempDirectory();
        var service = new IdentityService();
        using var original = service.Generate("Alice");
        var path = temp.PathFor("alice.r2kid");
        var fingerprint = original.Fingerprint;
        var encryptionPublic = original.EncryptionPublicKey.ToArray();
        var signingPublic = original.SigningPublicKey.ToArray();

        await service.ExportPrivateAsync(original, path, Password);
        using var restored = await service.ImportPrivateAsync(path, Password);

        Assert.Equal(original.Id, restored.Id);
        Assert.Equal("Alice", restored.Name);
        Assert.Equal(fingerprint, restored.Fingerprint);
        Assert.Equal(encryptionPublic, restored.EncryptionPublicKey);
        Assert.Equal(signingPublic, restored.SigningPublicKey);
    }

    [Fact]
    public void Generate_OversizedDisplayName_IsRejected()
    {
        var service = new IdentityService();

        var error = Assert.Throws<InvalidDataException>(() =>
            service.Generate(new string('A', 201)));

        Assert.Contains("200", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PrivateIdentity_WrongPassword_FailsClosed()
    {
        using var temp = new TempDirectory();
        var service = new IdentityService();
        using var identity = service.Generate("Alice");
        var path = temp.PathFor("alice.r2kid");
        await service.ExportPrivateAsync(identity, path, Password);

        await Assert.ThrowsAsync<CryptographicException>(() =>
            service.ImportPrivateAsync(path, "wrong password but long enough"));
    }

    [Fact]
    public async Task PrivateIdentity_ModifiedCiphertext_FailsAuthentication()
    {
        using var temp = new TempDirectory();
        var service = new IdentityService();
        using var identity = service.Generate("Alice");
        var path = temp.PathFor("alice.r2kid");
        await service.ExportPrivateAsync(identity, path, Password);

        var bytes = await File.ReadAllBytesAsync(path);
        bytes[^1] ^= 0x20;
        await File.WriteAllBytesAsync(path, bytes);

        await Assert.ThrowsAsync<CryptographicException>(() =>
            service.ImportPrivateAsync(path, Password));
    }

    [Fact]
    public async Task PublicIdentity_ExportImport_VerifiesSelfSignatureAndFingerprint()
    {
        using var temp = new TempDirectory();
        var service = new IdentityService();
        using var identity = service.Generate("Alice");
        var path = temp.PathFor("alice.r2kpub");

        await service.ExportPublicAsync(identity, path);
        var publicIdentity = await service.ImportPublicAsync(path);

        Assert.Equal(identity.Id, publicIdentity.Id);
        Assert.Equal(identity.Name, publicIdentity.Name);
        Assert.Equal(identity.Fingerprint, publicIdentity.Fingerprint);
        Assert.Equal(identity.EncryptionPublicKey, publicIdentity.EncryptionPublicKey);
        Assert.Equal(identity.SigningPublicKey, publicIdentity.SigningPublicKey);
    }

    [Fact]
    public async Task PublicIdentity_ModifiedName_InvalidatesSelfSignature()
    {
        using var temp = new TempDirectory();
        var service = new IdentityService();
        using var identity = service.Generate("Alice");
        var path = temp.PathFor("alice.r2kpub");
        await service.ExportPublicAsync(identity, path);

        var json = await File.ReadAllTextAsync(path);
        json = json.Replace("\"Alice\"", "\"Mallory\"", StringComparison.Ordinal);
        await File.WriteAllTextAsync(path, json);

        await Assert.ThrowsAsync<CryptographicException>(() => service.ImportPublicAsync(path));
    }

    [Fact]
    public async Task ExportPrivate_ExistingDestination_IsNotOverwritten()
    {
        using var temp = new TempDirectory();
        var service = new IdentityService();
        using var identity = service.Generate("Alice");
        var path = temp.PathFor("existing.r2kid");
        await File.WriteAllTextAsync(path, "keep me");
        var before = await File.ReadAllBytesAsync(path);

        await Assert.ThrowsAsync<IOException>(() =>
            service.ExportPrivateAsync(identity, path, Password));

        Assert.Equal(before, await File.ReadAllBytesAsync(path));
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            DirectoryPath = Path.Combine(Path.GetTempPath(), "Rice2k.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DirectoryPath);
        }

        public string DirectoryPath { get; }
        public string PathFor(string name) => Path.Combine(DirectoryPath, name);

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(DirectoryPath))
                    Directory.Delete(DirectoryPath, recursive: true);
            }
            catch
            {
                // Best effort test cleanup.
            }
        }
    }
}
