using System.Windows;
using System.Windows.Controls;
using Rice2k.Encryption.Services;

namespace Rice2k.Encryption;

public partial class SettingsSearchPanel : UserControl
{
    private readonly AppSettingsService _settingsService;
    private readonly Window _owner;
    private readonly Action<bool> _hintsChanged;
    private readonly Action<Rice2kAppSettings>? _privacyChanged;
    private bool _loading;

    public SettingsSearchPanel(
        AppSettingsService settingsService,
        Window owner,
        Action<bool> hintsChanged,
        Action<Rice2kAppSettings>? privacyChanged = null)
    {
        _settingsService = settingsService;
        _owner = owner;
        _hintsChanged = hintsChanged;
        _privacyChanged = privacyChanged;

        InitializeComponent();
        LoadSettings();
        ApplySearch();
    }

    public void RefreshPrivacySettings(Rice2kAppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _loading = true;
        try
        {
            PrivacyModeCheck.IsChecked = settings.PrivacyModeEnabled;
            HideActivityCheck.IsChecked = settings.HideActivityInPrivacyMode;
            ClearPreviewsCheck.IsChecked = settings.ClearSensitivePreviewsWhenPrivacyModeStarts;
            LockOnMinimizeCheck.IsChecked = settings.LockOnMinimize;
            SelectClipboardSeconds(settings.ClipboardAutoClearSeconds);
            PrivacyStatusText.Text = settings.PrivacyModeEnabled
                ? "Privacy Mode is on. Session activity is hidden according to your selected privacy options."
                : "Privacy Mode is off. Clipboard protection remains controlled separately below.";
        }
        finally
        {
            _loading = false;
        }
    }

    private void LoadSettings()
    {
        _loading = true;
        try
        {
            var settings = _settingsService.Load();
            HelpfulHintsCheck.IsChecked = settings.ShowHelpfulHints;
            PrivacyModeCheck.IsChecked = settings.PrivacyModeEnabled;
            HideActivityCheck.IsChecked = settings.HideActivityInPrivacyMode;
            ClearPreviewsCheck.IsChecked = settings.ClearSensitivePreviewsWhenPrivacyModeStarts;
            LockOnMinimizeCheck.IsChecked = settings.LockOnMinimize;
            SelectClipboardSeconds(settings.ClipboardAutoClearSeconds);
            _hintsChanged(settings.ShowHelpfulHints);
            _privacyChanged?.Invoke(settings);
        }
        finally
        {
            _loading = false;
        }
    }

    private void HelpfulHints_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;

        var enabled = HelpfulHintsCheck.IsChecked != false;
        var current = _settingsService.Load();
        var updated = current with { ShowHelpfulHints = enabled };
        var saved = _settingsService.TrySave(updated);
        _hintsChanged(enabled);
        PreferenceStatusText.Text = saved
            ? enabled ? "Helpful hints enabled." : "Helpful hints hidden."
            : "Preference could not be saved; it will apply for this session only.";
    }

    private void PrivacyPreference_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;

        var current = _settingsService.Load();
        var updated = current with
        {
            PrivacyModeEnabled = PrivacyModeCheck.IsChecked == true,
            HideActivityInPrivacyMode = HideActivityCheck.IsChecked != false,
            ClearSensitivePreviewsWhenPrivacyModeStarts = ClearPreviewsCheck.IsChecked != false
        };

        var saved = _settingsService.TrySave(updated);
        _privacyChanged?.Invoke(updated);
        PrivacyStatusText.Text = saved
            ? updated.PrivacyModeEnabled
                ? "Privacy Mode is on. Session activity is hidden according to your selected privacy options."
                : "Privacy Mode is off. Clipboard protection remains controlled separately below."
            : "Privacy preference could not be saved; the current session was updated only.";
    }

    private void ClipboardSeconds_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ClipboardSecondsCombo.SelectedItem is not ComboBoxItem item ||
            !int.TryParse(item.Tag?.ToString(), out var seconds))
        {
            return;
        }

        var current = _settingsService.Load();
        var updated = current with { ClipboardAutoClearSeconds = seconds };
        var saved = _settingsService.TrySave(updated);
        _privacyChanged?.Invoke(updated);
        PrivacyStatusText.Text = saved
            ? seconds == 0
                ? "Clipboard auto-clear disabled. Use Clear Clipboard Now whenever needed."
                : $"Rice2k-owned clipboard values will be cleared after {seconds} seconds if they have not been replaced."
            : "Clipboard preference could not be saved; the current session was updated only.";
    }

    private void ClearClipboardNow_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.Clear();
            PrivacyStatusText.Text = "✓ Windows clipboard cleared now.";
        }
        catch (Exception ex)
        {
            PrivacyStatusText.Text = $"Windows clipboard could not be cleared: {ex.Message}";
        }
    }

    private void SelectClipboardSeconds(int seconds)
    {
        foreach (var candidate in ClipboardSecondsCombo.Items.OfType<ComboBoxItem>())
        {
            if (int.TryParse(candidate.Tag?.ToString(), out var value) && value == seconds)
            {
                ClipboardSecondsCombo.SelectedItem = candidate;
                return;
            }
        }
        ClipboardSecondsCombo.SelectedIndex = 2;
    }

    private void ReplayTour_Click(object sender, RoutedEventArgs e)
    {
        var tour = new WelcomeTourWindow(_settingsService)
        {
            Owner = _owner
        };
        tour.ShowDialog();
        PreferenceStatusText.Text = "Welcome tour completed.";
    }

    private void SettingsSearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplySearch();

    private void ApplySearch()
    {
        var query = SettingsSearchBox.Text.Trim();
        var cards = new[] { ExperienceCard, ModeCard, SafetyCard, PrivacyCard };

        if (string.IsNullOrWhiteSpace(query))
        {
            foreach (var card in cards)
                card.Visibility = Visibility.Visible;

            SearchStatusText.Text = "Showing all settings.";
            return;
        }

        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var matches = 0;

        foreach (var card in cards)
        {
            var searchable = card.Tag?.ToString() ?? string.Empty;
            var visible = terms.All(term =>
                searchable.Contains(term, StringComparison.OrdinalIgnoreCase));

            card.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            if (visible)
                matches++;
        }

        SearchStatusText.Text = matches == 0
            ? "No settings matched. Try a broader word such as privacy, clipboard, hints, verify, or advanced."
            : $"{matches} matching setting section(s).";
    }
}
