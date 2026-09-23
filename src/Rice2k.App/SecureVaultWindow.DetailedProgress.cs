using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using Rice2k.Encryption.Models;

namespace Rice2k.Encryption;

public partial class SecureVaultWindow
{
    private CancellationTokenSource? _vaultOperationCts;
    private Stopwatch? _vaultStageStopwatch;
    private string? _vaultProgressStage;

    private (IProgress<VaultOperationProgress> Progress, CancellationToken Token) BeginDetailedVaultOperation(
        string status,
        string detail)
    {
        _vaultOperationCts?.Dispose();
        _vaultOperationCts = new CancellationTokenSource();
        _vaultProgressStage = null;
        _vaultStageStopwatch = Stopwatch.StartNew();

        SetBusy(true, status, detail);
        VaultBusyProgress.IsIndeterminate = true;
        VaultBusyProgress.Value = 0;
        VaultCancelButton.Visibility = Visibility.Visible;
        VaultCancelButton.IsEnabled = true;

        var progress = new Progress<VaultOperationProgress>(UpdateDetailedVaultProgress);
        return (progress, _vaultOperationCts.Token);
    }

    private void EndDetailedVaultOperation()
    {
        VaultCancelButton.IsEnabled = false;
        VaultCancelButton.Visibility = Visibility.Collapsed;
        VaultBusyProgress.IsIndeterminate = true;
        VaultBusyProgress.Value = 0;
        _vaultStageStopwatch = null;
        _vaultProgressStage = null;
        _vaultOperationCts?.Dispose();
        _vaultOperationCts = null;
        SetBusy(false);
    }

    private void UpdateDetailedVaultProgress(VaultOperationProgress value)
    {
        if (!string.Equals(_vaultProgressStage, value.Stage, StringComparison.Ordinal))
        {
            _vaultProgressStage = value.Stage;
            _vaultStageStopwatch = Stopwatch.StartNew();
        }

        VaultOperationText.Text = value.Stage;
        var itemText = string.IsNullOrWhiteSpace(value.CurrentItem)
            ? string.Empty
            : $" • {value.CurrentItem}";

        if (!value.IsDeterminate)
        {
            VaultBusyProgress.IsIndeterminate = true;
            var discovered = value.ItemsProcessed > 0 ? $" • {value.ItemsProcessed:N0} item(s)" : string.Empty;
            VaultDetailText.Text = $"Working…{discovered}{itemText}";
            return;
        }

        VaultBusyProgress.IsIndeterminate = false;
        VaultBusyProgress.Value = value.Percentage;

        var elapsed = _vaultStageStopwatch?.Elapsed ?? TimeSpan.Zero;
        var detail = $"{value.Percentage:0}%";
        if (value.TotalBytes > 0)
        {
            detail += $" • {FormatBytes(value.BytesProcessed)} of {FormatBytes(value.TotalBytes)}";
            if (elapsed.TotalSeconds >= 0.5 && value.BytesProcessed > 0)
            {
                var bytesPerSecond = value.BytesProcessed / Math.Max(elapsed.TotalSeconds, 0.001);
                detail += $" • {FormatBytes((long)bytesPerSecond)}/s";
                if (value.BytesProcessed < value.TotalBytes && bytesPerSecond > 0)
                {
                    var remaining = TimeSpan.FromSeconds((value.TotalBytes - value.BytesProcessed) / bytesPerSecond);
                    detail += $" • about {FormatShortDuration(remaining)} remaining";
                }
            }
        }
        else if (value.TotalItems > 0)
        {
            detail += $" • {value.ItemsProcessed:N0} of {value.TotalItems:N0} item(s)";
        }

        if (elapsed > TimeSpan.Zero)
            detail += $" • elapsed {FormatShortDuration(elapsed)}";
        VaultDetailText.Text = detail + itemText;
    }

    private void CancelVaultOperation_Click(object sender, RoutedEventArgs e)
    {
        if (_vaultOperationCts is null || _vaultOperationCts.IsCancellationRequested)
            return;

        VaultCancelButton.IsEnabled = false;
        VaultOperationText.Text = "Cancelling safely…";
        VaultDetailText.Text = "Rice2k will stop at a safe boundary, discard incomplete temporary output where possible, and preserve the last known-good vault state.";
        _vaultOperationCts.Cancel();
    }

