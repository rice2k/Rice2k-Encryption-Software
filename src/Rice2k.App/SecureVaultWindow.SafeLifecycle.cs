using System.ComponentModel;
using System.Windows;

namespace Rice2k.Encryption;

public partial class SecureVaultWindow
{
    private bool _closeVaultWhenFinished;
    private bool _vaultCloseWaitStarted;
    private bool _vaultCloseApproved;

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
        if (_vaultCloseApproved)
        {
            Window_Closing(sender, e);
            return;
        }

        if (_busy)
        {
            e.Cancel = true;
            _closeVaultWhenFinished = true;

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
                VaultDetailText.Text = "The Vault window will close automatically after the current non-cancellable operation finishes safely.";
            }

            if (!_vaultCloseWaitStarted)
            {
                _vaultCloseWaitStarted = true;
                _ = CompleteVaultCloseAfterOperationAsync();
            }
            return;
        }

        Window_Closing(sender, e);
    }

    private async Task CompleteVaultCloseAfterOperationAsync()
    {
        try
        {
            while (_busy && !Dispatcher.HasShutdownStarted)
                await Task.Delay(100);
        }
        finally
        {
            _vaultCloseWaitStarted = false;
        }

        if (!_closeVaultWhenFinished || Dispatcher.HasShutdownStarted)
            return;

        _closeVaultWhenFinished = false;
        _vaultCloseApproved = true;
        Dispatcher.BeginInvoke(new Action(Close));
    }
}
