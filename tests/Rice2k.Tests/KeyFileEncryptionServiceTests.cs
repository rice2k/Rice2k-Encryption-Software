using System.Buffers.Binary;
using System.Security.Cryptography;
using Rice2k.Encryption.Services;

namespace Rice2k.Tests;

public sealed class KeyFileEncryptionServiceTests
{
    private const string Password = "correct horse battery staple 2026";
    private const int FingerprintLengthOffset = 8 + 1 + 1 + 1 + 1 + sizeof(long) + sizeof(int) + sizeof(int) + 16;
    private readonly KeyFileEncryptionService _service = new();
    private readonly KeyManagerService _keys = new();

    [Fact]
    public async Task PasswordPlusKeyFile_RoundTrip_RestoresOriginal()
    {
        using var temp = new TempDirectory();
        using var key = _keys.Generate("Archive Key");
        var source = temp.PathFor("archive.bin");
        var encrypted = temp.PathFor("archive.bin.r2kenc");
        var restored = temp.PathFor("archive.restored.bin");
        var original = RandomNumberGenerator.GetBytes(256 * 1024 + 19);
        await File.WriteAllBytesAsync(source, original);

        await _service.EncryptFileAsync(source, encrypted, Password, key);
        await _service.DecryptFileAsync(encrypted, restored, Password, key);

        Assert.Equal(original, await File.ReadAllBytesAsync(restored));
        Assert.True(KeyFileEncryptionService.IsKeyFileProtectedContainer(encrypted));
        Assert.Equal(key.Fingerprint, KeyFileEncryptionService.TryGetRequiredKeyFingerprint(encrypted));
    }

    [Fact]
    public async Task Decrypt_WrongKey_FailsClosed()
    {
        using var temp = new TempDirectory();
        using var correctKey = _keys.Generate("Correct Key");
        using var wrongKey = _keys.Generate("Wrong Key");
        var source = temp.PathFor("secret.txt");
        var encrypted = temp.PathFor("secret.txt.r2kenc");
        var restored = temp.PathFor("should-not-exist.txt");
        await File.WriteAllTextAsync(source, "password plus key file test");
        await _service.EncryptFileAsync(source, encrypted, Password, correctKey, verifyAfterEncrypt: false);

        var error = await Assert.ThrowsAsync<CryptographicException>(() =>
            _service.DecryptFileAsync(encrypted, restored, Password, wrongKey));

        Assert.Contains(correctKey.Fingerprint, error.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(restored));
        Assert.Empty(Directory.GetFiles(temp.DirectoryPath, "*.partial"));
    }

    [Fact]
    public async Task Decrypt_WrongPasswordWithCorrectKey_FailsClosed()
    {
        using var temp = new TempDirectory();
        using var key = _keys.Generate("Correct Key");
        var source = temp.PathFor("secret.txt");
        var encrypted = temp.PathFor("secret.txt.r2kenc");
        var restored = temp.PathFor("should-not-exist.txt");
        await File.WriteAllTextAsync(source, "authenticated contents");
        await _service.EncryptFileAsync(source, encrypted, Password, key, verifyAfterEncrypt: false);

        await Assert.ThrowsAsync<CryptographicException>(() =>
            _service.DecryptFileAsync(encrypted, restored, "wrong password but long enough", key));

        Assert.False(File.Exists(restored));
    }

    [Fact]
    public async Task Decrypt_ModifiedCiphertext_FailsAuthentication()
    {
        using var temp = new TempDirectory();
        using var key = _keys.Generate("Tamper Test Key");
        var source = temp.PathFor("tamper.bin");
        var encrypted = temp.PathFor("tamper.bin.r2kenc");
        var restored = temp.PathFor("tamper.restored.bin");
        await File.WriteAllBytesAsync(source, RandomNumberGenerator.GetBytes(8192));
        await _service.EncryptFileAsync(source, encrypted, Password, key, verifyAfterEncrypt: false);

        var bytes = await File.ReadAllBytesAsync(encrypted);
        bytes[^1] ^= 0x20;
        await File.WriteAllBytesAsync(encrypted, bytes);

        await Assert.ThrowsAsync<CryptographicException>(() =>
            _service.DecryptFileAsync(encrypted, restored, Password, key));

        Assert.False(File.Exists(restored));
    }

    [Fact]
    public async Task Decrypt_ExcessiveMetadataLength_IsRejectedBeforeKeyDerivation()
    {
        using var temp = new TempDirectory();
        using var key = _keys.Generate("Metadata Limit Key");
        var source = temp.PathFor("metadata-limit.txt");
        var encrypted = temp.PathFor("metadata-limit.txt.r2kenc");
        var restored = temp.PathFor("metadata-limit.restored.txt");
        await File.WriteAllTextAsync(source, "metadata limit test");
        await _service.EncryptFileAsync(source, encrypted, Password, key, verifyAfterEncrypt: false);

        var bytes = await File.ReadAllBytesAsync(encrypted);
        var fingerprintLength = bytes[FingerprintLengthOffset];
        var metadataCipherLengthOffset = checked(FingerprintLengthOffset + 1 + fingerprintLength + 24);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(metadataCipherLengthOffset, sizeof(int)), int.MaxValue);
        await File.WriteAllBytesAsync(encrypted, bytes);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            _service.DecryptFileAsync(encrypted, restored, Password, key));

        Assert.Contains("metadata length", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(restored));
        Assert.Empty(Directory.GetFiles(temp.DirectoryPath, "*.partial"));
    }

    [Fact]
    public async Task PasswordOnlyContainer_IsNotMisidentifiedAsKeyFileProtected()
    {
        using var temp = new TempDirectory();
        var passwordOnly = new FileEncryptionService();
        var source = temp.PathFor("legacy.txt");
        var encrypted = temp.PathFor("legacy.txt.r2kenc");
        await File.WriteAllTextAsync(source, "legacy password-only container");
        await passwordOnly.EncryptFileAsync(source, encrypted, Password, verifyAfterEncrypt: false);

        Assert.False(KeyFileEncryptionService.IsKeyFileProtectedContainer(encrypted));
        Assert.Null(KeyFileEncryptionService.TryGetRequiredKeyFingerprint(encrypted));
    }

    [Fact]
    public async Task Encrypt_PreCancelledOperation_LeavesNoFinalOrPartialOutput()
    {
        using var temp = new TempDirectory();
        using var key = _keys.Generate("Cancellation Key");
        var source = temp.PathFor("cancel.bin");
        var destination = temp.PathFor("cancel.bin.r2kenc");
        await File.WriteAllBytesAsync(source, RandomNumberGenerator.GetBytes(1024 * 1024));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _service.EncryptFileAsync(source, destination, Password, key, cancellationToken: cts.Token));

        Assert.True(File.Exists(source));
        Assert.False(File.Exists(destination));
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
