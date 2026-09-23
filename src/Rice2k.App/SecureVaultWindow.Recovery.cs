using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace Rice2k.Encryption;

public partial class SecureVaultWindow
{
    private bool _recoveryBackupUiInitialized;

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        InitializeRecoveryBackupUi();
    }

    private void InitializeRecoveryBackupUi()
    {
        if (_recoveryBackupUiInitialized)
            return;
        _recoveryBackupUiInitialized = true;

        if (BackupWarningBorder.Child is not StackPanel panel)
            return;

        var explanation = new TextBlock
        {
            Text = "Verify the backup before deciding what to do. Restoring keeps the current active vault as a separate pre-recovery backup instead of deleting it.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Margin = new Thickness(0, 6, 0, 10),
            Opacity = 0.84
        };
        panel.Children.Add(explanation);

        var actions = new WrapPanel();
        var verify = new Button
        {
            Content = "Verify Backup",
            MinWidth = 125,
            Margin = new Thickness(0, 0, 8, 4)
        };
        verify.Click += VerifyRecoveryBackup_Click;

        var restore = new Button
        {
            Content = "Restore Backup",
            MinWidth = 130,
            Margin = new Thickness(0, 0, 8, 4)
        };
        restore.Click += RestoreRecoveryBackup_Click;

        var preserve = new Button
        {
            Content = "Move Backup Aside…",
            MinWidth = 155,
            Margin = new Thickness(0, 0, 0, 4)
        };
        if (TryFindResource("SecondaryButtonStyle") is Style secondary)
        {
            verify.Style = secondary;
            preserve.Style = secondary;
        }
        preserve.Click += PreserveRecoveryBackup_Click;

        actions.Children.Add(verify);
        actions.Children.Add(restore);
        actions.Children.Add(preserve);
        panel.Children.Add(actions);
    }

    private async void VerifyRecoveryBackup_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureUnlocked() || _busy)
            return;

        try
        {
            SetBusy(true, "Verifying recovery backup…", "Authenticating the backup manifest and every encrypted file chunk without modifying either vault copy.");
            var info = await _service.VerifyRecoveryBackupAsync(_session!);
            SetStatus(
                "✓ Recovery backup verified",
                $"Sequence {info.Sequence:N0} • {info.EntryCount:N0} protected file(s) • updated {info.UpdatedUtc.ToLocalTime():g} • {FormatBytes(info.FileSize)}");
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
            SetStatus("⚠ Recovery backup did not verify", "Do not delete the active vault or backup. Preserve both until the issue is understood.");
        }
        finally
        {
            SetBusy(false);
            RefreshBackupWarning();
        }
    }

    private async void RestoreRecoveryBackup_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureUnlocked() || _busy)
            return;

        var result = MessageBox.Show(
            this,
            "Restore the preserved recovery backup?\n\nRice2k will first authenticate the backup. The current active vault will be preserved as a separate pre-recovery backup instead of being deleted.\n\nUse this only when you intentionally want to return to the preserved vault state.",
            "Restore recovery backup?",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
            return;

        try
        {
            SetBusy(true, "Restoring recovery backup…", "Authenticating the backup and preserving the current active vault before the swap.");
            var archivedCurrent = await _service.RestoreRecoveryBackupAsync(_session!);
            RefreshEntries();
            RefreshBackupWarning();
            SetStatus(
                "✓ Recovery backup restored",
                $"The previous active vault was preserved as {Path.GetFileName(archivedCurrent)}. The restored vault contains {_session!.Entries.Count:N0} protected file(s).");
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
            SetStatus("⚠ Recovery restore needs attention", "Rice2k attempted to preserve all recovery copies. Do not delete .backup or pre-recovery files until they have been reviewed.");
        }
        finally
        {
            SetBusy(false);
            RefreshBackupWarning();
        }
    }

    private async void PreserveRecoveryBackup_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureUnlocked() || _busy)
            return;

        var sourceBackup = Services.SecureVaultService.RecoveryBackupPath(_session!.VaultPath);
        if (!File.Exists(sourceBackup))
        {
            ShowError("No preserved recovery backup was found.");
            RefreshBackupWarning();
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Move verified vault recovery backup aside",
            Filter = "Rice2k secure vault recovery copy (*.r2kvault)|*.r2kvault|All files (*.*)|*.*",
            DefaultExt = ".r2kvault",
            AddExtension = true,
            OverwritePrompt = false,
            FileName = Path.GetFileNameWithoutExtension(_session.VaultPath) + "-recovery-copy.r2kvault"
        };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            SetBusy(true, "Verifying and moving recovery backup…", "The backup must fully authenticate before Rice2k moves it away from the protected recovery location.");
            await _service.PreserveRecoveryBackupAsync(_session, dialog.FileName);
            RefreshBackupWarning();
            SetStatus("✓ Recovery backup preserved separately", dialog.FileName);
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
            SetStatus("⚠ Recovery backup was not moved", "The original recovery backup was left in place where possible.");
        }
        finally
        {
            SetBusy(false);
            RefreshBackupWarning();
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = Math.Max(0, bytes);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.##} {units[unit]}";
    }
}
