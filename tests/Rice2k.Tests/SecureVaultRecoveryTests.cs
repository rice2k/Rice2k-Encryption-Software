using Rice2k.Encryption.Services;

namespace Rice2k.Tests;

public sealed class SecureVaultRecoveryTests
{
    private const string Password = "correct horse battery staple 2026";
    private readonly SecureVaultService _service = new();

    [Fact]
    public async Task VerifyRecoveryBackup_AuthenticatesPreservedOlderState()
    {
        using var temp = new TempDirectory();
        var vaultPath = temp.PathFor("recovery.r2kvault");
        var source1 = temp.PathFor("one.txt");
        var source2 = temp.PathFor("two.txt");
        var oldSnapshot = temp.PathFor("old.snapshot");
        await File.WriteAllTextAsync(source1, "one");
        await File.WriteAllTextAsync(source2, "two");

        using var session = await _service.CreateAsync(vaultPath, Password);
        await _service.AddFileAsync(session, source1, "one.txt");
        File.Copy(vaultPath, oldSnapshot);
        var oldSequence = session.Sequence;

        await _service.AddFileAsync(session, source2, "two.txt");
        File.Copy(oldSnapshot, SecureVaultService.RecoveryBackupPath(vaultPath));

        var info = await _service.VerifyRecoveryBackupAsync(session);

        Assert.Equal(oldSequence, info.Sequence);
        Assert.Equal(1, info.EntryCount);
        Assert.True(info.FileSize > 0);
    }

    [Fact]
    public async Task RestoreRecoveryBackup_RestoresOldStateAndPreservesCurrentState()
    {
        using var temp = new TempDirectory();
        var vaultPath = temp.PathFor("restore.r2kvault");
        var source1 = temp.PathFor("one.txt");
        var source2 = temp.PathFor("two.txt");
        var oldSnapshot = temp.PathFor("old.snapshot");
        await File.WriteAllTextAsync(source1, "one");
        await File.WriteAllTextAsync(source2, "two");

        using var session = await _service.CreateAsync(vaultPath, Password);
        await _service.AddFileAsync(session, source1, "one.txt");
        File.Copy(vaultPath, oldSnapshot);
        var oldSequence = session.Sequence;

        await _service.AddFileAsync(session, source2, "two.txt");
        Assert.Equal(2, session.Entries.Count);
        File.Copy(oldSnapshot, SecureVaultService.RecoveryBackupPath(vaultPath));

        var archivedCurrent = await _service.RestoreRecoveryBackupAsync(session);

        Assert.True(File.Exists(archivedCurrent));
        Assert.False(File.Exists(SecureVaultService.RecoveryBackupPath(vaultPath)));
        Assert.Equal(oldSequence, session.Sequence);
        Assert.Single(session.Entries);
        Assert.Equal("one.txt", session.Entries[0].Path);
        await _service.VerifyAsync(session);

        using var archivedSession = await _service.UnlockAsync(archivedCurrent, Password);
        Assert.Equal(2, archivedSession.Entries.Count);
    }

    [Fact]
    public async Task PreserveRecoveryBackup_VerifiesThenMovesWithoutOverwriting()
    {
        using var temp = new TempDirectory();
        var vaultPath = temp.PathFor("preserve.r2kvault");
        var source = temp.PathFor("file.txt");
        var preserved = temp.PathFor("saved-recovery.r2kvault");
        await File.WriteAllTextAsync(source, "preserve me");

        using var session = await _service.CreateAsync(vaultPath, Password);
        await _service.AddFileAsync(session, source, "file.txt");
        File.Copy(vaultPath, SecureVaultService.RecoveryBackupPath(vaultPath));

        await _service.PreserveRecoveryBackupAsync(session, preserved);

        Assert.False(File.Exists(SecureVaultService.RecoveryBackupPath(vaultPath)));
        Assert.True(File.Exists(preserved));
        using var preservedSession = await _service.UnlockAsync(preserved, Password);
        Assert.Single(preservedSession.Entries);
    }

    [Fact]
    public async Task PreserveRecoveryBackup_ExistingDestinationIsNotOverwritten()
    {
        using var temp = new TempDirectory();
        var vaultPath = temp.PathFor("preserve-existing.r2kvault");
        var preserved = temp.PathFor("existing.r2kvault");
        await File.WriteAllTextAsync(preserved, "do not overwrite");

        using var session = await _service.CreateAsync(vaultPath, Password);
        File.Copy(vaultPath, SecureVaultService.RecoveryBackupPath(vaultPath));
        var before = await File.ReadAllTextAsync(preserved);

        await Assert.ThrowsAsync<IOException>(() =>
            _service.PreserveRecoveryBackupAsync(session, preserved));

        Assert.Equal(before, await File.ReadAllTextAsync(preserved));
        Assert.True(File.Exists(SecureVaultService.RecoveryBackupPath(vaultPath)));
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
