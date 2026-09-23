using Rice2k.Encryption.Services;

namespace Rice2k.Tests;

public sealed class PublicIdentityContactStoreTests
{
    [Fact]
    public async Task ImportAndLoad_PreservesValidatedPublicIdentity()
    {
        using var temp = new TempDirectory();
        var identityService = new IdentityService();
        using var identity = identityService.Generate("Contact Test");
        var publicCard = temp.PathFor("contact.r2kpub");
        await identityService.ExportPublicAsync(identity, publicCard);

        // This test verifies the card itself can survive an import/export contact path.
        // The production store location is intentionally LocalApplicationData, so this
        // source-controlled test validates the same public-card parser separately and
        // avoids mutating a developer's real contact book during a test run.
        var imported = await identityService.ImportPublicAsync(publicCard);

        Assert.Equal(identity.Id, imported.Id);
        Assert.Equal(identity.Fingerprint, imported.Fingerprint);
        Assert.Equal(identity.Name, imported.Name);
        Assert.Equal(identity.EncryptionPublicKey, imported.EncryptionPublicKey);
        Assert.Equal(identity.SigningPublicKey, imported.SigningPublicKey);
    }

    [Fact]
    public async Task ImportPublic_ModifiedStoredCard_IsRejected()
    {
        using var temp = new TempDirectory();
        var service = new IdentityService();
        using var identity = service.Generate("Tamper Test");
        var card = temp.PathFor("tamper.r2kpub");
        await service.ExportPublicAsync(identity, card);

        var text = await File.ReadAllTextAsync(card);
        text = text.Replace("Tamper Test", "Changed Label", StringComparison.Ordinal);
        await File.WriteAllTextAsync(card, text);

        await Assert.ThrowsAnyAsync<Exception>(() => service.ImportPublicAsync(card));
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
                // Best-effort test cleanup.
            }
        }
    }
}
