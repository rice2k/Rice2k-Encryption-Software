using Rice2k.Encryption.Services;

namespace Rice2k.Tests;

public sealed class PackageFinalizationSafetyTests
{
    private const string Password = "correct horse battery staple 2026";

    [Fact]
    public async Task KeyPackage_PreCancelledExport_LeavesNoFinalOrPartialOutput()
    {
        using var temp = new TempDirectory();
        var service = new KeyManagerService();
        using var key = service.Generate("Cancellation Key");
        var destination = temp.PathFor("cancelled.r2kkey");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.ExportAsync(key, destination, Password, cts.Token));

        Assert.False(File.Exists(destination));
        Assert.Empty(Directory.GetFiles(temp.DirectoryPath, "*.partial"));
    }

    [Fact]
    public async Task KeyPackage_ExistingDestination_IsNeverOverwritten()
    {
        using var temp = new TempDirectory();
        var service = new KeyManagerService();
        using var key = service.Generate("Collision Key");
        var destination = temp.PathFor("existing.r2kkey");
        await File.WriteAllTextAsync(destination, "existing key destination");
        var before = await File.ReadAllBytesAsync(destination);

        await Assert.ThrowsAsync<IOException>(() =>
            service.ExportAsync(key, destination, Password));

        Assert.Equal(before, await File.ReadAllBytesAsync(destination));
        Assert.Empty(Directory.GetFiles(temp.DirectoryPath, "*.partial"));
    }

    [Fact]
    public async Task RecoveryPackage_PreCancelledCreate_LeavesNoFinalOrPartialOutput()
    {
        using var temp = new TempDirectory();
        var keys = new KeyManagerService();
        var service = new RecoveryPackageService();
        using var key = keys.Generate("Recovery Cancellation Key");
        var destination = temp.PathFor("cancelled.r2krecovery");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.CreateAsync(key, destination, Password, cts.Token));

        Assert.False(File.Exists(destination));
        Assert.Empty(Directory.GetFiles(temp.DirectoryPath, "*.partial"));
    }

    [Fact]
    public async Task RecoveryPackage_ExistingDestination_IsNeverOverwritten()
    {
        using var temp = new TempDirectory();
        var keys = new KeyManagerService();
        var service = new RecoveryPackageService();
        using var key = keys.Generate("Recovery Collision Key");
        var destination = temp.PathFor("existing.r2krecovery");
        await File.WriteAllTextAsync(destination, "existing recovery destination");
        var before = await File.ReadAllBytesAsync(destination);

        await Assert.ThrowsAsync<IOException>(() =>
            service.CreateAsync(key, destination, Password));

        Assert.Equal(before, await File.ReadAllBytesAsync(destination));
        Assert.Empty(Directory.GetFiles(temp.DirectoryPath, "*.partial"));
    }

    [Fact]
    public async Task PrivateIdentity_PreCancelledExport_LeavesNoFinalOrPartialOutput()
    {
        using var temp = new TempDirectory();
        var service = new IdentityService();
        using var identity = service.Generate("Cancellation Identity");
        var destination = temp.PathFor("cancelled.r2kid");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.ExportPrivateAsync(identity, destination, Password, cts.Token));

        Assert.False(File.Exists(destination));
        Assert.Empty(Directory.GetFiles(temp.DirectoryPath, "*.partial"));
    }

    [Fact]
    public async Task PrivateIdentity_ExistingDestination_IsNeverOverwritten()
    {
        using var temp = new TempDirectory();
        var service = new IdentityService();
        using var identity = service.Generate("Identity Collision");
        var destination = temp.PathFor("existing.r2kid");
        await File.WriteAllTextAsync(destination, "existing private identity destination");
        var before = await File.ReadAllBytesAsync(destination);

        await Assert.ThrowsAsync<IOException>(() =>
            service.ExportPrivateAsync(identity, destination, Password));

        Assert.Equal(before, await File.ReadAllBytesAsync(destination));
        Assert.Empty(Directory.GetFiles(temp.DirectoryPath, "*.partial"));
    }

    [Fact]
    public async Task PublicIdentity_PreCancelledExport_LeavesNoFinalOrPartialOutput()
    {
        using var temp = new TempDirectory();
        var service = new IdentityService();
        using var identity = service.Generate("Public Cancellation Identity");
        var destination = temp.PathFor("cancelled.r2kpub");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.ExportPublicAsync(identity, destination, cts.Token));

        Assert.False(File.Exists(destination));
        Assert.Empty(Directory.GetFiles(temp.DirectoryPath, "*.partial"));
    }

    [Fact]
    public async Task PublicIdentity_ExistingDestination_IsNeverOverwritten()
    {
        using var temp = new TempDirectory();
        var service = new IdentityService();
        using var identity = service.Generate("Public Identity Collision");
        var destination = temp.PathFor("existing.r2kpub");
        await File.WriteAllTextAsync(destination, "existing public identity destination");
        var before = await File.ReadAllBytesAsync(destination);

        await Assert.ThrowsAsync<IOException>(() =>
            service.ExportPublicAsync(identity, destination));

        Assert.Equal(before, await File.ReadAllBytesAsync(destination));
        Assert.Empty(Directory.GetFiles(temp.DirectoryPath, "*.partial"));
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
