using System.Buffers.Binary;
using System.Security.Cryptography;
using Rice2k.Encryption.Services;

namespace Rice2k.Tests;

public sealed class RecipientContainerParserMatrixTests
{
    private const int VersionOffset = 8;
    private const int AlgorithmOffset = VersionOffset + 1;
    private const int KeyWrapOffset = AlgorithmOffset + 1;
    private const int ChunkSizeOffset = KeyWrapOffset + 1;
    private const int RecipientCountOffset = ChunkSizeOffset + sizeof(int);
    private const int FirstWrappedLengthOffset = RecipientCountOffset + sizeof(int);

    [Fact]
    public async Task MalformedHeaderMatrix_IsRejectedByInspection()
    {
        using var temp = new TempDirectory();
        var identities = new IdentityService();
        var service = new RecipientFileEncryptionService();
        using var alice = identities.Generate("Alice Parser Matrix");
        var source = temp.PathFor("fixture.txt");
        var fixture = temp.PathFor("fixture.txt.r2kenc");
        const string originalText = "Rice2k R2KENC03 parser fixture";
        await File.WriteAllTextAsync(source, originalText);
        await service.EncryptForRecipientAsync(
            source,
            fixture,
            alice.ToPublicIdentity(),
            verifyAfterEncrypt: false);

        var baseline = await File.ReadAllBytesAsync(fixture);
        var sourceHash = SHA256.HashData(await File.ReadAllBytesAsync(source));
        var wrappedLength = BinaryPrimitives.ReadInt32LittleEndian(
            baseline.AsSpan(FirstWrappedLengthOffset, sizeof(int)));
        Assert.True(wrappedLength > 0);
        var firstWrappedOffset = FirstWrappedLengthOffset + sizeof(int);
        var metadataNonceOffset = checked(firstWrappedOffset + wrappedLength);
        var metadataCipherLengthOffset = checked(metadataNonceOffset + 24);
        var metadataCipherOffset = checked(metadataCipherLengthOffset + sizeof(int));

        var cases = new (string Name, Func<byte[], byte[]> Mutate)[]
        {
            ("bad-magic", bytes => Mutate(bytes, copy => copy[0] ^= 0x20)),
            ("unsupported-version", bytes => Mutate(bytes, copy => copy[VersionOffset] = 99)),
            ("unsupported-algorithm", bytes => Mutate(bytes, copy => copy[AlgorithmOffset] = 99)),
            ("unsupported-key-wrap", bytes => Mutate(bytes, copy => copy[KeyWrapOffset] = 99)),
            ("chunk-too-small", bytes => Mutate(bytes, copy => BinaryPrimitives.WriteInt32LittleEndian(copy.AsSpan(ChunkSizeOffset, sizeof(int)), 1024))),
            ("recipient-count-zero", bytes => Mutate(bytes, copy => BinaryPrimitives.WriteInt32LittleEndian(copy.AsSpan(RecipientCountOffset, sizeof(int)), 0))),
            ("recipient-count-huge", bytes => Mutate(bytes, copy => BinaryPrimitives.WriteInt32LittleEndian(copy.AsSpan(RecipientCountOffset, sizeof(int)), 65))),
            ("wrapped-key-too-small", bytes => Mutate(bytes, copy => BinaryPrimitives.WriteInt32LittleEndian(copy.AsSpan(FirstWrappedLengthOffset, sizeof(int)), 79))),
            ("wrapped-key-too-large", bytes => Mutate(bytes, copy => BinaryPrimitives.WriteInt32LittleEndian(copy.AsSpan(FirstWrappedLengthOffset, sizeof(int)), 257))),
            ("metadata-length-zero", bytes => Mutate(bytes, copy => BinaryPrimitives.WriteInt32LittleEndian(copy.AsSpan(metadataCipherLengthOffset, sizeof(int)), 0))),
            ("metadata-length-huge", bytes => Mutate(bytes, copy => BinaryPrimitives.WriteInt32LittleEndian(copy.AsSpan(metadataCipherLengthOffset, sizeof(int)), int.MaxValue))),
            ("truncated-fixed-header", bytes => bytes[..10]),
            ("truncated-wrapped-key", bytes => bytes[..checked(firstWrappedOffset + wrappedLength - 1)]),
            ("truncated-metadata", bytes => bytes[..checked(metadataCipherOffset + 5)])
        };

        foreach (var testCase in cases)
        {
            var malformed = testCase.Mutate(baseline);
            var malformedPath = temp.PathFor($"{testCase.Name}.r2kenc");
            await File.WriteAllBytesAsync(malformedPath, malformed);

            Exception? expectedFailure = null;
            try
            {
                _ = await service.InspectAsync(malformedPath);
            }
            catch (Exception ex) when (ex is InvalidDataException or NotSupportedException)
            {
                expectedFailure = ex;
            }

            Assert.NotNull(expectedFailure);
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
