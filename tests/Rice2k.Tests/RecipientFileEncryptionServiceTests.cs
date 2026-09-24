using System.Buffers.Binary;
using System.Security.Cryptography;
using Rice2k.Encryption.Services;

namespace Rice2k.Tests;

public sealed class RecipientFileEncryptionServiceTests
{
    [Fact]
    public async Task SingleRecipient_RoundTrip_RestoresOriginalFile()
    {
        using var temp = new TempDirectory();
        var identities = new IdentityService();
        var service = new RecipientFileEncryptionService();
        using var alice = identities.Generate("Alice");
        var source = temp.PathFor("source.bin");
        var encrypted = temp.PathFor("source.bin.r2kenc");
        var restored = temp.PathFor("restored.bin");
        var original = RandomNumberGenerator.GetBytes((4 * 1024 * 1024) + 777);
        await File.WriteAllBytesAsync(source, original);

        await service.EncryptForRecipientAsync(source, encrypted, alice.ToPublicIdentity());
        await service.DecryptAsync(encrypted, restored, alice);

        Assert.Equal(original, await File.ReadAllBytesAsync(restored));
        Assert.True(File.Exists(source));
        var info = await service.InspectAsync(encrypted);
        Assert.Equal(1, info.RecipientCount);
    }

    [Fact]
    public async Task MultiRecipient_EachRecipientCanDecryptSameContainer()
    {
        using var temp = new TempDirectory();
        var identities = new IdentityService();
        var service = new RecipientFileEncryptionService();
        using var alice = identities.Generate("Alice");
        using var bob = identities.Generate("Bob");
        var source = temp.PathFor("shared.txt");
        var encrypted = temp.PathFor("shared.txt.r2kenc");
        var aliceRestored = temp.PathFor("alice.txt");
        var bobRestored = temp.PathFor("bob.txt");
        await File.WriteAllTextAsync(source, "shared recipient data");

        await service.EncryptForRecipientsAsync(
            source,
            encrypted,
            [alice.ToPublicIdentity(), bob.ToPublicIdentity()]);

        var info = await service.InspectAsync(encrypted);
        Assert.Equal(2, info.RecipientCount);

        await service.DecryptAsync(encrypted, aliceRestored, alice);
        await service.DecryptAsync(encrypted, bobRestored, bob);

        Assert.Equal("shared recipient data", await File.ReadAllTextAsync(aliceRestored));
        Assert.Equal("shared recipient data", await File.ReadAllTextAsync(bobRestored));
    }

    [Fact]
    public async Task WrongRecipient_FailsClosedAndCreatesNoOutput()
    {
        using var temp = new TempDirectory();
        var identities = new IdentityService();
        var service = new RecipientFileEncryptionService();
        using var alice = identities.Generate("Alice");
        using var mallory = identities.Generate("Mallory");
        var source = temp.PathFor("private.txt");
        var encrypted = temp.PathFor("private.txt.r2kenc");
        var restored = temp.PathFor("should-not-exist.txt");
        await File.WriteAllTextAsync(source, "for Alice only");
        await service.EncryptForRecipientAsync(source, encrypted, alice.ToPublicIdentity());

        await Assert.ThrowsAsync<CryptographicException>(() =>
            service.DecryptAsync(encrypted, restored, mallory));

        Assert.False(File.Exists(restored));
        Assert.Empty(Directory.GetFiles(temp.DirectoryPath, "*.partial"));
    }

    [Fact]
    public async Task ModifiedCiphertext_FailsAuthentication()
    {
        using var temp = new TempDirectory();
        var identities = new IdentityService();
        var service = new RecipientFileEncryptionService();
        using var alice = identities.Generate("Alice");
        var source = temp.PathFor("tamper.bin");
        var encrypted = temp.PathFor("tamper.bin.r2kenc");
        var restored = temp.PathFor("tamper-restored.bin");
        await File.WriteAllBytesAsync(source, RandomNumberGenerator.GetBytes(8192));
        await service.EncryptForRecipientAsync(source, encrypted, alice.ToPublicIdentity(), verifyAfterEncrypt: false);

        var bytes = await File.ReadAllBytesAsync(encrypted);
        bytes[^1] ^= 0x40;
        await File.WriteAllBytesAsync(encrypted, bytes);

        await Assert.ThrowsAsync<CryptographicException>(() =>
            service.DecryptAsync(encrypted, restored, alice));
        Assert.False(File.Exists(restored));
    }

