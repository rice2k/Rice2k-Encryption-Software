using Rice2k.Encryption.Services;

namespace Rice2k.Tests;

public sealed class SecureVaultFaultInjectionTests
{
    private const string Password = "correct horse battery staple 2026";

    [Fact]
    public async Task FailureAfterPendingVerification_LeavesActiveVaultUnchanged()
    {
        using var temp = new TempDirectory();
        var service = new SecureVaultService();
        var vaultPath = temp.PathFor("pending-failure.r2kvault");
        var first = temp.PathFor("one.txt");
        var second = temp.PathFor("two.txt");
        await File.WriteAllTextAsync(first, "one");
        await File.WriteAllTextAsync(second, "two");

        using var session = await service.CreateAsync(vaultPath, Password);
        await service.AddFileAsync(session, first, "one.txt");
        var sequenceBefore = session.Sequence;

        service.MutationCheckpointForTesting = checkpoint =>
        {
            if (checkpoint == VaultMutationCheckpoint.PendingVaultVerified)
                throw new InvalidOperationException("Injected failure after pending verification.");
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AddFileWithProgressAsync(session, second, "two.txt"));

        service.MutationCheckpointForTesting = null;
        Assert.Equal(sequenceBefore, session.Sequence);
        Assert.Single(session.Entries);
        Assert.False(File.Exists(SecureVaultService.RecoveryBackupPath(vaultPath)));
        Assert.Empty(Directory.GetFiles(temp.DirectoryPath, "*.pending"));

        using var reopened = await service.UnlockAsync(vaultPath, Password);
        Assert.Single(reopened.Entries);
        Assert.Equal("one.txt", reopened.Entries[0].Path);
        await service.VerifyAsync(reopened);
    }

    [Fact]
    public async Task FailureImmediatelyAfterReplacement_RestoresLastKnownGoodVault()
    {
        using var temp = new TempDirectory();
        var service = new SecureVaultService();
        var vaultPath = temp.PathFor("replace-failure.r2kvault");
        var first = temp.PathFor("one.txt");
        var second = temp.PathFor("two.txt");
        await File.WriteAllTextAsync(first, "one");
        await File.WriteAllTextAsync(second, "two");

        using var session = await service.CreateAsync(vaultPath, Password);
        await service.AddFileAsync(session, first, "one.txt");
        var sequenceBefore = session.Sequence;

        service.MutationCheckpointForTesting = checkpoint =>
        {
            if (checkpoint == VaultMutationCheckpoint.ActiveVaultReplaced)
                throw new InvalidOperationException("Injected failure after active-vault replacement.");
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AddFileWithProgressAsync(session, second, "two.txt"));

        service.MutationCheckpointForTesting = null;
        Assert.Equal(sequenceBefore, session.Sequence);
        Assert.Single(session.Entries);
        Assert.False(File.Exists(SecureVaultService.RecoveryBackupPath(vaultPath)));

        using var reopened = await service.UnlockAsync(vaultPath, Password);
        Assert.Equal(sequenceBefore, reopened.Sequence);
        Assert.Single(reopened.Entries);
        Assert.Equal("one.txt", reopened.Entries[0].Path);
        await service.VerifyAsync(reopened);
    }

    [Fact]
    public async Task FailureAfterFinalVerification_RollsBackBeforeRecoveryBackupIsReleased()
    {
        using var temp = new TempDirectory();
        var service = new SecureVaultService();
        var vaultPath = temp.PathFor("final-verify-failure.r2kvault");
        var first = temp.PathFor("one.txt");
        var second = temp.PathFor("two.txt");
        await File.WriteAllTextAsync(first, "one");
        await File.WriteAllTextAsync(second, "two");

        using var session = await service.CreateAsync(vaultPath, Password);
        await service.AddFileAsync(session, first, "one.txt");
        var sequenceBefore = session.Sequence;

        service.MutationCheckpointForTesting = checkpoint =>
        {
            if (checkpoint == VaultMutationCheckpoint.FinalVaultVerified)
                throw new InvalidOperationException("Injected failure after final verification.");
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AddFileWithProgressAsync(session, second, "two.txt"));

        service.MutationCheckpointForTesting = null;
        Assert.Equal(sequenceBefore, session.Sequence);
        Assert.Single(session.Entries);
        Assert.False(File.Exists(SecureVaultService.RecoveryBackupPath(vaultPath)));

        using var reopened = await service.UnlockAsync(vaultPath, Password);
        Assert.Equal(sequenceBefore, reopened.Sequence);
        Assert.Single(reopened.Entries);
        Assert.Equal("one.txt", reopened.Entries[0].Path);
        await service.VerifyAsync(reopened);
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
