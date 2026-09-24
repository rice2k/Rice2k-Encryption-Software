using System.Buffers.Binary;
using System.Security.Cryptography;
using Rice2k.Encryption.Services;

namespace Rice2k.Tests;

public sealed class FileContainerParserMatrixTests
{
    private const string Password = "correct horse battery staple 2026";
    private const int VersionOffset = 8;
    private const int AlgorithmOffset = VersionOffset + 1;
    private const int KdfOffset = AlgorithmOffset + 1;
    private const int OperationsLimitOffset = KdfOffset + 1;
    private const int MemLimitOffset = OperationsLimitOffset + sizeof(long);
    private const int ChunkSizeOffset = MemLimitOffset + sizeof(int);
    private const int MetadataCipherLengthOffset = ChunkSizeOffset + sizeof(int) + 16 + 24;
    private const int MetadataCipherOffset = MetadataCipherLengthOffset + sizeof(int);

    [Fact]
    public async Task MalformedHeaderMatrix_FailsClosedWithoutProducingOutput()
    {
        using var temp = new TempDirectory();
        var service = new FileEncryptionService();
        var source = temp.PathFor("fixture.txt");
        var fixture = temp.PathFor("fixture.txt.r2kenc");
        const string originalText = "Rice2k malformed-header parser fixture";
        await File.WriteAllTextAsync(source, originalText);
        await service.EncryptFileAsync(source, fixture, Password, verifyAfterEncrypt: false);
        var baseline = await File.ReadAllBytesAsync(fixture);
        var sourceHash = SHA256.HashData(await File.ReadAllBytesAsync(source));

        var cases = new (string Name, Func<byte[], byte[]> Mutate)[]
        {
            ("bad-magic", bytes => Mutate(bytes, copy => copy[0] ^= 0x20)),
            ("unsupported-version", bytes => Mutate(bytes, copy => copy[VersionOffset] = 99)),
            ("unsupported-algorithm", bytes => Mutate(bytes, copy => copy[AlgorithmOffset] = 99)),
            ("unsupported-kdf", bytes => Mutate(bytes, copy => copy[KdfOffset] = 99)),
            ("argon-ops-too-low", bytes => Mutate(bytes, copy => BinaryPrimitives.WriteInt64LittleEndian(copy.AsSpan(OperationsLimitOffset, sizeof(long)), 2))),
            ("argon-memory-too-low", bytes => Mutate(bytes, copy => BinaryPrimitives.WriteInt32LittleEndian(copy.AsSpan(MemLimitOffset, sizeof(int)), 1024))),
            ("chunk-too-small", bytes => Mutate(bytes, copy => BinaryPrimitives.WriteInt32LittleEndian(copy.AsSpan(ChunkSizeOffset, sizeof(int)), 1024))),
            ("metadata-length-zero", bytes => Mutate(bytes, copy => BinaryPrimitives.WriteInt32LittleEndian(copy.AsSpan(MetadataCipherLengthOffset, sizeof(int)), 0))),
            ("metadata-length-huge", bytes => Mutate(bytes, copy => BinaryPrimitives.WriteInt32LittleEndian(copy.AsSpan(MetadataCipherLengthOffset, sizeof(int)), int.MaxValue))),
            ("truncated-fixed-header", bytes => bytes[..20]),
            ("truncated-metadata", bytes => bytes[..checked(MetadataCipherOffset + 5)])
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
                await service.DecryptFileAsync(malformedPath, restoredPath, Password);
            }
            catch (Exception ex) when (ex is InvalidDataException or NotSupportedException)
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
