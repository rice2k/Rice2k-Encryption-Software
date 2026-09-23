using System.Security.Cryptography;
using Rice2k.Encryption.Models;
using Rice2k.Encryption.Services;

namespace Rice2k.Tests;

public sealed class FolderProtectionServiceTests
{
    private const string Password = "correct horse battery staple 2026";

    [Fact]
    public async Task ProtectFolder_PreservesRelativeStructureAndSourceFiles()
    {
        using var temp = new TempDirectory();
        var source = temp.CreateDirectory("Family Photos");
        var nested = Directory.CreateDirectory(Path.Combine(source, "2026", "September")).FullName;
        var first = Path.Combine(source, "cover.txt");
        var second = Path.Combine(nested, "photo.bin");
        var secondBytes = RandomNumberGenerator.GetBytes(128 * 1024 + 17);
        await File.WriteAllTextAsync(first, "folder protection test");
        await File.WriteAllBytesAsync(second, secondBytes);

        var destination = temp.PathFor("family-photos.r2kvault");
        var service = new FolderProtectionService();
        var plan = await service.PrepareAsync(source, destination);

        Assert.Equal(2, plan.Files.Count);
        Assert.Equal(new FileInfo(first).Length + secondBytes.Length, plan.TotalBytes);
        Assert.Equal("Family Photos", plan.VaultRootName);

        await service.ProtectAsync(plan, Password);

        Assert.True(File.Exists(first));
        Assert.Equal(secondBytes, await File.ReadAllBytesAsync(second));
        Assert.True(File.Exists(destination));

        var vault = new SecureVaultService();
        using var session = await vault.UnlockAsync(destination, Password);
        Assert.Contains(session.Entries, entry => entry.Path == "Family Photos/cover.txt");
        Assert.Contains(session.Entries, entry => entry.Path == "Family Photos/2026/September/photo.bin");
        await vault.VerifyAsync(session);
    }

    [Fact]
    public async Task Prepare_DestinationInsideSourceFolder_IsRejected()
    {
        using var temp = new TempDirectory();
        var source = temp.CreateDirectory("Source");
        await File.WriteAllTextAsync(Path.Combine(source, "file.txt"), "data");
        var destination = Path.Combine(source, "self.r2kvault");
        var service = new FolderProtectionService();

        var error = await Assert.ThrowsAsync<IOException>(() =>
            service.PrepareAsync(source, destination));

        Assert.Contains("outside", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(destination));
    }

    [Fact]
    public async Task Prepare_ExistingDestination_IsRejectedWithoutOverwrite()
    {
        using var temp = new TempDirectory();
        var source = temp.CreateDirectory("Source");
        await File.WriteAllTextAsync(Path.Combine(source, "file.txt"), "data");
        var destination = temp.PathFor("existing.r2kvault");
        await File.WriteAllTextAsync(destination, "keep me");
        var before = await File.ReadAllBytesAsync(destination);
        var service = new FolderProtectionService();

        await Assert.ThrowsAsync<IOException>(() => service.PrepareAsync(source, destination));

        Assert.Equal(before, await File.ReadAllBytesAsync(destination));
    }

    [Fact]
    public async Task ProtectFolder_CancelDuringEncryption_RemovesOnlyEmptyCreatedVault()
    {
        using var temp = new TempDirectory();
        var source = temp.CreateDirectory("Large Folder");
        var sourceFile = Path.Combine(source, "large.bin");
        await File.WriteAllBytesAsync(sourceFile, RandomNumberGenerator.GetBytes((9 * 1024 * 1024) + 41));
        var originalHash = SHA256.HashData(await File.ReadAllBytesAsync(sourceFile));
        var destination = temp.PathFor("large.r2kvault");
        var service = new FolderProtectionService();
        var plan = await service.PrepareAsync(source, destination);
        using var cts = new CancellationTokenSource();
        var progress = new CallbackProgress(value =>
        {
            if (value.Stage == "Encrypting new vault data" && value.BytesProcessed > 0)
                cts.Cancel();
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.ProtectAsync(plan, Password, progress, cts.Token));

        Assert.True(File.Exists(sourceFile));
        Assert.Equal(originalHash, SHA256.HashData(await File.ReadAllBytesAsync(sourceFile)));
        Assert.False(File.Exists(destination));
        Assert.False(File.Exists(SecureVaultService.RecoveryBackupPath(destination)));
    }

    [Fact]
    public async Task Prepare_EmptyFolder_IsRejected()
    {
        using var temp = new TempDirectory();
        var source = temp.CreateDirectory("Empty");
        var destination = temp.PathFor("empty.r2kvault");
        var service = new FolderProtectionService();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.PrepareAsync(source, destination));

        Assert.Contains("does not contain", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(destination));
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

        public string CreateDirectory(string name)
        {
            var path = Path.Combine(DirectoryPath, name);
            Directory.CreateDirectory(path);
            return path;
        }

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
