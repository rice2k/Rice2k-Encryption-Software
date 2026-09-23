using System.Security.Cryptography;
using Rice2k.Encryption.Models;
using Rice2k.Encryption.Services;

namespace Rice2k.Tests;

public sealed class SecureVaultProgressTests
{
    private const string Password = "correct horse battery staple 2026";

    [Fact]
    public async Task AddVerifyExtract_ReportMeasurableProgress()
    {
        using var temp = new TempDirectory();
        var service = new SecureVaultService();
        var vaultPath = temp.PathFor("progress.r2kvault");
        var source = temp.PathFor("source.bin");
        var restored = temp.PathFor("restored.bin");
        var original = RandomNumberGenerator.GetBytes((5 * 1024 * 1024) + 123);
        await File.WriteAllBytesAsync(source, original);

        using var session = await service.CreateAsync(vaultPath, Password);

        var addProgress = new ProgressCollector();
        await service.AddFileWithProgressAsync(session, source, "Archive/source.bin", addProgress);
        Assert.Contains(addProgress.Values, value => value.Stage == "Encrypting new vault data" && value.Percentage > 0);
        Assert.Contains(addProgress.Values, value => value.Stage == "Verifying pending vault");
        Assert.Contains(addProgress.Values, value => value.Stage == "Verifying finalized vault");
        Assert.Contains(addProgress.Values, value => value.Stage == "Complete" && value.Percentage == 100);

        var verifyProgress = new ProgressCollector();
        await service.VerifyWithProgressAsync(session, verifyProgress);
        Assert.Contains(verifyProgress.Values, value => value.Stage == "Verifying vault" && value.Percentage > 0);
        Assert.Contains(verifyProgress.Values, value => value.Stage == "Verification complete" && value.Percentage == 100);

        var entry = Assert.Single(session.Entries);
        var extractProgress = new ProgressCollector();
        await service.ExtractWithProgressAsync(session, entry.Id, restored, extractProgress);
        Assert.Contains(extractProgress.Values, value => value.Stage == "Restoring file" && value.Percentage > 0);
        Assert.Contains(extractProgress.Values, value => value.Stage == "Restore complete" && value.Percentage == 100);
        Assert.Equal(original, await File.ReadAllBytesAsync(restored));
    }

    [Fact]
    public async Task CancelDuringNewFileEncryption_DiscardsPendingVaultAndKeepsActiveState()
    {
        using var temp = new TempDirectory();
        var service = new SecureVaultService();
        var vaultPath = temp.PathFor("cancel-progress.r2kvault");
        var source = temp.PathFor("large.bin");
        await File.WriteAllBytesAsync(source, RandomNumberGenerator.GetBytes((9 * 1024 * 1024) + 17));

        using var session = await service.CreateAsync(vaultPath, Password);
        var sequenceBefore = session.Sequence;
        using var cts = new CancellationTokenSource();
        var progress = new CallbackProgress(value =>
        {
            if (value.Stage == "Encrypting new vault data" && value.BytesProcessed > 0)
                cts.Cancel();
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.AddFileWithProgressAsync(session, source, "large.bin", progress, cts.Token));

        Assert.Equal(sequenceBefore, session.Sequence);
        Assert.Empty(session.Entries);
        Assert.False(File.Exists(SecureVaultService.RecoveryBackupPath(vaultPath)));
        Assert.Empty(Directory.GetFiles(temp.DirectoryPath, "*.pending"));

        using var reopened = await service.UnlockAsync(vaultPath, Password);
        Assert.Empty(reopened.Entries);
        await service.VerifyAsync(reopened);
    }

    private sealed class ProgressCollector : IProgress<VaultOperationProgress>
    {
        public List<VaultOperationProgress> Values { get; } = [];
        public void Report(VaultOperationProgress value) => Values.Add(value);
    }

    private sealed class CallbackProgress(Action<VaultOperationProgress> callback) : IProgress<VaultOperationProgress>
    {
        public void Report(VaultOperationProgress value) => callback(value);
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
