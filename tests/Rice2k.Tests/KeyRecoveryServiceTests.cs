using System.Security.Cryptography;
using Rice2k.Encryption.Services;

namespace Rice2k.Tests;

public sealed class KeyRecoveryServiceTests
{
    private const string PackagePassword = "correct horse battery staple 2026";
    private const string RecoveryPassword = "separate recovery password 2026";

    [Fact]
    public async Task KeyPackage_RoundTrip_PreservesIdentityAndFingerprint()
    {
        var directory = CreateTempDirectory();
        try
        {
            var service = new KeyManagerService();
            using var original = service.Generate("Archive Key");
            var path = Path.Combine(directory, "archive.r2kkey");

            await service.ExportAsync(original, path, PackagePassword);
            using var imported = await service.ImportAsync(path, PackagePassword);

            Assert.Equal(original.Id, imported.Id);
            Assert.Equal(original.Name, imported.Name);
            Assert.Equal(original.Fingerprint, imported.Fingerprint);
            Assert.StartsWith("R2K-", imported.Fingerprint, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task KeyPackage_WrongPassword_FailsClosed()
    {
        var directory = CreateTempDirectory();
        try
        {
            var service = new KeyManagerService();
            using var original = service.Generate("Test Key");
            var path = Path.Combine(directory, "test.r2kkey");
            await service.ExportAsync(original, path, PackagePassword);

            await Assert.ThrowsAsync<CryptographicException>(() =>
                service.ImportAsync(path, "this is the wrong package password"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task KeyPackage_Tampering_FailsAuthentication()
    {
        var directory = CreateTempDirectory();
        try
        {
            var service = new KeyManagerService();
            using var original = service.Generate("Tamper Test");
            var path = Path.Combine(directory, "tamper.r2kkey");
            await service.ExportAsync(original, path, PackagePassword);

            var bytes = await File.ReadAllBytesAsync(path);
            bytes[^1] ^= 0x01;
            await File.WriteAllBytesAsync(path, bytes);

            await Assert.ThrowsAsync<CryptographicException>(() =>
                service.ImportAsync(path, PackagePassword));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RecoveryPackage_TestAndRestore_PreservesFingerprint()
    {
        var directory = CreateTempDirectory();
        try
        {
            var keys = new KeyManagerService();
            var recovery = new RecoveryPackageService();
            using var original = keys.Generate("Recovery Test Key");

            var recoveryPath = Path.Combine(directory, "backup.r2krecovery");
            await recovery.CreateAsync(original, recoveryPath, RecoveryPassword);

            using var recovered = await recovery.OpenAsync(recoveryPath, RecoveryPassword);
            Assert.Equal(original.Fingerprint, recovered.Fingerprint);
            Assert.Equal(original.Id, recovered.Id);

            var restoredKeyPath = Path.Combine(directory, "restored.r2kkey");
            await keys.ExportAsync(recovered, restoredKeyPath, PackagePassword);
            using var restored = await keys.ImportAsync(restoredKeyPath, PackagePassword);
            Assert.Equal(original.Fingerprint, restored.Fingerprint);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RecoveryPackage_WrongPassword_FailsClosed()
    {
        var directory = CreateTempDirectory();
        try
        {
            var keys = new KeyManagerService();
            var recovery = new RecoveryPackageService();
            using var original = keys.Generate("Recovery Failure Test");
            var recoveryPath = Path.Combine(directory, "backup.r2krecovery");
            await recovery.CreateAsync(original, recoveryPath, RecoveryPassword);

            await Assert.ThrowsAsync<CryptographicException>(() =>
                recovery.OpenAsync(recoveryPath, "wrong recovery password 2026"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "Rice2k.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