    private async void AddFilesDetailed_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureUnlocked() || _busy)
            return;

        var dialog = new OpenFileDialog
        {
            Title = "Add files to secure vault",
            CheckFileExists = true,
            Multiselect = true
        };
        if (dialog.ShowDialog(this) != true)
            return;

        var planned = new List<(string SourcePath, string VaultPath)>();
        var occupied = CurrentOccupiedPaths();
        foreach (var file in dialog.FileNames)
        {
            var proposed = MakeUniqueVaultPath(Path.GetFileName(file), occupied);
            occupied.Add(proposed);
            planned.Add((file, proposed));
        }

        var operation = BeginDetailedVaultOperation(
            "Adding files…",
            $"Rice2k will authenticate the current vault, protect {planned.Count:N0} file(s), verify the pending replacement, and verify the finalized vault.");

        try
        {
            await _service.AddFilesWithProgressAsync(_session!, planned, operation.Progress, operation.Token);
            RefreshEntries();
            SetStatus("✓ Files added", $"{planned.Count:N0} file(s) added. Vault sequence is now {_session!.Sequence:N0}.");
        }
        catch (OperationCanceledException)
        {
            SetStatus("Operation cancelled safely", "Incomplete pending output was discarded where possible. The last known-good vault remains preferred.");
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
            SetStatus("⚠ Add operation did not complete", "The previous vault remains the active copy unless a preserved recovery backup is shown above.");
        }
        finally
        {
            EndDetailedVaultOperation();
            RefreshBackupWarning();
        }
    }

    private async void AddFolderDetailed_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureUnlocked() || _busy)
            return;

        var dialog = new OpenFolderDialog
        {
            Title = "Choose a folder to protect inside the vault",
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true)
            return;

        var operation = BeginDetailedVaultOperation(
            "Scanning folder…",
            "Discovering files. Inaccessible folders and reparse/junction folders will be skipped.");

        try
        {
            var scan = await Task.Run(() => ScanFolderDetailed(dialog.FolderName, operation.Progress, operation.Token), operation.Token);
            if (scan.Files.Count == 0)
            {
                SetStatus(
                    "Folder contains no files",
                    scan.SkippedFolders > 0
                        ? $"{scan.SkippedFolders:N0} inaccessible or reparse-point folder(s) were skipped."
                        : "Nothing was added.");
                return;
            }

            var rootName = MakeUniqueVaultRoot(new DirectoryInfo(dialog.FolderName).Name, CurrentOccupiedPaths());
            var planned = scan.Files
                .Select(item => (item.SourcePath, VaultPath: $"{rootName}/{item.RelativePath.Replace('\\', '/')}"))
                .ToArray();

            await _service.AddFilesWithProgressAsync(_session!, planned, operation.Progress, operation.Token);
            RefreshEntries();
            var skipped = scan.SkippedFolders > 0
                ? $" {scan.SkippedFolders:N0} inaccessible/reparse folder(s) were skipped."
                : string.Empty;
            SetStatus("✓ Folder added", $"{planned.Length:N0} file(s) protected beneath '{rootName}'.{skipped}");
        }
        catch (OperationCanceledException)
        {
            SetStatus("Operation cancelled safely", "The folder scan or pending vault operation stopped. The last known-good vault remains preferred.");
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
            SetStatus("⚠ Folder add did not complete", "Rice2k did not intentionally replace the last known-good vault with an unverified pending copy.");
        }
        finally
        {
            EndDetailedVaultOperation();
            RefreshBackupWarning();
        }
    }

    private async void ExtractDetailed_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureUnlocked() || _busy || VaultEntriesGrid.SelectedItem is not VaultEntryInfo entry)
        {
            if (_session is not null && VaultEntriesGrid.SelectedItem is null)
                ShowError("Select a file in the vault first.");
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Restore file from Rice2k vault",
            FileName = entry.Name,
            OverwritePrompt = false
        };
        if (dialog.ShowDialog(this) != true)
            return;

        var operation = BeginDetailedVaultOperation("Restoring file…", entry.Path);
        try
        {
            await _service.ExtractWithProgressAsync(_session!, entry.Id, dialog.FileName, operation.Progress, operation.Token);
            SetStatus("✓ File restored", dialog.FileName);
        }
        catch (OperationCanceledException)
        {
            SetStatus("Restore cancelled safely", "Incomplete restored output was discarded where possible. The encrypted vault was not changed.");
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
            SetStatus("⚠ Restore did not complete", "Incomplete temporary output was discarded where possible.");
        }
        finally
        {
            EndDetailedVaultOperation();
        }
    }

    private async void RenameDetailed_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureUnlocked() || _busy || VaultEntriesGrid.SelectedItem is not VaultEntryInfo entry)
        {
            if (_session is not null && VaultEntriesGrid.SelectedItem is null)
                ShowError("Select a file in the vault first.");
            return;
        }

        var newPath = PromptForText("Rename Vault Entry", "Enter the new relative path inside the vault.", entry.Path);
        if (newPath is null)
            return;

        var operation = BeginDetailedVaultOperation(
            "Renaming entry…",
            "Rice2k will authenticate the current vault and verify the replacement before finalizing the metadata change.");
        try
        {
            await _service.RenameEntryWithProgressAsync(_session!, entry.Id, newPath, operation.Progress, operation.Token);
            RefreshEntries();
            SetStatus("✓ Entry renamed", newPath);
        }
        catch (OperationCanceledException)
        {
            SetStatus("Rename cancelled safely", "The last known-good authenticated vault state remains preferred.");
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
            SetStatus("⚠ Rename did not complete", "The previous authenticated vault state remains preferred.");
        }
        finally
        {
            EndDetailedVaultOperation();
            RefreshBackupWarning();
        }
    }

    private async void RemoveDetailed_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureUnlocked() || _busy || VaultEntriesGrid.SelectedItem is not VaultEntryInfo entry)
        {
            if (_session is not null && VaultEntriesGrid.SelectedItem is null)
                ShowError("Select a file in the vault first.");
            return;
        }

        var result = MessageBox.Show(
            this,
            $"Remove this file from the vault?\n\n{entry.Path}\n\nRice2k will build and verify a replacement vault before finalizing the removal.",
            "Remove protected file?",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
            return;

        var operation = BeginDetailedVaultOperation("Removing entry…", entry.Path);
        try
        {
            await _service.RemoveEntryWithProgressAsync(_session!, entry.Id, operation.Progress, operation.Token);
            RefreshEntries();
            SetStatus("✓ Entry removed", "The replacement vault authenticated successfully before the old state was released.");
        }
        catch (OperationCanceledException)
        {
            SetStatus("Removal cancelled safely", "The last known-good authenticated vault state remains preferred.");
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
            SetStatus("⚠ Remove did not complete", "Review any recovery-backup warning before making another change.");
        }
        finally
        {
            EndDetailedVaultOperation();
            RefreshBackupWarning();
        }
    }

    private async void VerifyVaultDetailed_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureUnlocked() || _busy)
            return;

        var operation = BeginDetailedVaultOperation(
            "Verifying vault…",
            "Authenticating the encrypted manifest and every protected file chunk.");
        try
        {
            await _service.VerifyWithProgressAsync(_session!, operation.Progress, operation.Token);
            SetStatus("✓ Vault verification passed", $"{_session!.Entries.Count:N0} protected file(s) authenticated successfully.");
        }
        catch (OperationCanceledException)
        {
            SetStatus("Verification cancelled", "The vault was not modified. A complete verification is still recommended before relying on it.");
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
            SetStatus("⚠ Vault requires attention", "Do not add/remove/rename data until the problem is understood. Preserve any .backup file.");
        }
        finally
        {
            EndDetailedVaultOperation();
            RefreshBackupWarning();
        }
    }

    private FolderScanResult ScanFolderDetailed(
        string rootPath,
        IProgress<VaultOperationProgress> progress,
        CancellationToken cancellationToken)
    {
        var files = new List<(string SourcePath, string RelativePath)>();
        var skipped = 0;
        var root = new DirectoryInfo(rootPath);
        var pending = new Stack<DirectoryInfo>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = pending.Pop();
            try
            {
                if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    skipped++;
                    continue;
                }

                try
                {
                    foreach (var file in current.EnumerateFiles())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        files.Add((file.FullName, Path.GetRelativePath(root.FullName, file.FullName).Replace('\\', '/')));
                        progress.Report(new VaultOperationProgress(
                            "Scanning folder",
                            0,
                            0,
                            files.Count,
                            0,
                            file.FullName));
                    }
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
                {
                    skipped++;
                }

                try
                {
                    foreach (var child in current.EnumerateDirectories())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        try
                        {
                            if ((child.Attributes & FileAttributes.ReparsePoint) != 0)
                                skipped++;
                            else
                                pending.Push(child);
                        }
                        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
                        {
                            skipped++;
                        }
                    }
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
                {
                    skipped++;
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                skipped++;
            }
        }

        return new FolderScanResult(files, skipped);
    }

    private static string FormatShortDuration(TimeSpan value)
    {
        if (value.TotalHours >= 1)
            return $"{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}";
        if (value.TotalMinutes >= 1)
            return $"{(int)value.TotalMinutes}:{value.Seconds:00}";
        return $"{Math.Max(0, (int)Math.Ceiling(value.TotalSeconds))} sec";
    }

    private void Window_MouseActivity(object sender, MouseEventArgs e) => _lastActivityUtc = DateTimeOffset.UtcNow;
    private void Window_KeyActivity(object sender, KeyEventArgs e) => _lastActivityUtc = DateTimeOffset.UtcNow;
}
