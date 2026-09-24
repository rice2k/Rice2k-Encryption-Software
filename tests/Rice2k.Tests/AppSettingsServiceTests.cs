using Rice2k.Encryption.Services;

namespace Rice2k.Tests;

public sealed class AppSettingsServiceTests
{
    [Fact]
    public void TrySaveAndLoad_RoundTripsAndCleansTemporaryFile()
    {
        using var temp = new TempDirectory();
        var service = new AppSettingsService(temp.DirectoryPath);
        var settings = new Rice2kAppSettings(
            FirstRunTourCompleted: true,
            ShowHelpfulHints: false,
            PrivacyModeEnabled: true,
            ClipboardAutoClearSeconds: 60,
            AppLockEnabled: true,
            LockOnMinimize: true,
            AppLockInactivityMinutes: 5,
            RememberRecentFiles: true,
            PersistentActivityLogEnabled: true,
            ReduceMotion: true,
            DesktopNotificationsEnabled: false);

        Assert.True(service.TrySave(settings));

        var loaded = service.Load();
        Assert.True(loaded.FirstRunTourCompleted);
        Assert.False(loaded.ShowHelpfulHints);
        Assert.True(loaded.PrivacyModeEnabled);
        Assert.Equal(60, loaded.ClipboardAutoClearSeconds);
        Assert.True(loaded.AppLockEnabled);
        Assert.True(loaded.LockOnMinimize);
        Assert.Equal(5, loaded.AppLockInactivityMinutes);
        Assert.True(loaded.RememberRecentFiles);
        Assert.True(loaded.PersistentActivityLogEnabled);
        Assert.True(loaded.ReduceMotion);
        Assert.False(loaded.DesktopNotificationsEnabled);

        Assert.True(File.Exists(temp.PathFor("settings.json")));
        Assert.Empty(Directory.GetFiles(temp.DirectoryPath, "settings.*.tmp"));
    }

    [Fact]
    public void SavedSettings_AreReloadedByFreshServiceAndIgnoreStaleTempFile()
    {
        using var temp = new TempDirectory();
        var first = new AppSettingsService(temp.DirectoryPath);
        Assert.True(first.TrySave(new Rice2kAppSettings(
            FirstRunTourCompleted: true,
            ShowHelpfulHints: false,
            PrivacyModeEnabled: true,
            ClipboardAutoClearSeconds: 120,
            AppLockInactivityMinutes: 15)));

        File.WriteAllText(temp.PathFor("settings.interrupted.tmp"), "{ incomplete replacement");

        var restarted = new AppSettingsService(temp.DirectoryPath);
        var loaded = restarted.Load();

        Assert.True(loaded.FirstRunTourCompleted);
        Assert.False(loaded.ShowHelpfulHints);
        Assert.True(loaded.PrivacyModeEnabled);
        Assert.Equal(120, loaded.ClipboardAutoClearSeconds);
        Assert.Equal(15, loaded.AppLockInactivityMinutes);
    }

    [Fact]
    public void Load_MalformedJson_ReturnsSafeDefaults()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.PathFor("settings.json"), "{ malformed json");
        var service = new AppSettingsService(temp.DirectoryPath);

        var loaded = service.Load();

        Assert.False(loaded.FirstRunTourCompleted);
        Assert.True(loaded.ShowHelpfulHints);
        Assert.False(loaded.PrivacyModeEnabled);
        Assert.Equal(30, loaded.ClipboardAutoClearSeconds);
        Assert.False(loaded.AppLockEnabled);
        Assert.Equal(0, loaded.AppLockInactivityMinutes);
        Assert.False(loaded.RememberRecentFiles);
        Assert.False(loaded.PersistentActivityLogEnabled);
        Assert.True(loaded.ClearDiskHistoryWhenPrivacyModeStarts);
        Assert.True(loaded.DesktopNotificationsEnabled);
    }

    [Fact]
    public void Load_OversizedSettingsFile_ReturnsSafeDefaultsBeforeParsing()
    {
        using var temp = new TempDirectory();
        File.WriteAllBytes(temp.PathFor("settings.json"), new byte[(256 * 1024) + 1]);
        var service = new AppSettingsService(temp.DirectoryPath);

        var loaded = service.Load();

        Assert.False(loaded.PrivacyModeEnabled);
        Assert.False(loaded.AppLockEnabled);
        Assert.Equal(30, loaded.ClipboardAutoClearSeconds);
        Assert.Equal(0, loaded.AppLockInactivityMinutes);
    }

    [Fact]
    public void TrySave_NormalizesUnsupportedTimingValues()
    {
        using var temp = new TempDirectory();
        var service = new AppSettingsService(temp.DirectoryPath);

        Assert.True(service.TrySave(new Rice2kAppSettings(
            ClipboardAutoClearSeconds: 17,
            AppLockInactivityMinutes: 7)));

        var loaded = service.Load();
        Assert.Equal(30, loaded.ClipboardAutoClearSeconds);
        Assert.Equal(0, loaded.AppLockInactivityMinutes);
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
