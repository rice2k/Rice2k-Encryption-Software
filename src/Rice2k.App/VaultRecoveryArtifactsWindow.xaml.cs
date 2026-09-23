using System.Windows;
using Microsoft.Win32;
using Rice2k.Encryption.Models;
using Rice2k.Encryption.Services;

namespace Rice2k.Encryption;

public partial class VaultRecoveryArtifactsWindow : Window
{
    private readonly SecureVaultService _service;
    private readonly SecureVaultSession _session;

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
        RefreshRows();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshRows();

    private async void VerifySelected_Click(object sender, RoutedEventArgs e)
    {
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

        try
        {
            StatusText.Text = "Verifying interrupted save…";
            DetailText.Text = row.FileName;
            var result = await _service.VerifyInterruptedPendingAsync(_session, row.Path);
            StatusText.Text = "✓ Interrupted pending save verified";
            DetailText.Text = $"Sequence {result.Sequence:N0} • {result.EntryCount:N0} protected file(s) • updated {result.UpdatedUtc.ToLocalTime():g} • {FormatBytes(result.FileSize)}. Rice2k will not promote this copy automatically.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "⚠ Pending save did not verify";
            DetailText.Text = "Keep the active vault unchanged. Preserve the artifact until the failure is understood.";
            ShowWarning(ex.Message);
        }
    }

    private async void MovePendingAside_Click(object sender, RoutedEventArgs e)
    {
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

        try
        {
            StatusText.Text = "Verifying before moving…";
            DetailText.Text = "The pending copy must fully authenticate before Rice2k moves it aside.";
            await _service.PreserveInterruptedPendingAsync(_session, row.Path, dialog.FileName);
            StatusText.Text = "✓ Interrupted save preserved separately";
            DetailText.Text = dialog.FileName;
            RefreshRows();
        }
        catch (Exception ex)
        {
            StatusText.Text = "⚠ Pending save was not moved";
            DetailText.Text = "The original artifact was left in place where possible.";
            ShowWarning(ex.Message);
        }
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
