using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace Rice2k.Encryption;

public partial class SecureVaultWindow
{
    private bool _vaultHardeningUiInitialized;

    private void InitializeVaultHardeningUi()
    {
        if (_vaultHardeningUiInitialized)
            return;
        _vaultHardeningUiInitialized = true;

        AutomationProperties.SetName(SearchBox, "Search unlocked vault files");
        AutomationProperties.SetHelpText(SearchBox, "Searches protected filenames and relative paths only while this vault is unlocked.");
        AutomationProperties.SetName(VaultEntriesGrid, "Protected vault files");
        AutomationProperties.SetHelpText(VaultEntriesGrid, "Lists decrypted file metadata only during the current unlocked vault session.");
        AutomationProperties.SetLiveSetting(VaultOperationText, AutomationLiveSetting.Polite);
        AutomationProperties.SetLiveSetting(VaultDetailText, AutomationLiveSetting.Polite);
        AutomationProperties.SetName(AutoLockCheckBox, "Automatically lock vault after ten minutes of inactivity");

        if (UnlockedPanel.Children.OfType<Border>().FirstOrDefault() is Border toolbarCard &&
            toolbarCard.Child is Grid toolbarRoot &&
            toolbarRoot.Children.OfType<Grid>().FirstOrDefault() is Grid topRow &&
            topRow.Children.OfType<StackPanel>().FirstOrDefault(panel => Grid.GetColumn(panel) == 1) is StackPanel rightActions)
        {
            var recoveryButton = new Button
            {
                Content = "Recovery Files…",
                MinWidth = 130,
                ToolTip = "Review preserved .backup and interrupted .pending vault files"
            };
            if (TryFindResource("SecondaryButtonStyle") is Style secondary)
                recoveryButton.Style = secondary;
            AutomationProperties.SetName(recoveryButton, "Open vault recovery files");
            AutomationProperties.SetHelpText(recoveryButton, "Review, verify, and preserve recovery artifacts left beside this vault.");
            recoveryButton.Click += OpenRecoveryArtifacts_Click;
            rightActions.Children.Insert(Math.Max(0, rightActions.Children.Count - 1), recoveryButton);
        }

        PreviewKeyDown += SecureVaultWindow_PreviewKeyDown;
    }

    private void OpenRecoveryArtifacts_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureUnlocked() || _busy)
            return;

        var window = new VaultRecoveryArtifactsWindow(_service, _session!)
        {
            Owner = this
        };
        window.ShowDialog();
        RefreshBackupWarning();
        SetStatus("Recovery files reviewed", "Rice2k did not change the active vault unless you explicitly used an existing Restore Backup action.");
    }

    private void SecureVaultWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _session is not null && !_busy)
        {
            LockVault("Vault locked with Esc");
            e.Handled = true;
            return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            if (e.Key == Key.F && _session is not null)
            {
                SearchBox.Focus();
                SearchBox.SelectAll();
                e.Handled = true;
            }
            else if (e.Key == Key.L && _session is not null && !_busy)
            {
                LockVault("Vault locked with Ctrl+L");
                e.Handled = true;
            }
        }
    }
}
