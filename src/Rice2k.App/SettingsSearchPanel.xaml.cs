using System.Windows;
using System.Windows.Controls;
using Rice2k.Encryption.Services;

namespace Rice2k.Encryption;

public partial class SettingsSearchPanel : UserControl
{
    private readonly AppSettingsService _settingsService;
    private readonly Window _owner;
    private readonly Action<bool> _hintsChanged;
    private bool _loading;

    public SettingsSearchPanel(
        AppSettingsService settingsService,
        Window owner,
        Action<bool> hintsChanged)
    {
        _settingsService = settingsService;
        _owner = owner;
        _hintsChanged = hintsChanged;

        InitializeComponent();
        LoadSettings();
        ApplySearch();
    }

    private void LoadSettings()
    {
        _loading = true;
        try
        {
            var settings = _settingsService.Load();
            HelpfulHintsCheck.IsChecked = settings.ShowHelpfulHints;
            _hintsChanged(settings.ShowHelpfulHints);
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
        var saved = _settingsService.TrySave(current with { ShowHelpfulHints = enabled });
        _hintsChanged(enabled);
        PreferenceStatusText.Text = saved
            ? enabled ? "Helpful hints enabled." : "Helpful hints hidden."
            : "Preference could not be saved; it will apply for this session only.";
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
            ? "No settings matched. Try a broader word such as privacy, hints, verify, or advanced."
            : $"{matches} matching setting section(s).";
    }
}
