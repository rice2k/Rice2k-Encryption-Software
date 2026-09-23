using Rice2k.Encryption.Services;

namespace Rice2k.Tests;

public sealed class PrivacyHistoryServiceTests
{
    [Fact]
    public void RecentFiles_AreOptInStorageFriendlyAndDeduplicated()
    {
        using var temp = new TempDirectory();
        var service = new PrivacyHistoryService(temp.DirectoryPath);
        var path = Path.Combine(temp.DirectoryPath, "example.txt");
        File.WriteAllText(path, "example");

        Assert.True(service.TryRememberRecentFile(path, "Encrypt source"));
        Assert.True(service.TryRememberRecentFile(path, "Decrypt source"));

        var items = service.LoadRecentFiles();
        var item = Assert.Single(items);
        Assert.Equal(Path.GetFullPath(path), item.Path);
        Assert.Equal("Decrypt source", item.Purpose);
    }

    [Fact]
    public void RedactedActivity_DropsDetailsSuchAsFileNames()
    {
        using var temp = new TempDirectory();
        var service = new PrivacyHistoryService(temp.DirectoryPath);

        Assert.True(service.TryAppendRedactedActivity(
            "9/23/2026 2:10:00 PM   Encryption complete   —   secret-finances.pdf"));

        var item = Assert.Single(service.LoadActivity());
        Assert.Equal("Encryption complete", item.Action);
        Assert.DoesNotContain("secret-finances.pdf", item.Action, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Encryption complete   —   C:\\Users\\Alex\\secret.txt", "Encryption complete")]
    [InlineData("Password generated   —   32 characters", "Password generated")]
    [InlineData("Text encrypted   —   100 characters", "Text encrypted")]
    public void Redactor_ReturnsOnlyAction(string display, string expected)
    {
        Assert.Equal(expected, PrivacyHistoryService.RedactActivityDisplayText(display));
    }

    [Fact]
    public void ClearAll_RemovesBothStores()
    {
        using var temp = new TempDirectory();
        var service = new PrivacyHistoryService(temp.DirectoryPath);
        var path = Path.Combine(temp.DirectoryPath, "example.txt");
        File.WriteAllText(path, "example");

        Assert.True(service.TryRememberRecentFile(path, "Encrypt source"));
        Assert.True(service.TryAppendRedactedActivity("Encryption complete   —   example.txt"));
        Assert.NotEmpty(service.LoadRecentFiles());
        Assert.NotEmpty(service.LoadActivity());

        Assert.True(service.TryClearAll());
        Assert.Empty(service.LoadRecentFiles());
        Assert.Empty(service.LoadActivity());
    }

    [Fact]
    public void OversizedHistoryFile_IsIgnored()
    {
        using var temp = new TempDirectory();
        File.WriteAllBytes(Path.Combine(temp.DirectoryPath, "activity-redacted.json"), new byte[(2 * 1024 * 1024) + 1]);
        var service = new PrivacyHistoryService(temp.DirectoryPath);

        Assert.Empty(service.LoadActivity());
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            DirectoryPath = Path.Combine(Path.GetTempPath(), "Rice2k.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DirectoryPath);
        }

        public string DirectoryPath { get; }

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
