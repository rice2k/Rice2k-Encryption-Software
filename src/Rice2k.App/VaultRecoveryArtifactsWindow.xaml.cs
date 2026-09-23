using System.ComponentModel;
using System.Windows;
using Microsoft.Win32;
using Rice2k.Encryption.Models;
using Rice2k.Encryption.Services;

namespace Rice2k.Encryption;

public partial class VaultRecoveryArtifactsWindow : Window
{
    private readonly SecureVaultService _service;
    private readonly SecureVaultSession _session;
    private CancellationTokenSource? _operationCts;
    private bool _busy;
    private bool _closeWhenFinished;

    private sealed record ArtifactRow(
        string Path,
        string Kind,
        string FileName,
        string SizeDisplay,
        string ModifiedDisplay);

    public VaultRecoveryArtifactsWindow(SecureVaultService service, SecureVaultSession session)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        InitializeComponent();
        Closing += VaultRecoveryArtifactsWindow_Closing;
        RefreshRows();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (!_busy)
            RefreshRows();
    }

    private async void VerifySelected_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
            return;

        if (ArtifactsGrid.SelectedItem is not ArtifactRow row)
        {
            ShowWarning("Select a recovery file first.");
            return;
        }

        if (!row.Kind.Contains("pending", StringComparison.OrdinalIgnoreCase))
        {
            StatusText.Text = "Recovery backup selected";
            DetailText.Text = "Use the main Vault Browser recovery controls to verify or restore the .backup copy.";
            return;
        }

        BeginOperation("Verifying interrupted save…", row.FileName);
        try
        {
            var result = await _service.VerifyInterruptedPendingAsync(_session, row.Path, _operationCts!.Token);
            _operationCts.Token.ThrowIfCancellationRequested();
            StatusText.Text = "✓ Interrupted pending save verified";
            DetailText.Text = $"Sequence {result.Sequence:N0} • {result.EntryCount:N0} protected file(s) • updated {result.UpdatedUtc.ToLocalTime():g} • {FormatBytes(result.FileSize)}. Rice2k will not promote this copy automatically.";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Pending-save verification cancelled";
            DetailText.Text = "The recovery artifact was left unchanged.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "⚠ Pending save did not verify";
            DetailText.Text = "Keep the active vault unchanged. Preserve the artifact until the failure is understood.";
            ShowWarning(ex.Message);
        }
        finally
        {
            EndOperation();
        }
    }

    private async void MovePendingAside_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
            return;

        if (ArtifactsGrid.SelectedItem is not ArtifactRow row || !row.Kind.Contains("pending", StringComparison.OrdinalIgnoreCase))
        {
            ShowWarning("Select an interrupted pending-save file first.");
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Preserve verified interrupted vault save",
            Filter = "Rice2k secure vaults (*.r2kvault)|*.r2kvault|All files (*.*)|*.*",
            DefaultExt = ".r2kvault",
            AddExtension = true,
            OverwritePrompt = false,
            FileName = Path.GetFileNameWithoutExtension(_session.VaultPath) + "-interrupted-save.r2kvault"
        };
        if (dialog.ShowDialog(this) != true)
            return;

        BeginOperation(
            "Verifying before moving…",
            "The pending copy must fully authenticate before Rice2k moves it aside.");
        try
        {
            await _service.PreserveInterruptedPendingAsync(
                _session,
                row.Path,
                dialog.FileName,
                _operationCts!.Token);
            _operationCts.Token.ThrowIfCancellationRequested();
            StatusText.Text = "✓ Interrupted save preserved separately";
            DetailText.Text = dialog.FileName;
            RefreshRows();
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Pending-save preservation cancelled";
            DetailText.Text = "The original artifact was left in place unless its verified atomic move had already completed.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "⚠ Pending save was not moved";
            DetailText.Text = "The original artifact was left in place where possible.";
            ShowWarning(ex.Message);
        }
        finally
        {
            EndOperation();
        }
    }

    private void BeginOperation(string status, string detail)
    {
        _operationCts?.Dispose();
        _operationCts = new CancellationTokenSource();
        _busy = true;
        ArtifactsGrid.IsEnabled = false;
        StatusText.Text = status;
        DetailText.Text = detail;
    }

    private void EndOperation()
    {
        _busy = false;
        ArtifactsGrid.IsEnabled = true;
        _operationCts?.Dispose();
        _operationCts = null;

        if (_closeWhenFinished)
        {
            _closeWhenFinished = false;
            Dispatcher.InvokeAsync(Close);
        }
    }

    private void VaultRecoveryArtifactsWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (!_busy)
            return;

        e.Cancel = true;
        _closeWhenFinished = true;
        StatusText.Text = "Cancelling recovery-file operation before closing…";
        DetailText.Text = "The window will close after Rice2k reaches a safe verification/file-operation boundary.";
        _operationCts?.Cancel();
    }

    private void RefreshRows()
    {
        var artifacts = SecureVaultService.FindRecoveryArtifacts(_session.VaultPath)
            .Select(item => new ArtifactRow(
                item.Path,
                item.Kind,
                Path.GetFileName(item.Path),
                FormatBytes(item.FileSize),
                item.LastWriteUtc.ToLocalTime().ToString("g")))
            .ToArray();
        ArtifactsGrid.ItemsSource = artifacts;
        CountText.Text = $"{artifacts.Length:N0} recovery file(s)";
        if (artifacts.Length == 0)
        {
            StatusText.Text = "No recovery files found";
            DetailText.Text = "No preserved .backup or interrupted .pending files are currently beside this vault.";
        }
    }

    private void ShowWarning(string message) =>
        MessageBox.Show(this, message, "Rice2k Vault Recovery Files", MessageBoxButton.OK, MessageBoxImage.Warning);

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
