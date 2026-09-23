using System.Windows;
using Rice2k.Encryption.Services;

namespace Rice2k.Encryption;

public partial class PrivacyHistoryWindow : Window
{
    private readonly AppSettingsService _settingsService = new();
    private readonly PrivacyHistoryService _historyService = new();

    public PrivacyHistoryWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => RefreshHistory();
    }

    private void RefreshHistory()
    {
        var settings = _settingsService.Load();
        var privacyOn = settings.PrivacyModeEnabled;

        PrivacyBanner.Visibility = privacyOn ? Visibility.Visible : Visibility.Collapsed;
        RecentFilesList.Visibility = privacyOn ? Visibility.Collapsed : Visibility.Visible;
        ActivityList.Visibility = privacyOn ? Visibility.Collapsed : Visibility.Visible;

        RecentFilesList.Items.Clear();
        ActivityList.Items.Clear();

        var recent = _historyService.LoadRecentFiles();
        var activity = _historyService.LoadActivity();

        if (!privacyOn)
        {
            foreach (var entry in recent)
                RecentFilesList.Items.Add($"{entry.LastUsedUtc.ToLocalTime():g}  •  {entry.Purpose}  •  {entry.Path}");

            foreach (var entry in activity)
                ActivityList.Items.Add($"{entry.TimestampUtc.ToLocalTime():g}  •  {entry.Action}");
        }

        StatusText.Text = privacyOn
            ? $"Privacy Mode is hiding {recent.Count} recent-file record(s) and {activity.Count} redacted activity record(s)."
            : $"{recent.Count} recent-file record(s) • {activity.Count} redacted activity record(s)";
    }

    private void ClearRecent_Click(object sender, RoutedEventArgs e)
    {
        var cleared = _historyService.TryClearRecentFiles();
        RefreshHistory();
        StatusText.Text = cleared
            ? "✓ Recent-file history cleared."
            : "⚠ Rice2k could not clear recent-file history. The existing file may still be present.";
    }

    private void ClearActivity_Click(object sender, RoutedEventArgs e)
    {
        var cleared = _historyService.TryClearActivity();
        RefreshHistory();
        StatusText.Text = cleared
            ? "✓ Persistent redacted activity cleared."
            : "⚠ Rice2k could not clear persistent activity. The existing file may still be present.";
    }

    private void ClearAll_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            this,
            "Clear all optional Rice2k history stored on this Windows account?\n\nThis removes recent-file paths and the redacted persistent activity log. It does not delete encrypted files, vaults, keys, identities, or recovery packages.",
            "Clear stored history?",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
            return;

        var cleared = _historyService.TryClearAll();
        RefreshHistory();
        StatusText.Text = cleared
            ? "✓ All optional stored history cleared."
            : "⚠ Rice2k could not clear one or more stored-history files. Review the counts above and try again.";
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
