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
            description.Text = "Protect complete folders in one guided operation, or create and manage persistent .r2kvault containers with encrypted filenames, metadata, authenticated file contents, search, verification, recovery backups, and explicit lock state.";
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
            Text = "Folder Protection & Secure Vault",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Protect Folder is the simplest path: choose a folder, destination, and password and Rice2k builds a verified .r2kvault while preserving the original folder. Secure Vault opens the full browser for long-term encrypted storage and file management.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 7, 0, 8),
            Opacity = 0.84
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Vault changes are written to a separate pending copy, authenticated before replacement, and protected by a recovery backup during the final swap. Long operations show detailed progress and support safe cancellation.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Pre-1.0 warning: keep an independent backup of important data. Full build/test execution, large-vault performance profiling, accessibility review, and external security review remain release gates.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 14)
        });

        var actions = new WrapPanel();
        var protectFolder = new Button
        {
            Content = "🔒 Protect Folder",
            Width = 180,
            Height = 42,
            Margin = new Thickness(0, 0, 10, 8)
        };
        protectFolder.Click += ProtectFolder_Click;
        System.Windows.Automation.AutomationProperties.SetHelpText(
            protectFolder,
            "Create a verified encrypted Rice2k vault from an existing folder in one guided workflow");

        var openVault = new Button
        {
            Content = "Open Secure Vault",
            Width = 180,
            Height = 42,
            Margin = new Thickness(0, 0, 0, 8)
        };
        if (TryFindResource("SecondaryButtonStyle") is Style secondary)
            openVault.Style = secondary;
        openVault.Click += OpenSecureVault_Click;
        System.Windows.Automation.AutomationProperties.SetHelpText(
            openVault,
            "Open the full Rice2k Secure Vault browser");

        actions.Children.Add(protectFolder);
        actions.Children.Add(openVault);
        panel.Children.Add(actions);
        card.Child = panel;
        root.Children.Add(card);
    }

    private void ProtectFolder_Click(object sender, RoutedEventArgs e)
    {
        var protector = new FolderProtectionWindow
        {
            Owner = this
        };
        protector.ShowDialog();
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
