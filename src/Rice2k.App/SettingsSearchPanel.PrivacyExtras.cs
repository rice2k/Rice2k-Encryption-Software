using System.Windows;
using Rice2k.Encryption.Services;

namespace Rice2k.Encryption;

public partial class SettingsSearchPanel
{
    private bool _privacyExtrasHooked;

    protected override void OnInitialized(EventArgs e)
    {
        base.OnInitialized(e);

        if (_privacyExtrasHooked)
            return;

        _privacyExtrasHooked = true;
        Loaded += SettingsSearchPanel_PrivacyExtrasLoaded;
    }

    private void SettingsSearchPanel_PrivacyExtrasLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= SettingsSearchPanel_PrivacyExtrasLoaded;

        _loading = true;
        try
        {
            var settings = _settingsService.Load();
            RememberRecentFilesCheck.IsChecked = settings.RememberRecentFiles;
            PersistentActivityCheck.IsChecked = settings.PersistentActivityLogEnabled;
            ClearDiskHistoryCheck.IsChecked = settings.ClearDiskHistoryWhenPrivacyModeStarts;
            ReduceMotionCheck.IsChecked = settings.ReduceMotion;
        }
        finally
        {
            _loading = false;
        }

        RememberRecentFilesCheck.Checked += OptionalHistoryPreference_Changed;
        RememberRecentFilesCheck.Unchecked += OptionalHistoryPreference_Changed;
        PersistentActivityCheck.Checked += OptionalHistoryPreference_Changed;
        PersistentActivityCheck.Unchecked += OptionalHistoryPreference_Changed;
        ClearDiskHistoryCheck.Checked += OptionalHistoryPreference_Changed;
        ClearDiskHistoryCheck.Unchecked += OptionalHistoryPreference_Changed;
    }

    private void OptionalHistoryPreference_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;

        var current = _settingsService.Load();
        var updated = current with
        {
            RememberRecentFiles = RememberRecentFilesCheck.IsChecked == true,
            PersistentActivityLogEnabled = PersistentActivityCheck.IsChecked == true,
            ClearDiskHistoryWhenPrivacyModeStarts = ClearDiskHistoryCheck.IsChecked != false
        };

        var saved = _settingsService.TrySave(updated);
        _privacyChanged?.Invoke(updated);
        PrivacyStatusText.Text = saved
            ? BuildStoredHistoryStatus(updated)
            : "Stored-history preference could not be saved; Rice2k will continue using the last saved setting.";
    }

    private void ReduceMotion_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;

        var enabled = ReduceMotionCheck.IsChecked == true;
        var current = _settingsService.Load();
        var updated = current with { ReduceMotion = enabled };
        var saved = _settingsService.TrySave(updated);
        _privacyChanged?.Invoke(updated);
        PreferenceStatusText.Text = saved
            ? enabled
                ? "Reduced motion enabled. Rice2k will avoid nonessential indeterminate animation where supported."
                : "Reduced motion disabled. Rice2k may use standard progress animation."
            : "Reduced-motion preference could not be saved; Rice2k will continue using the last saved setting.";
    }

    private void ReviewStoredHistory_Click(object sender, RoutedEventArgs e)
    {
        var history = new PrivacyHistoryWindow
        {
            Owner = _owner
        };
        history.ShowDialog();
    }

    private static string BuildStoredHistoryStatus(Rice2kAppSettings settings)
    {
        var recent = settings.RememberRecentFiles ? "recent-file paths ON" : "recent-file paths OFF";
        var activity = settings.PersistentActivityLogEnabled ? "redacted activity ON" : "redacted activity OFF";
        var privacyClear = settings.ClearDiskHistoryWhenPrivacyModeStarts
            ? "Privacy Mode clears stored history"
            : "Privacy Mode leaves stored history unchanged";
        return $"Optional history: {recent}; {activity}; {privacyClear}.";
    }
}
