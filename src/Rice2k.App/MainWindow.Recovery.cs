using System.Windows;
using System.Windows.Controls;

namespace Rice2k.Encryption;

public partial class MainWindow
{
    private bool _recoveryUiInitialized;

    private void InitializeRecoveryUi()
    {
        if (_recoveryUiInitialized)
            return;

        _recoveryUiInitialized = true;
        if (RecoveryPage.Content is not StackPanel root)
            return;

        if (root.Children.Count > 1 && root.Children[1] is TextBlock description)
        {
            description.Text = "Create password-protected recovery packages, test them in memory, and restore new encrypted .r2kkey packages when needed.";
        }

        foreach (var border in root.Children.OfType<Border>().ToArray())
            border.Visibility = Visibility.Collapsed;

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

    private void OpenRecoveryCenter_Click(object sender, RoutedEventArgs e)
    {
        var recovery = new RecoveryCenterWindow
        {
            Owner = this
        };
        recovery.ShowDialog();
    }
}
