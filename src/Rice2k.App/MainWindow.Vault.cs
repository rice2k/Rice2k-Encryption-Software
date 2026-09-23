using System.Windows;
using System.Windows.Controls;

namespace Rice2k.Encryption;

public partial class MainWindow
{
    private bool _vaultUiInitialized;

    private void InitializeVaultUi()
    {
        if (_vaultUiInitialized)
            return;

        _vaultUiInitialized = true;
        if (VaultPage.Content is not StackPanel root)
            return;

        if (root.Children.Count > 1 && root.Children[1] is TextBlock description)
        {
            description.Text = "Create and manage persistent .r2kvault containers with encrypted filenames, metadata, authenticated file contents, search, verification, and explicit lock state.";
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
            Text = "Secure Vault development preview",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Create or unlock a .r2kvault, add files or folders, search encrypted filenames after unlock, extract files, rename/remove entries, verify every encrypted chunk, and lock the vault when finished.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 7, 0, 8),
            Opacity = 0.84
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Mutations are written to a separate pending vault and authenticated before Rice2k replaces the current vault. A recovery backup is retained during the final swap.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Pre-1.0 warning: keep an independent backup of important data. Vault fault-injection, large-vault performance, and external security review are still release gates.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 14)
        });

        var button = new Button
        {
            Content = "Open Secure Vault",
            Width = 180,
            Height = 42,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        button.Click += OpenSecureVault_Click;
        panel.Children.Add(button);
        card.Child = panel;
        root.Children.Add(card);
    }

    private void OpenSecureVault_Click(object sender, RoutedEventArgs e)
    {
        var vault = new SecureVaultWindow
        {
            Owner = this
        };
        vault.ShowDialog();
    }
}
