using System.Buffers.Binary;
using System.Security.Cryptography;
using Rice2k.Encryption.Services;

namespace Rice2k.Tests;

public sealed class KeyFileContainerParserMatrixTests
{
    private const string Password = "correct horse battery staple 2026";
    private const int VersionOffset = 8;
    private const int AlgorithmOffset = VersionOffset + 1;
    private const int KdfOffset = AlgorithmOffset + 1;
    private const int ProtectionOffset = KdfOffset + 1;
    private const int OperationsLimitOffset = ProtectionOffset + 1;
    private const int MemLimitOffset = OperationsLimitOffset + sizeof(long);
    private const int ChunkSizeOffset = MemLimitOffset + sizeof(int);
    private const int FingerprintLengthOffset = ChunkSizeOffset + sizeof(int) + 16;

    [Fact]
    public async Task MalformedHeaderMatrix_FailsClosedWithoutProducingOutput()
    {
        using var temp = new TempDirectory();
        var service = new KeyFileEncryptionService();
        var keys = new KeyManagerService();
        using var key = keys.Generate("Parser Matrix Key");
        var source = temp.PathFor("fixture.txt");
        var fixture = temp.PathFor("fixture.txt.r2kenc");
        const string originalText = "Rice2k R2KENC02 parser fixture";
        await File.WriteAllTextAsync(source, originalText);
        await service.EncryptFileAsync(source, fixture, Password, key, verifyAfterEncrypt: false);

        var baseline = await File.ReadAllBytesAsync(fixture);
        var sourceHash = SHA256.HashData(await File.ReadAllBytesAsync(source));
        var fingerprintLength = baseline[FingerprintLengthOffset];
        var fingerprintOffset = FingerprintLengthOffset + 1;
        var metadataNonceOffset = checked(fingerprintOffset + fingerprintLength);
        var metadataCipherLengthOffset = checked(metadataNonceOffset + 24);
        var metadataCipherOffset = checked(metadataCipherLengthOffset + sizeof(int));

        var cases = new (string Name, Func<byte[], byte[]> Mutate)[]
        {
            ("bad-magic", bytes => Mutate(bytes, copy => copy[0] ^= 0x20)),
            ("unsupported-version", bytes => Mutate(bytes, copy => copy[VersionOffset] = 99)),
            ("unsupported-algorithm", bytes => Mutate(bytes, copy => copy[AlgorithmOffset] = 99)),
            ("unsupported-kdf", bytes => Mutate(bytes, copy => copy[KdfOffset] = 99)),
            ("unsupported-protection", bytes => Mutate(bytes, copy => copy[ProtectionOffset] = 99)),
            ("argon-ops-too-low", bytes => Mutate(bytes, copy => BinaryPrimitives.WriteInt64LittleEndian(copy.AsSpan(OperationsLimitOffset, sizeof(long)), 2))),
            ("argon-memory-too-low", bytes => Mutate(bytes, copy => BinaryPrimitives.WriteInt32LittleEndian(copy.AsSpan(MemLimitOffset, sizeof(int)), 1024))),
            ("chunk-too-small", bytes => Mutate(bytes, copy => BinaryPrimitives.WriteInt32LittleEndian(copy.AsSpan(ChunkSizeOffset, sizeof(int)), 1024))),
            ("fingerprint-length-zero", bytes => Mutate(bytes, copy => copy[FingerprintLengthOffset] = 0)),
            ("fingerprint-length-huge", bytes => Mutate(bytes, copy => copy[FingerprintLengthOffset] = byte.MaxValue)),
            ("metadata-length-zero", bytes => Mutate(bytes, copy => BinaryPrimitives.WriteInt32LittleEndian(copy.AsSpan(metadataCipherLengthOffset, sizeof(int)), 0))),
            ("metadata-length-huge", bytes => Mutate(bytes, copy => BinaryPrimitives.WriteInt32LittleEndian(copy.AsSpan(metadataCipherLengthOffset, sizeof(int)), int.MaxValue))),
            ("truncated-fixed-header", bytes => bytes[..20]),
            ("truncated-fingerprint", bytes => bytes[..checked(fingerprintOffset + Math.Max(0, fingerprintLength - 1))]),
            ("truncated-metadata", bytes => bytes[..checked(metadataCipherOffset + 5)])
        };

        foreach (var testCase in cases)
        {
            var malformed = testCase.Mutate(baseline);
            var malformedPath = temp.PathFor($"{testCase.Name}.r2kenc");
            var restoredPath = temp.PathFor($"{testCase.Name}.restored");
            await File.WriteAllBytesAsync(malformedPath, malformed);

            Exception? expectedFailure = null;
            try
            {
                await service.DecryptFileAsync(malformedPath, restoredPath, Password, key);
            }
            catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or CryptographicException)
            {
                expectedFailure = ex;
            }

            Assert.NotNull(expectedFailure);
            Assert.False(File.Exists(restoredPath));
            Assert.Empty(Directory.GetFiles(temp.DirectoryPath, "*.partial"));
        }

        Assert.Equal(originalText, await File.ReadAllTextAsync(source));
        Assert.Equal(sourceHash, SHA256.HashData(await File.ReadAllBytesAsync(source)));
        Assert.Equal(baseline, await File.ReadAllBytesAsync(fixture));
    }

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
