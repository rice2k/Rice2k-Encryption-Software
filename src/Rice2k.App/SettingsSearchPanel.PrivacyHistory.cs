using System.Windows;
using Rice2k.Encryption.Services;

namespace Rice2k.Encryption;

public partial class SettingsSearchPanel
{
    private bool _extendedPrivacyLoaded;

    protected override void OnInitialized(EventArgs e)
    {
        base.OnInitialized(e);
        Loaded += SettingsSearchPanel_ExtendedLoaded;
    }

    private void SettingsSearchPanel_ExtendedLoaded(object sender, RoutedEventArgs e)
    {
        if (_extendedPrivacyLoaded)
            return;

        _extendedPrivacyLoaded = true;
        RememberRecentFilesCheck.Checked += ExtendedPrivacyPreference_Changed;
        RememberRecentFilesCheck.Unchecked += ExtendedPrivacyPreference_Changed;
        PersistentActivityCheck.Checked += ExtendedPrivacyPreference_Changed;
        PersistentActivityCheck.Unchecked += ExtendedPrivacyPreference_Changed;
        ClearDiskHistoryCheck.Checked += ExtendedPrivacyPreference_Changed;
        ClearDiskHistoryCheck.Unchecked += ExtendedPrivacyPreference_Changed;

        RefreshExtendedPrivacySettings(_settingsService.Load());
    }

    private void RefreshExtendedPrivacySettings(Rice2kAppSettings settings)
    {
        _loading = true;
        try
        {
            RememberRecentFilesCheck.IsChecked = settings.RememberRecentFiles;
            PersistentActivityCheck.IsChecked = settings.PersistentActivityLogEnabled;
            ClearDiskHistoryCheck.IsChecked = settings.ClearDiskHistoryWhenPrivacyModeStarts;
            ReduceMotionCheck.IsChecked = settings.ReduceMotion;
        }
        finally
        {
            _loading = false;
        }
    }

    private void ExtendedPrivacyPreference_Changed(object sender, RoutedEventArgs e)
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
            ? BuildHistoryPreferenceSummary(updated)
            : "History preference could not be saved. Rice2k will not claim the new setting is persistent.";
    }

    private void ReduceMotion_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;

        var current = _settingsService.Load();
        var updated = current with { ReduceMotion = ReduceMotionCheck.IsChecked == true };
        var saved = _settingsService.TrySave(updated);
        _privacyChanged?.Invoke(updated);
        PreferenceStatusText.Text = saved
            ? updated.ReduceMotion
                ? "Reduced Motion enabled. Rice2k will prefer static progress/status presentation where supported."
                : "Reduced Motion disabled."
            : "Reduced Motion preference could not be saved.";
    }

    private void ReviewStoredHistory_Click(object sender, RoutedEventArgs e)
    {
        var window = new PrivacyHistoryWindow { Owner = _owner };
        window.ShowDialog();
    }

    public bool FocusSearchBox() => SettingsSearchBox.Focus();

    private static string BuildHistoryPreferenceSummary(Rice2kAppSettings settings)
    {
        var recent = settings.RememberRecentFiles ? "recent-file paths ON" : "recent-file paths OFF";
        var activity = settings.PersistentActivityLogEnabled ? "redacted activity ON" : "redacted activity OFF";
        var purge = settings.ClearDiskHistoryWhenPrivacyModeStarts ? "Privacy Mode purge ON" : "Privacy Mode purge OFF";
        return $"Stored history: {recent} • {activity} • {purge}.";
    }
}
