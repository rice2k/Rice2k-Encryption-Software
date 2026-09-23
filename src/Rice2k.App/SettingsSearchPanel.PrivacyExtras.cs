using System.Windows;
using System.Windows.Controls;
using Rice2k.Encryption.Services;

namespace Rice2k.Encryption;

public partial class SettingsSearchPanel
{
    private bool _privacyExtrasHooked;
    private CheckBox? _desktopNotificationsCheck;

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
        EnsureDesktopNotificationControl();

        _loading = true;
        try
        {
            var settings = _settingsService.Load();
            RememberRecentFilesCheck.IsChecked = settings.RememberRecentFiles;
            PersistentActivityCheck.IsChecked = settings.PersistentActivityLogEnabled;
            ClearDiskHistoryCheck.IsChecked = settings.ClearDiskHistoryWhenPrivacyModeStarts;
            ReduceMotionCheck.IsChecked = settings.ReduceMotion;
            if (_desktopNotificationsCheck is not null)
                _desktopNotificationsCheck.IsChecked = settings.DesktopNotificationsEnabled;
        }
        finally
        {
            _loading = false;
        }

        // These three controls were originally wired in XAML to the broad
        // PrivacyPreference_Changed handler. Detach that legacy handler before
        // attaching the dedicated history handler so one click results in one
        // coherent settings write/callback with the new history values.
        RememberRecentFilesCheck.Checked -= PrivacyPreference_Changed;
        RememberRecentFilesCheck.Unchecked -= PrivacyPreference_Changed;
        PersistentActivityCheck.Checked -= PrivacyPreference_Changed;
        PersistentActivityCheck.Unchecked -= PrivacyPreference_Changed;
        ClearDiskHistoryCheck.Checked -= PrivacyPreference_Changed;
        ClearDiskHistoryCheck.Unchecked -= PrivacyPreference_Changed;

        RememberRecentFilesCheck.Checked += OptionalHistoryPreference_Changed;
        RememberRecentFilesCheck.Unchecked += OptionalHistoryPreference_Changed;
        PersistentActivityCheck.Checked += OptionalHistoryPreference_Changed;
        PersistentActivityCheck.Unchecked += OptionalHistoryPreference_Changed;
        ClearDiskHistoryCheck.Checked += OptionalHistoryPreference_Changed;
        ClearDiskHistoryCheck.Unchecked += OptionalHistoryPreference_Changed;
    }

    private void EnsureDesktopNotificationControl()
    {
        if (_desktopNotificationsCheck is not null || PrivacyCard.Child is not StackPanel panel)
            return;

        panel.Children.Add(new Separator { Margin = new Thickness(0, 18, 0, 12) });
        panel.Children.Add(new TextBlock
        {
            Text = "Desktop completion notifications",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 5)
        });

        _desktopNotificationsCheck = new CheckBox
        {
            Content = "Show generic Windows notifications when encryption/decryption finishes",
            Foreground = TryFindResource("TextBrush") as System.Windows.Media.Brush,
            ToolTip = "Notifications never include filenames, paths, passwords, keys, or plaintext. Privacy Mode suppresses them.",
            Margin = new Thickness(0, 0, 0, 4)
        };
        _desktopNotificationsCheck.Checked += DesktopNotifications_Changed;
        _desktopNotificationsCheck.Unchecked += DesktopNotifications_Changed;
        panel.Children.Add(_desktopNotificationsCheck);
        panel.Children.Add(new TextBlock
        {
            Text = "Completion notifications are intentionally generic and are automatically suppressed while Privacy Mode is on.",
            Foreground = TryFindResource("MutedTextBrush") as System.Windows.Media.Brush,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(20, 2, 0, 0)
        });
    }

    private void DesktopNotifications_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || _desktopNotificationsCheck is null)
            return;

        var enabled = _desktopNotificationsCheck.IsChecked == true;
        var current = _settingsService.Load();
        var updated = current with { DesktopNotificationsEnabled = enabled };
        var saved = _settingsService.TrySave(updated);
        _privacyChanged?.Invoke(updated);
        PrivacyStatusText.Text = saved
            ? enabled
                ? "Generic completion notifications enabled. Privacy Mode will suppress them."
                : "Desktop completion notifications disabled."
            : "Desktop-notification preference could not be saved; Rice2k will continue using the last saved setting.";
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
