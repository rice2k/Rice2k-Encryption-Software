using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Rice2k.Encryption.Services;

namespace Rice2k.Tests;

public sealed class AppLockCredentialServiceTests
{
    [Fact]
    public void SetVerifyAndRemove_RoundTripsInIsolatedPath()
    {
        using var temp = new TempDirectory();
        var path = temp.PathFor("app-lock.json");
        var service = new AppLockCredentialService(path);
        const string password = "correct horse battery staple";

        service.SetPassword(password);

        Assert.True(service.IsConfigured());
        Assert.True(service.Verify(password));
        Assert.False(service.Verify("wrong password that is long enough"));
        Assert.True(File.Exists(path));

        service.Remove(password);
        Assert.False(File.Exists(path));
        Assert.False(service.IsConfigured());
    }

    [Fact]
    public void SetPassword_ReplacesExistingCredentialAndCleansTemporaryFile()
    {
        using var temp = new TempDirectory();
        var path = temp.PathFor("app-lock.json");
        var service = new AppLockCredentialService(path);
        const string firstPassword = "first sufficiently long app lock password";
        const string secondPassword = "second sufficiently long app lock password";

        service.SetPassword(firstPassword);
        service.SetPassword(secondPassword);

        Assert.True(service.IsConfigured());
        Assert.True(service.Verify(secondPassword));
        Assert.False(service.Verify(firstPassword));
        Assert.Single(Directory.GetFiles(temp.DirectoryPath, "app-lock.json"));
        Assert.Empty(Directory.GetFiles(temp.DirectoryPath, "app-lock.json.*.tmp"));
    }

    [Fact]
    public void SetPassword_RejectsShortPassword()
    {
        using var temp = new TempDirectory();
        var service = new AppLockCredentialService(temp.PathFor("app-lock.json"));

        Assert.Throws<ArgumentException>(() => service.SetPassword("too-short"));
        Assert.False(service.IsConfigured());
    }

    [Fact]
    public void Remove_WrongPassword_DoesNotDeleteCredential()
    {
        using var temp = new TempDirectory();
        var path = temp.PathFor("app-lock.json");
        var service = new AppLockCredentialService(path);
        service.SetPassword("this is the correct lock password");

        Assert.Throws<CryptographicException>(() =>
            service.Remove("this is definitely the wrong password"));

        Assert.True(File.Exists(path));
        Assert.True(service.Verify("this is the correct lock password"));
    }

    [Fact]
    public void Verify_ModifiedVerifier_FailsAuthentication()
    {
        using var temp = new TempDirectory();
        var path = temp.PathFor("app-lock.json");
        var service = new AppLockCredentialService(path);
        const string password = "another strong app lock password";
        service.SetPassword(password);

        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        root["VerifierBase64"] = Convert.ToBase64String(new byte[32]);
        File.WriteAllText(path, root.ToJsonString());

        Assert.True(service.IsConfigured());
        Assert.False(service.Verify(password));
    }

    [Fact]
    public void HostileArgonOperations_AreRejectedBeforeKdf()
    {
        using var temp = new TempDirectory();
        var path = temp.PathFor("app-lock.json");
        var service = new AppLockCredentialService(path);
        service.SetPassword("parameter validation password");

        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        root["OpsLimit"] = 999_999;
        File.WriteAllText(path, root.ToJsonString());

        Assert.False(service.IsConfigured());
        Assert.Throws<InvalidDataException>(() => service.Verify("parameter validation password"));
    }

    [Fact]
    public void HostileArgonMemory_AreRejectedBeforeKdf()
    {
        using var temp = new TempDirectory();
        var path = temp.PathFor("app-lock.json");
        var service = new AppLockCredentialService(path);
        service.SetPassword("memory parameter validation password");

        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        root["MemLimit"] = int.MaxValue;
        File.WriteAllText(path, root.ToJsonString());

        Assert.False(service.IsConfigured());
        Assert.Throws<InvalidDataException>(() => service.Verify("memory parameter validation password"));
    }

    [Fact]
    public void UnsupportedVersion_IsRejected()
    {
        using var temp = new TempDirectory();
        var path = temp.PathFor("app-lock.json");
        var service = new AppLockCredentialService(path);
        service.SetPassword("unsupported version password");

        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        root["Version"] = 999;
        File.WriteAllText(path, root.ToJsonString());

        Assert.False(service.IsConfigured());
        Assert.Throws<NotSupportedException>(() => service.Verify("unsupported version password"));
    }

    [Fact]
    public void MalformedJson_IsRejected()
    {
        using var temp = new TempDirectory();
        var path = temp.PathFor("app-lock.json");
        File.WriteAllText(path, "{ not valid json");
        var service = new AppLockCredentialService(path);

        Assert.False(service.IsConfigured());
        Assert.Throws<InvalidDataException>(() => service.Verify("any sufficiently long password"));
    }

    [Fact]
    public void OversizedCredentialFile_IsRejectedBeforeParsing()
    {
        using var temp = new TempDirectory();
        var path = temp.PathFor("app-lock.json");
        File.WriteAllBytes(path, new byte[(32 * 1024) + 1]);
        var service = new AppLockCredentialService(path);

        Assert.False(service.IsConfigured());
        Assert.Throws<InvalidDataException>(() => service.Verify("any sufficiently long password"));
    }

    [Fact]
    public void InvalidSaltLength_IsRejected()
    {
        using var temp = new TempDirectory();
        var path = temp.PathFor("app-lock.json");
        var service = new AppLockCredentialService(path);
        service.SetPassword("salt length validation password");

        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        root["SaltBase64"] = Convert.ToBase64String(new byte[8]);
        File.WriteAllText(path, root.ToJsonString());

        Assert.False(service.IsConfigured());
        Assert.Throws<InvalidDataException>(() => service.Verify("salt length validation password"));
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
                // Best-effort test cleanup.
            }
        }
    }
}
