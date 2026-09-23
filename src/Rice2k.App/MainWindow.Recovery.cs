using System.Windows;
using System.Windows.Controls;

namespace Rice2k.Encryption;

public partial class MainWindow
{
    private bool _recoveryUiInitialized;
    private TextBlock? _recoveryHealthText;

    private void InitializeRecoveryUi()
    {
        if (_recoveryUiInitialized)
            return;

        _recoveryUiInitialized = true;
        InitializeRecoveryPage();
        InitializeRecoveryHealthCard();
        RefreshRecoveryHealth();
    }

    private void InitializeRecoveryPage()
    {
        if (RecoveryPage.Content is not StackPanel root)
            return;

        if (root.Children.Count > 1 && root.Children[1] is TextBlock description)
        {
            description.Text = "Create password-protected recovery packages, test them in memory, and restore new encrypted .r2kkey packages when needed.";
        }

        foreach (var border in root.Children.OfType<Border>().ToArray())
            border.Visibility = Visibility.Collapsed;

        var card = CreateRecoveryCard();
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = "Recovery packages are now available",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Create a .r2krecovery package from an existing .r2kkey, protect it with a separate recovery password, test the package before storing it, and restore a fresh .r2kkey package if needed.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 7, 0, 7),
            Opacity = 0.82
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Important: Rice2k cannot recover a lost recovery password. Keep the recovery file and password in separate trusted locations.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });

        var openButton = new Button
        {
            Content = "Open Recovery Center",
            Width = 190,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        openButton.Click += OpenRecoveryCenter_Click;
        panel.Children.Add(openButton);
        card.Child = panel;
        root.Children.Add(card);
    }

    private void InitializeRecoveryHealthCard()
    {
        if (HomePage.Content is not StackPanel homeRoot)
            return;

        var card = CreateRecoveryCard();
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = "Recovery health",
            FontSize = 17,
            FontWeight = FontWeights.SemiBold
        });

        _recoveryHealthText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 7, 0, 10)
        };
        panel.Children.Add(_recoveryHealthText);

        var openButton = new Button
        {
            Content = "Open Recovery Center",
            Width = 180,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        openButton.Click += OpenRecoveryCenter_Click;
        panel.Children.Add(openButton);
        card.Child = panel;
        homeRoot.Children.Add(card);
    }

    private Border CreateRecoveryCard()
    {
        var card = new Border
        {
            Margin = new Thickness(0, 0, 0, 10),
            Padding = new Thickness(18),
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1)
        };

        if (TryFindResource("SurfaceBrush") is System.Windows.Media.Brush surface)
            card.Background = surface;
        if (TryFindResource("BorderBrush") is System.Windows.Media.Brush borderBrush)
            card.BorderBrush = borderBrush;
        return card;
    }

    private void RefreshRecoveryHealth()
    {
        if (_recoveryHealthText is null)
            return;

        var settings = _appSettingsService.Load();
        if (settings.LastRecoveryTestUtc is not { } testedUtc)
        {
            _recoveryHealthText.Text = "⚠ No successful recovery test has been recorded on this PC yet. Creating a backup is not enough—test it before relying on it.";
            return;
        }

        var local = testedUtc.ToLocalTime();
        var keyName = string.IsNullOrWhiteSpace(settings.LastRecoveryKeyName)
            ? "recovery key"
            : settings.LastRecoveryKeyName;
        var fingerprint = string.IsNullOrWhiteSpace(settings.LastRecoveryFingerprint)
            ? string.Empty
            : $" • {settings.LastRecoveryFingerprint}";

        _recoveryHealthText.Text = $"✓ Last successful recovery test: {local:g} • {keyName}{fingerprint}";
    }

    private void OpenRecoveryCenter_Click(object sender, RoutedEventArgs e)
    {
        var recovery = new RecoveryCenterWindow(_appSettingsService)
        {
            Owner = this
        };
        recovery.ShowDialog();
        RefreshRecoveryHealth();
    }
}
