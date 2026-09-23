using System.Security.Cryptography;
using Rice2k.Encryption.Services;

namespace Rice2k.Tests;

public sealed class FileSignatureServiceTests
{
    [Fact]
    public async Task SignVerify_RoundTrip_ReportsValidContentAndSignerFingerprint()
    {
        using var temp = new TempDirectory();
        var identities = new IdentityService();
        var signatures = new FileSignatureService();
        using var identity = identities.Generate("Alice");
        var file = temp.PathFor("archive.zip");
        var signature = temp.PathFor("archive.zip.r2ksig");
        await File.WriteAllBytesAsync(file, RandomNumberGenerator.GetBytes(192 * 1024 + 11));

        await signatures.SignAsync(file, signature, identity);
        var result = await signatures.VerifyAsync(file, signature, identity.ToPublicIdentity());

        Assert.True(result.CryptographicSignatureValid);
        Assert.True(result.FileContentMatches);
        Assert.True(result.MatchesExpectedIdentity);
        Assert.True(result.IsValid);
        Assert.Equal(identity.Fingerprint, result.SignerFingerprint);
        Assert.Equal(identity.Id, result.SignerIdentityId);
        Assert.Equal("archive.zip", result.OriginalFileName);
        Assert.InRange(new FileInfo(signature).Length, 1, 128 * 1024);
        Assert.Empty(Directory.GetFiles(temp.DirectoryPath, "*.partial"));
    }

    [Fact]
    public async Task Verify_ModifiedFile_ReportsContentMismatch()
    {
        using var temp = new TempDirectory();
        var identities = new IdentityService();
        var signatures = new FileSignatureService();
        using var identity = identities.Generate("Alice");
        var file = temp.PathFor("document.bin");
        var signature = temp.PathFor("document.bin.r2ksig");
        await File.WriteAllTextAsync(file, "original data");
        await signatures.SignAsync(file, signature, identity);

        await File.AppendAllTextAsync(file, " changed");
        var result = await signatures.VerifyAsync(file, signature);

        Assert.True(result.CryptographicSignatureValid);
        Assert.False(result.FileContentMatches);
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task Verify_RenamedButUnchangedFile_PreservesContentValidityAndReportsNameDifference()
    {
        using var temp = new TempDirectory();
        var identities = new IdentityService();
        var signatures = new FileSignatureService();
        using var identity = identities.Generate("Alice");
        var original = temp.PathFor("original.txt");
        var renamed = temp.PathFor("renamed.txt");
        var signature = temp.PathFor("original.txt.r2ksig");
        await File.WriteAllTextAsync(original, "same bytes after rename");
        await signatures.SignAsync(original, signature, identity);
        File.Move(original, renamed);

        var result = await signatures.VerifyAsync(renamed, signature);

        Assert.True(result.CryptographicSignatureValid);
        Assert.True(result.FileContentMatches);
        Assert.True(result.IsValid);
        Assert.False(result.CurrentNameMatchesOriginal);
        Assert.Equal("original.txt", result.OriginalFileName);
        Assert.Equal("renamed.txt", result.CurrentFileName);
    }

    [Fact]
    public async Task Verify_DifferentExpectedIdentity_FailsTrustMatch()
    {
        using var temp = new TempDirectory();
        var identities = new IdentityService();
        var signatures = new FileSignatureService();
        using var alice = identities.Generate("Alice");
        using var bob = identities.Generate("Bob");
        var file = temp.PathFor("signed.dat");
        var signature = temp.PathFor("signed.dat.r2ksig");
        await File.WriteAllTextAsync(file, "signed by Alice");
        await signatures.SignAsync(file, signature, alice);

        var result = await signatures.VerifyAsync(file, signature, bob.ToPublicIdentity());

        Assert.True(result.CryptographicSignatureValid);
        Assert.True(result.FileContentMatches);
        Assert.False(result.MatchesExpectedIdentity);
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task Verify_TamperedSignatureDocument_FailsClosed()
    {
        using var temp = new TempDirectory();
        var identities = new IdentityService();
        var signatures = new FileSignatureService();
        using var identity = identities.Generate("Alice");
        var file = temp.PathFor("signed.dat");
        var signature = temp.PathFor("signed.dat.r2ksig");
        await File.WriteAllTextAsync(file, "signed contents");
        await signatures.SignAsync(file, signature, identity);

        var json = await File.ReadAllTextAsync(signature);
        json = json.Replace("\"Alice\"", "\"Mallory\"", StringComparison.Ordinal);
        await File.WriteAllTextAsync(signature, json);

        await Assert.ThrowsAsync<CryptographicException>(() =>
            signatures.VerifyAsync(file, signature));
    }

    [Fact]
    public async Task Sign_ExistingSignatureDestination_IsNeverOverwritten()
    {
        using var temp = new TempDirectory();
        var identities = new IdentityService();
        var signatures = new FileSignatureService();
        using var identity = identities.Generate("Alice");
        var file = temp.PathFor("file.txt");
        var signature = temp.PathFor("file.txt.r2ksig");
        await File.WriteAllTextAsync(file, "data");
        await File.WriteAllTextAsync(signature, "keep me");
        var before = await File.ReadAllBytesAsync(signature);

        await Assert.ThrowsAsync<IOException>(() =>
            signatures.SignAsync(file, signature, identity));

        Assert.Equal(before, await File.ReadAllBytesAsync(signature));
    }

    [Fact]
    public async Task Sign_PreCancelled_LeavesNoFinalOrPartialOutput()
    {
        using var temp = new TempDirectory();
        var identities = new IdentityService();
        var signatures = new FileSignatureService();
        using var identity = identities.Generate("Alice");
        var file = temp.PathFor("cancel.txt");
        var signature = temp.PathFor("cancel.txt.r2ksig");
        await File.WriteAllTextAsync(file, "cancel before signing");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            signatures.SignAsync(file, signature, identity, cts.Token));

        Assert.True(File.Exists(file));
        Assert.False(File.Exists(signature));
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