    [Fact]
    public async Task Inspect_ExcessiveMetadataCipherLength_IsRejectedBeforeRead()
    {
        using var temp = new TempDirectory();
        var identities = new IdentityService();
        var service = new RecipientFileEncryptionService();
        using var alice = identities.Generate("Alice");
        var source = temp.PathFor("metadata-length.txt");
        var encrypted = temp.PathFor("metadata-length.txt.r2kenc");
        await File.WriteAllTextAsync(source, "metadata length parser test");
        await service.EncryptForRecipientAsync(source, encrypted, alice.ToPublicIdentity(), verifyAfterEncrypt: false);

        var bytes = await File.ReadAllBytesAsync(encrypted);
        const int firstWrappedKeyLengthOffset = 8 + 1 + 1 + 1 + sizeof(int) + sizeof(int);
        var wrappedKeyLength = BinaryPrimitives.ReadInt32LittleEndian(
            bytes.AsSpan(firstWrappedKeyLengthOffset, sizeof(int)));
        var metadataCipherLengthOffset = checked(
            firstWrappedKeyLengthOffset + sizeof(int) + wrappedKeyLength + 24);
        BinaryPrimitives.WriteInt32LittleEndian(
            bytes.AsSpan(metadataCipherLengthOffset, sizeof(int)),
            int.MaxValue);
        await File.WriteAllBytesAsync(encrypted, bytes);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => service.InspectAsync(encrypted));

        Assert.Contains("metadata length", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DuplicateRecipient_IsRejected()
    {
        using var temp = new TempDirectory();
        var identities = new IdentityService();
        var service = new RecipientFileEncryptionService();
        using var alice = identities.Generate("Alice");
        var source = temp.PathFor("dup.txt");
        var encrypted = temp.PathFor("dup.txt.r2kenc");
        await File.WriteAllTextAsync(source, "data");
        var publicAlice = alice.ToPublicIdentity();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.EncryptForRecipientsAsync(source, encrypted, [publicAlice, publicAlice]));

        Assert.False(File.Exists(encrypted));
    }

    [Fact]
    public async Task RecipientFingerprint_MustMatchSuppliedPublicKeys()
    {
        using var temp = new TempDirectory();
        var identities = new IdentityService();
        var service = new RecipientFileEncryptionService();
        using var alice = identities.Generate("Alice");
        using var bob = identities.Generate("Bob");
        var source = temp.PathFor("fingerprint.txt");
        var encrypted = temp.PathFor("fingerprint.txt.r2kenc");
        await File.WriteAllTextAsync(source, "data");

        var malformed = alice.ToPublicIdentity() with { Fingerprint = bob.Fingerprint };

        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.EncryptForRecipientAsync(source, encrypted, malformed));

        Assert.Contains("fingerprint", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(encrypted));
        Assert.Empty(Directory.GetFiles(temp.DirectoryPath, "*.partial"));
    }

    [Fact]
    public async Task RecipientFingerprint_OverMaximumLength_IsRejectedBeforeOutput()
    {
        using var temp = new TempDirectory();
        var identities = new IdentityService();
        var service = new RecipientFileEncryptionService();
        using var alice = identities.Generate("Alice");
        var source = temp.PathFor("long-fingerprint.txt");
        var encrypted = temp.PathFor("long-fingerprint.txt.r2kenc");
        await File.WriteAllTextAsync(source, "data");

        var malformed = alice.ToPublicIdentity() with { Fingerprint = new string('A', 101) };

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.EncryptForRecipientAsync(source, encrypted, malformed));

        Assert.False(File.Exists(encrypted));
        Assert.Empty(Directory.GetFiles(temp.DirectoryPath, "*.partial"));
    }

    [Fact]
    public async Task DuplicateRecipientIdentifier_IsRejectedBeforeOutput()
    {
        using var temp = new TempDirectory();
        var identities = new IdentityService();
        var service = new RecipientFileEncryptionService();
        using var alice = identities.Generate("Alice");
        using var bob = identities.Generate("Bob");
        var source = temp.PathFor("duplicate-id.txt");
        var encrypted = temp.PathFor("duplicate-id.txt.r2kenc");
        await File.WriteAllTextAsync(source, "data");

        var publicAlice = alice.ToPublicIdentity();
        var duplicateIdBob = bob.ToPublicIdentity() with { Id = publicAlice.Id };

        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.EncryptForRecipientsAsync(source, encrypted, [publicAlice, duplicateIdBob]));

        Assert.Contains("identifier", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(encrypted));
        Assert.Empty(Directory.GetFiles(temp.DirectoryPath, "*.partial"));
    }

    [Fact]
    public async Task PreCancelledEncryption_LeavesNoFinalOrPartialOutput()
    {
        using var temp = new TempDirectory();
        var identities = new IdentityService();
        var service = new RecipientFileEncryptionService();
        using var alice = identities.Generate("Alice");
        var source = temp.PathFor("cancel.bin");
        var encrypted = temp.PathFor("cancel.bin.r2kenc");
        await File.WriteAllBytesAsync(source, RandomNumberGenerator.GetBytes(1024 * 1024));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.EncryptForRecipientAsync(
                source,
                encrypted,
                alice.ToPublicIdentity(),
                cancellationToken: cts.Token));

        Assert.True(File.Exists(source));
        Assert.False(File.Exists(encrypted));
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
        public string PathFor(string name) => Path.Combine(DirectoryPath, name);

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(DirectoryPath))
                    Directory.Delete(DirectoryPath, recursive: true);
            }
            catch
            {
                // Best effort test cleanup.
            }
        }
    }
}
