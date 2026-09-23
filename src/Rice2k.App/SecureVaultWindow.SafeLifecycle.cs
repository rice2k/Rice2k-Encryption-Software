using System.ComponentModel;
using System.Windows;

namespace Rice2k.Encryption;

public partial class SecureVaultWindow
{
    private void LockVaultSafe_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            ShowError("A vault operation is still running. Cancel it safely or let it finish before locking the vault.");
            return;
        }

        LockVault("Vault locked by user");
    }

    private void Window_ClosingEnhanced(object? sender, CancelEventArgs e)
    {
        if (_busy)
        {
            e.Cancel = true;

            if (_vaultOperationCts is not null && !_vaultOperationCts.IsCancellationRequested)
            {
                VaultCancelButton.IsEnabled = false;
                VaultOperationText.Text = "Cancelling before close…";
                VaultDetailText.Text = "Rice2k is stopping the current operation at a safe boundary before the Vault window can close.";
                _vaultOperationCts.Cancel();
            }
            else
            {
                VaultOperationText.Text = "Operation still finishing…";
                VaultDetailText.Text = "The Vault window will remain open until the current non-cancellable operation finishes safely.";
            }

            return;
        }

        Window_Closing(sender, e);
    }
}
