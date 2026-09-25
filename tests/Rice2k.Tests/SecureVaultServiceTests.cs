using System.Security.Cryptography;
using Rice2k.Encryption.Services;

namespace Rice2k.Tests;

public sealed class SecureVaultServiceTests
{
    private const string Password = "correct horse battery staple 2026";
    private readonly SecureVaultService _service = new();

    [Fact]
    public async Task CreateUnlockLock_EmptyVaultRoundTrip()
    {
        using var temp = new TempDirectory();
        var vaultPath = temp.PathFor("empty.r2kvault");

        using (var created = await _service.CreateAsync(vaultPath, Password))
        {
            Assert.Empty(created.Entries);
            Assert.False(created.IsLocked);
            Assert.Equal(0, created.Sequence);
        }

        using var reopened = await _service.UnlockAsync(vaultPath, Password);
        Assert.Empty(reopened.Entries);
        Assert.False(reopened.IsLocked);
        await _service.VerifyAsync(reopened);
    }

    [Fact]
    public async Task AddExtract_RestoresAuthenticatedFile()
    {
        using var temp = new TempDirectory();
        var vaultPath = temp.PathFor("files.r2kvault");
        var source = temp.PathFor("source.bin");
        var restored = temp.PathFor("restored.bin");
        var original = RandomNumberGenerator.GetBytes(256 * 1024 + 31);
        await File.WriteAllBytesAsync(source, original);

        using var session = await _service.CreateAsync(vaultPath, Password);
        await _service.AddFileAsync(session, source, "Archive/source.bin");

        var entry = Assert.Single(session.Entries);
        Assert.Equal("Archive/source.bin", entry.Path);
        await _service.VerifyAsync(session);
        await _service.ExtractAsync(session, entry.Id, restored);

        Assert.Equal(original, await File.ReadAllBytesAsync(restored));
        Assert.False(File.Exists(SecureVaultService.RecoveryBackupPath(vaultPath)));
    }

    [Fact]
    public async Task RestartStyle_NewService_ReopensVerifiesAndExtractsPersistedVault()
    {
        using var temp = new TempDirectory();
        var vaultPath = temp.PathFor("restart.r2kvault");
        var source = temp.PathFor("restart-source.bin");
        var restored = temp.PathFor("restart-restored.bin");
        var original = RandomNumberGenerator.GetBytes((512 * 1024) + 37);
        await File.WriteAllBytesAsync(source, original);

        var firstService = new SecureVaultService();
        using (var created = await firstService.CreateAsync(vaultPath, Password))
        {
            await firstService.AddFileAsync(created, source, "Restart/source.bin");
            Assert.Single(created.Entries);
        }

        var secondService = new SecureVaultService();
        using var reopened = await secondService.UnlockAsync(vaultPath, Password);
        await secondService.VerifyAsync(reopened);

        var entry = Assert.Single(reopened.Entries);
        Assert.Equal("Restart/source.bin", entry.Path);
        await secondService.ExtractAsync(reopened, entry.Id, restored);

        Assert.Equal(original, await File.ReadAllBytesAsync(restored));
        Assert.True(File.Exists(source));
    }

    [Fact]
    public async Task RenameAndRemove_UpdateAuthenticatedManifest()
    {
        using var temp = new TempDirectory();
        var vaultPath = temp.PathFor("mutations.r2kvault");
        var source = temp.PathFor("note.txt");
        await File.WriteAllTextAsync(source, "vault mutation test");

        using var session = await _service.CreateAsync(vaultPath, Password);
        await _service.AddFileAsync(session, source, "Old/note.txt");
        var entryId = Assert.Single(session.Entries).Id;
        var afterAddSequence = session.Sequence;

        await _service.RenameEntryAsync(session, entryId, "New/note.txt");
        Assert.Equal("New/note.txt", Assert.Single(session.Entries).Path);
        Assert.True(session.Sequence > afterAddSequence);

        await _service.RemoveEntryAsync(session, entryId);
        Assert.Empty(session.Entries);
        await _service.VerifyAsync(session);

        using var reopened = await _service.UnlockAsync(vaultPath, Password);
        Assert.Empty(reopened.Entries);
    }

    [Fact]
    public async Task Unlock_WrongPassword_FailsClosed()
    {
        using var temp = new TempDirectory();
        var vaultPath = temp.PathFor("wrong-password.r2kvault");
        using (var session = await _service.CreateAsync(vaultPath, Password))
        {
        }

        await Assert.ThrowsAsync<CryptographicException>(() =>
            _service.UnlockAsync(vaultPath, "this is the wrong vault password"));
    }

    [Fact]
    public async Task Verify_ModifiedEncryptedFileData_FailsAuthentication()
    {
        using var temp = new TempDirectory();
        var vaultPath = temp.PathFor("tamper.r2kvault");
        var source = temp.PathFor("source.bin");
        await File.WriteAllBytesAsync(source, RandomNumberGenerator.GetBytes(8192));

        using var session = await _service.CreateAsync(vaultPath, Password);
        await _service.AddFileAsync(session, source, "source.bin");

        var bytes = await File.ReadAllBytesAsync(vaultPath);
        bytes[^1] ^= 0x40;
        await File.WriteAllBytesAsync(vaultPath, bytes);

        await Assert.ThrowsAsync<CryptographicException>(() => _service.VerifyAsync(session));
    }

    [Fact]
    public async Task AddFiles_DuplicateVaultPath_IsRejectedWithoutMutation()
    {
        using var temp = new TempDirectory();
        var vaultPath = temp.PathFor("duplicate.r2kvault");
        var first = temp.PathFor("first.txt");
        var second = temp.PathFor("second.txt");
        await File.WriteAllTextAsync(first, "first");
        await File.WriteAllTextAsync(second, "second");

        using var session = await _service.CreateAsync(vaultPath, Password);
        await _service.AddFileAsync(session, first, "Docs/file.txt");
        var sequence = session.Sequence;

        await Assert.ThrowsAsync<IOException>(() =>
            _service.AddFileAsync(session, second, "docs/FILE.txt"));

        Assert.Equal(sequence, session.Sequence);
        Assert.Single(session.Entries);
        await _service.VerifyAsync(session);
    }

    [Fact]
    public async Task Extract_ExistingDestination_IsNotOverwritten()
    {
        using var temp = new TempDirectory();
        var vaultPath = temp.PathFor("extract.r2kvault");
        var source = temp.PathFor("source.txt");
        var destination = temp.PathFor("existing.txt");
        await File.WriteAllTextAsync(source, "protected");
        await File.WriteAllTextAsync(destination, "keep me");

        using var session = await _service.CreateAsync(vaultPath, Password);
        await _service.AddFileAsync(session, source, "source.txt");
        var before = await File.ReadAllTextAsync(destination);

        await Assert.ThrowsAsync<IOException>(() =>
            _service.ExtractAsync(session, Assert.Single(session.Entries).Id, destination));

        Assert.Equal(before, await File.ReadAllTextAsync(destination));
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
