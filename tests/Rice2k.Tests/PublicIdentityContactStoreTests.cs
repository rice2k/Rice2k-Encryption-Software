using Rice2k.Encryption.Services;

namespace Rice2k.Tests;

public sealed class PublicIdentityContactStoreTests
{
    [Fact]
    public async Task ImportLoadRemove_RoundTrip_PreservesValidatedPublicIdentity()
    {
        using var temp = new TempDirectory();
        var identityService = new IdentityService();
        var store = new PublicIdentityContactStore(temp.ContactsDirectory);
        using var identity = identityService.Generate("Contact Test");
        var publicCard = temp.PathFor("contact.r2kpub");
        await identityService.ExportPublicAsync(identity, publicCard);

        var imported = await store.ImportAsync(publicCard);
        var loaded = await store.LoadAllAsync();

        var contact = Assert.Single(loaded);
        Assert.Equal(identity.Id, imported.Id);
        Assert.Equal(identity.Fingerprint, imported.Fingerprint);
        Assert.Equal(identity.Id, contact.Id);
        Assert.Equal(identity.Fingerprint, contact.Fingerprint);
        Assert.Equal(identity.Name, contact.Name);
        Assert.Equal(identity.EncryptionPublicKey, contact.EncryptionPublicKey);
        Assert.Equal(identity.SigningPublicKey, contact.SigningPublicKey);
        Assert.True(File.Exists(Path.Combine(temp.ContactsDirectory, $"{identity.Id:N}.r2kpub")));

        await store.RemoveAsync(contact);
        Assert.Empty(await store.LoadAllAsync());
    }

    [Fact]
    public async Task LoadAll_TamperedSavedContact_IsOmittedWithoutDeletingArtifact()
    {
        using var temp = new TempDirectory();
        var identityService = new IdentityService();
        var store = new PublicIdentityContactStore(temp.ContactsDirectory);
        using var identity = identityService.Generate("Tamper Test");
        var source = temp.PathFor("tamper.r2kpub");
        await identityService.ExportPublicAsync(identity, source);
        await store.ImportAsync(source);

        var savedPath = Path.Combine(temp.ContactsDirectory, $"{identity.Id:N}.r2kpub");
        var text = await File.ReadAllTextAsync(savedPath);
        text = text.Replace("Tamper Test", "Changed Label", StringComparison.Ordinal);
        await File.WriteAllTextAsync(savedPath, text);

        Assert.Empty(await store.LoadAllAsync());
        Assert.True(File.Exists(savedPath));
    }

    [Fact]
    public async Task Import_SameValidatedContactTwice_IsIdempotent()
    {
        using var temp = new TempDirectory();
        var identityService = new IdentityService();
        var store = new PublicIdentityContactStore(temp.ContactsDirectory);
        using var identity = identityService.Generate("Repeat Contact");
        var source = temp.PathFor("repeat.r2kpub");
        await identityService.ExportPublicAsync(identity, source);

        var first = await store.ImportAsync(source);
        var second = await store.ImportAsync(source);
        var loaded = await store.LoadAllAsync();

        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.Single(loaded);
    }

    [Fact]
    public async Task Remove_TamperedSavedContact_FailsClosed()
    {
        using var temp = new TempDirectory();
        var identityService = new IdentityService();
        var store = new PublicIdentityContactStore(temp.ContactsDirectory);
        using var identity = identityService.Generate("Removal Guard");
        var source = temp.PathFor("remove.r2kpub");
        await identityService.ExportPublicAsync(identity, source);
        var contact = await store.ImportAsync(source);

        var savedPath = Path.Combine(temp.ContactsDirectory, $"{identity.Id:N}.r2kpub");
        var bytes = await File.ReadAllBytesAsync(savedPath);
        bytes[^1] ^= 0x01;
        await File.WriteAllBytesAsync(savedPath, bytes);

        await Assert.ThrowsAnyAsync<Exception>(() => store.RemoveAsync(contact));
        Assert.True(File.Exists(savedPath));
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            DirectoryPath = Path.Combine(Path.GetTempPath(), "Rice2k.Tests", Guid.NewGuid().ToString("N"));
            ContactsDirectory = Path.Combine(DirectoryPath, "contacts");
            Directory.CreateDirectory(DirectoryPath);
        }

        public string DirectoryPath { get; }
        public string ContactsDirectory { get; }
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
