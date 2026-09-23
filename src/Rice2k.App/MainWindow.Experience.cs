using System.Windows;
using System.Windows.Controls;

namespace Rice2k.Encryption;

public partial class MainWindow
{
    private TextBlock? _encryptHintText;
    private TextBlock? _decryptHintText;
    private bool _experienceUiInitialized;

    private void InitializeExperienceUi()
    {
        if (_experienceUiInitialized)
            return;

        _experienceUiInitialized = true;
        _encryptHintText = CreateHintText(
            "Tip: encrypting several files or an entire folder? Use Batch Queue from the sidebar. Your original files stay unchanged.");
        _decryptHintText = CreateHintText(
            "Tip: a wrong password or modified .r2kenc file fails authentication instead of silently producing damaged plaintext.");

        InsertAfter(EncryptSourceSummaryText, _encryptHintText);
        InsertAfter(DecryptSourceSummaryText, _decryptHintText);

        if (SettingsPage.Content is StackPanel settingsRoot)
        {
            foreach (var existingCard in settingsRoot.Children.OfType<Border>().ToArray())
                existingCard.Visibility = Visibility.Collapsed;

            settingsRoot.Children.Add(new SettingsSearchPanel(
                _appSettingsService,
                this,
                ApplyHelpfulHints));
        }

        ApplyHelpfulHints(_appSettingsService.Load().ShowHelpfulHints);
    }

    private static TextBlock CreateHintText(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        FontSize = 12,
        Margin = new Thickness(0, 8, 0, 0),
        Opacity = 0.82
    };

    private static void InsertAfter(FrameworkElement anchor, UIElement element)
    {
        if (anchor.Parent is not Panel panel)
            return;

        var index = panel.Children.IndexOf(anchor);
        if (index >= 0)
            panel.Children.Insert(index + 1, element);
        else
            panel.Children.Add(element);
    }

    private void ApplyHelpfulHints(bool enabled)
    {
        var visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        if (_encryptHintText is not null)
            _encryptHintText.Visibility = visibility;
        if (_decryptHintText is not null)
            _decryptHintText.Visibility = visibility;
    }
}
