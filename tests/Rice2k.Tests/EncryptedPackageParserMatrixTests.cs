using System.Buffers.Binary;
using Rice2k.Encryption.Services;

namespace Rice2k.Tests;

public sealed class EncryptedPackageParserMatrixTests
{
    private const string Password = "correct horse battery staple 2026";
    private const int VersionOffset = 8;
    private const int KdfOffset = VersionOffset + 1;
    private const int OperationsLimitOffset = KdfOffset + 1;
    private const int MemLimitOffset = OperationsLimitOffset + sizeof(long);
    private const int CipherLengthOffset = MemLimitOffset + sizeof(int) + 16 + 24;
    private const int CipherOffset = CipherLengthOffset + sizeof(int);

    [Fact]
    public async Task KeyPackage_MalformedHeaderMatrix_IsRejected()
    {
        using var temp = new TempDirectory();
        var service = new KeyManagerService();
        using var key = service.Generate("Parser Matrix Key");
        var fixture = temp.PathFor("fixture.r2kkey");
        await service.ExportAsync(key, fixture, Password);
        var baseline = await File.ReadAllBytesAsync(fixture);

        await AssertMatrixRejectedAsync(
            temp,
            baseline,
            ".r2kkey",
            async path =>
            {
                using var imported = await service.ImportAsync(path, Password);
            });

        Assert.Equal(baseline, await File.ReadAllBytesAsync(fixture));
    }

    [Fact]
    public async Task RecoveryPackage_MalformedHeaderMatrix_IsRejected()
    {
        using var temp = new TempDirectory();
        var keys = new KeyManagerService();
        var service = new RecoveryPackageService();
        using var key = keys.Generate("Recovery Parser Key");
        var fixture = temp.PathFor("fixture.r2krecovery");
        await service.CreateAsync(key, fixture, Password);
        var baseline = await File.ReadAllBytesAsync(fixture);

        await AssertMatrixRejectedAsync(
            temp,
            baseline,
            ".r2krecovery",
            async path =>
            {
                using var recovered = await service.OpenAsync(path, Password);
            });

        Assert.Equal(baseline, await File.ReadAllBytesAsync(fixture));
    }

    [Fact]
    public async Task PrivateIdentityPackage_MalformedHeaderMatrix_IsRejected()
    {
        using var temp = new TempDirectory();
        var service = new IdentityService();
        using var identity = service.Generate("Identity Parser Matrix");
        var fixture = temp.PathFor("fixture.r2kid");
        await service.ExportPrivateAsync(identity, fixture, Password);
        var baseline = await File.ReadAllBytesAsync(fixture);

        await AssertMatrixRejectedAsync(
            temp,
            baseline,
            ".r2kid",
            async path =>
            {
                using var imported = await service.ImportPrivateAsync(path, Password);
            });

        Assert.Equal(baseline, await File.ReadAllBytesAsync(fixture));
    }

    private static async Task AssertMatrixRejectedAsync(
        TempDirectory temp,
        byte[] baseline,
        string extension,
        Func<string, Task> openAsync)
    {
        Assert.True(baseline.Length > CipherOffset + 16);
        var cases = BuildCases();

        foreach (var testCase in cases)
        {
            var malformed = testCase.Mutate(baseline);
            var path = temp.PathFor($"{testCase.Name}{extension}");
            await File.WriteAllBytesAsync(path, malformed);

            Exception? expectedFailure = null;
            try
            {
                await openAsync(path);
            }
            catch (Exception ex) when (ex is InvalidDataException or NotSupportedException)
            {
                expectedFailure = ex;
            }

            Assert.NotNull(expectedFailure);
        }
    }

    private static (string Name, Func<byte[], byte[]> Mutate)[] BuildCases() =>
    [
        ("bad-magic", bytes => Mutate(bytes, copy => copy[0] ^= 0x20)),
        ("unsupported-version", bytes => Mutate(bytes, copy => copy[VersionOffset] = 99)),
        ("unsupported-kdf", bytes => Mutate(bytes, copy => copy[KdfOffset] = 99)),
        ("argon-ops-too-low", bytes => Mutate(bytes, copy => BinaryPrimitives.WriteInt64LittleEndian(copy.AsSpan(OperationsLimitOffset, sizeof(long)), 2))),
        ("argon-ops-too-high", bytes => Mutate(bytes, copy => BinaryPrimitives.WriteInt64LittleEndian(copy.AsSpan(OperationsLimitOffset, sizeof(long)), 999))),
        ("argon-memory-too-low", bytes => Mutate(bytes, copy => BinaryPrimitives.WriteInt32LittleEndian(copy.AsSpan(MemLimitOffset, sizeof(int)), 1024))),
        ("argon-memory-too-high", bytes => Mutate(bytes, copy => BinaryPrimitives.WriteInt32LittleEndian(copy.AsSpan(MemLimitOffset, sizeof(int)), int.MaxValue))),
        ("cipher-length-zero", bytes => Mutate(bytes, copy => BinaryPrimitives.WriteInt32LittleEndian(copy.AsSpan(CipherLengthOffset, sizeof(int)), 0))),
        ("cipher-length-huge", bytes => Mutate(bytes, copy => BinaryPrimitives.WriteInt32LittleEndian(copy.AsSpan(CipherLengthOffset, sizeof(int)), int.MaxValue))),
        ("truncated-fixed-header", bytes => bytes[..20]),
        ("truncated-cipher", bytes => bytes[..checked(CipherOffset + 5)]),
        ("unexpected-trailing-data", bytes => bytes.Concat([0x52, 0x32, 0x4B]).ToArray())
    ];

    private static byte[] Mutate(byte[] source, Action<byte[]> mutation)
    {
        var copy = source.ToArray();
        mutation(copy);
        return copy;
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
