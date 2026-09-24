using System.Text.Json;

namespace Rice2k.Encryption.Services;

public sealed record Rice2kAppSettings(
    bool FirstRunTourCompleted = false,
    bool ShowHelpfulHints = true,
    DateTimeOffset? LastRecoveryTestUtc = null,
    string? LastRecoveryFingerprint = null,
    string? LastRecoveryKeyName = null,
    bool? VaultAutoLockEnabled = null,
    int? VaultAutoLockMinutes = null,
    bool PrivacyModeEnabled = false,
    int ClipboardAutoClearSeconds = 30,
    bool HideActivityInPrivacyMode = true,
    bool ClearSensitivePreviewsWhenPrivacyModeStarts = true,
    bool AppLockEnabled = false,
    bool LockOnMinimize = false,
    bool LockOnWindowsSessionLock = false,
    int AppLockInactivityMinutes = 0,
    bool RememberRecentFiles = false,
    bool PersistentActivityLogEnabled = false,
    bool ClearDiskHistoryWhenPrivacyModeStarts = true,
    bool ReduceMotion = false,
    bool DesktopNotificationsEnabled = true);

public sealed class AppSettingsService
{
    private const int MaxSettingsFileBytes = 256 * 1024;

    private readonly string _settingsDirectory;
    private readonly string _settingsPath;

    public AppSettingsService(string? settingsDirectory = null)
    {
        _settingsDirectory = settingsDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Rice2k Encryption Software");
        _settingsPath = Path.Combine(_settingsDirectory, "settings.json");
    }

    public Rice2kAppSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
                return Normalize(new Rice2kAppSettings());

            var bytes = BoundedFileReader.ReadAllBytes(
                _settingsPath,
                MaxSettingsFileBytes,
                "Rice2k settings file");
            return Normalize(JsonSerializer.Deserialize<Rice2kAppSettings>(bytes)
                ?? new Rice2kAppSettings());
        }
        catch
        {
            // Preferences must never stop the encryption application from opening.
            return Normalize(new Rice2kAppSettings());
        }
    }

    public bool TrySave(Rice2kAppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings = Normalize(settings);
        string? tempPath = null;

        try
        {
            Directory.CreateDirectory(_settingsDirectory);
            tempPath = Path.Combine(
                _settingsDirectory,
                $"settings.{Guid.NewGuid():N}.tmp");

            using (var stream = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None))
            {
                JsonSerializer.Serialize(
                    stream,
                    settings,
                    new JsonSerializerOptions { WriteIndented = true });
                stream.Flush(flushToDisk: true);
            }

            File.Move(tempPath, _settingsPath, overwrite: true);
            return true;
        }
        catch
        {
            // Preferences must never stop the encryption application from operating.
            return false;
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(tempPath))
            {
                try
                {
                    if (File.Exists(tempPath))
                        File.Delete(tempPath);
                }
                catch
                {
                    // Best effort cleanup only.
                }
            }
        }
    }

    private static Rice2kAppSettings Normalize(Rice2kAppSettings settings)
    {
        var clipboardSeconds = settings.ClipboardAutoClearSeconds is 0 or 15 or 30 or 60 or 120
            ? settings.ClipboardAutoClearSeconds
            : 30;
        var appLockMinutes = settings.AppLockInactivityMinutes is 0 or 1 or 5 or 10 or 15 or 30
            ? settings.AppLockInactivityMinutes
            : 0;

        return settings with
        {
            ClipboardAutoClearSeconds = clipboardSeconds,
            AppLockInactivityMinutes = appLockMinutes
        };
    }
}
