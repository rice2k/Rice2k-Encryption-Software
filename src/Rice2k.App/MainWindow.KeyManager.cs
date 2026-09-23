using System.Windows;
using System.Windows.Controls;

namespace Rice2k.Encryption;

public partial class MainWindow
{
    private bool _keyManagerUiInitialized;

    private void InitializeKeyManagerUi()
    {
        if (_keyManagerUiInitialized)
            return;

        _keyManagerUiInitialized = true;
        if (PasswordsPage.Content is not StackPanel root)
            return;

        if (root.Children.Count > 1 && root.Children[1] is TextBlock description)
        {
            description.Text = "Generate strong passwords and manage encrypted Rice2k key packages without exposing raw secret key material.";
        }

        var card = new Border
        {
            Margin = new Thickness(0, 0, 0, 10),
            Padding = new Thickness(18),
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1)
        };

        if (TryFindResource("SurfaceBrush") is System.Windows.Media.Brush surface)
            card.Background = surface;
        if (TryFindResource("BorderBrush") is System.Windows.Media.Brush border)
            card.BorderBrush = border;

        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = "Encryption Key Manager",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Create 256-bit keys, compare safe fingerprints, and export/import password-protected .r2kkey packages. Secret key bytes are never displayed in the normal interface.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 7, 0, 12),
            Opacity = 0.82
        });

        var openButton = new Button
        {
            Content = "Open Key Manager",
            Width = 170,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        openButton.Click += OpenKeyManager_Click;
        panel.Children.Add(openButton);
        card.Child = panel;
        root.Children.Add(card);
    }

    private void OpenKeyManager_Click(object sender, RoutedEventArgs e)
    {
        var manager = new KeyManagerWindow
        {
            Owner = this
        };
        manager.ShowDialog();
    }
}
