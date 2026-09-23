using System.Text.Json;

namespace Rice2k.Encryption.Services;

public sealed record Rice2kAppSettings(
    bool FirstRunTourCompleted = false,
    bool ShowHelpfulHints = true,
    DateTimeOffset? LastRecoveryTestUtc = null,
    string? LastRecoveryFingerprint = null,
    string? LastRecoveryKeyName = null);

public sealed class AppSettingsService
{
    private readonly string _settingsDirectory;
    private readonly string _settingsPath;

    public AppSettingsService()
    {
        _settingsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Rice2k Encryption Software");
        _settingsPath = Path.Combine(_settingsDirectory, "settings.json");
    }

    public Rice2kAppSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
                return new Rice2kAppSettings();

            var json = File.ReadAllText(_settingsPath);
            return JsonSerializer.Deserialize<Rice2kAppSettings>(json)
                ?? new Rice2kAppSettings();
        }
        catch
        {
            // Preferences must never stop the encryption application from opening.
            return new Rice2kAppSettings();
        }
    }

    public bool TrySave(Rice2kAppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        string? tempPath = null;

        try
        {
            Directory.CreateDirectory(_settingsDirectory);
            tempPath = Path.Combine(
                _settingsDirectory,
                $"settings.{Guid.NewGuid():N}.tmp");

            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(tempPath, json);
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
}
