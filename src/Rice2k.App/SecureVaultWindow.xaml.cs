using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using Rice2k.Encryption.Models;
using Rice2k.Encryption.Services;

namespace Rice2k.Encryption;

public partial class SecureVaultWindow : Window
{
    private readonly SecureVaultService _service = new();
    private readonly DispatcherTimer _autoLockTimer;
    private SecureVaultSession? _session;
    private DateTimeOffset _lastActivityUtc = DateTimeOffset.UtcNow;
    private bool _busy;

    private sealed record FolderScanResult(IReadOnlyList<(string SourcePath, string RelativePath)> Files, int SkippedFolders);

    public SecureVaultWindow()
    {
        InitializeComponent();
        _autoLockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _autoLockTimer.Tick += AutoLockTimer_Tick;
        _autoLockTimer.Start();
        ShowLocked("No vault open");
    }

    private async void CreateVault_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
            return;

        var dialog = new SaveFileDialog
        {
            Title = "Create Rice2k secure vault",
            Filter = "Rice2k secure vaults (*.r2kvault)|*.r2kvault",
            DefaultExt = ".r2kvault",
            AddExtension = true,
            OverwritePrompt = false,
            FileName = "Secure Vault.r2kvault"
        };
        if (dialog.ShowDialog(this) != true)
            return;

        var password = PromptForPassword(
            "Create Secure Vault",
            "Choose a vault password of at least 12 characters. Rice2k cannot recover this password.",
            confirm: true);
        if (password is null)
            return;

        try
        {
            SetBusy(true, "Creating vault…", "Preparing encrypted manifest and verifying the empty vault before finalizing it.");
            _session?.Dispose();
            _session = await _service.CreateAsync(dialog.FileName, password);
            ShowUnlocked("✓ Vault created and unlocked", "The empty vault was authenticated successfully.");
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
            ShowLocked("Vault creation did not complete");
        }
        finally
        {
            password = string.Empty;
            SetBusy(false);
        }
    }

    private async void OpenVault_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
            return;

        var dialog = new OpenFileDialog
        {
            Title = "Open Rice2k secure vault",
            Filter = "Rice2k secure vaults (*.r2kvault)|*.r2kvault|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true)
            return;

        var password = PromptForPassword(
            "Unlock Secure Vault",
            "Enter the password for this vault. The password is used only to establish the unlocked in-memory session.",
            confirm: false);
        if (password is null)
            return;

        try
        {
            SetBusy(true, "Unlocking vault…", "Authenticating and decrypting the protected vault manifest.");
            _session?.Dispose();
            _session = await _service.UnlockAsync(dialog.FileName, password);
            ShowUnlocked("✓ Vault unlocked", $"{_session.Entries.Count:N0} protected file(s) available.");
        }
        catch (Exception ex)
        {
            _session?.Dispose();
            _session = null;
            ShowError(ex.Message);
            ShowLocked("Vault remains locked");
        }
        finally
        {
            password = string.Empty;
            SetBusy(false);
        }
    }

    private async void AddFiles_Click(object sender, RoutedEventArgs e)
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

        try
        {
            var planned = new List<(string SourcePath, string VaultPath)>();
            var occupied = CurrentOccupiedPaths();
            foreach (var file in dialog.FileNames)
            {
                var proposed = MakeUniqueVaultPath(Path.GetFileName(file), occupied);
                occupied.Add(proposed);
                planned.Add((file, proposed));
            }

            SetBusy(true, "Adding files…", $"Authenticating the current vault, protecting {planned.Count:N0} file(s), then verifying the replacement before it becomes active.");
            await _service.AddFilesAsync(_session!, planned);
            RefreshEntries();
            SetStatus("✓ Files added", $"{planned.Count:N0} file(s) added. Vault sequence is now {_session!.Sequence:N0}.");
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
            SetStatus("⚠ Add operation did not complete", "The previous vault remains the active copy unless a preserved recovery backup is shown above.");
        }
        finally
        {
            SetBusy(false);
            RefreshBackupWarning();
        }
    }

    private async void AddFolder_Click(object sender, RoutedEventArgs e)
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

        try
        {
            SetBusy(true, "Scanning folder…", dialog.FolderName);
            var scan = await Task.Run(() => ScanFolder(dialog.FolderName));
            if (scan.Files.Count == 0)
            {
                SetStatus("Folder contains no files", scan.SkippedFolders > 0 ? $"{scan.SkippedFolders} inaccessible or reparse-point folder(s) were skipped." : "Nothing was added.");
                return;
            }

            var rootName = MakeUniqueVaultRoot(new DirectoryInfo(dialog.FolderName).Name, CurrentOccupiedPaths());
            var planned = scan.Files
                .Select(item => (item.SourcePath, VaultPath: $"{rootName}/{item.RelativePath.Replace('\\', '/')}"))
                .ToArray();

            SetBusy(true, "Protecting folder…", $"Adding {planned.Length:N0} file(s) beneath '{rootName}'. This may take a while for large folders.");
            await _service.AddFilesAsync(_session!, planned);
            RefreshEntries();
            var skipped = scan.SkippedFolders > 0 ? $" {scan.SkippedFolders} inaccessible/reparse folder(s) were skipped." : string.Empty;
            SetStatus("✓ Folder added", $"{planned.Length:N0} file(s) protected.{skipped}");
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
            SetStatus("⚠ Folder add did not complete", "Rice2k did not intentionally replace the last known-good vault with an unverified pending copy.");
        }
        finally
        {
            SetBusy(false);
            RefreshBackupWarning();
        }
    }

    private async void Extract_Click(object sender, RoutedEventArgs e)
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

        try
        {
            SetBusy(true, "Restoring file…", entry.Path);
            await _service.ExtractAsync(_session!, entry.Id, dialog.FileName);
            SetStatus("✓ File restored", dialog.FileName);
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
            SetStatus("⚠ Restore did not complete", "Incomplete temporary output was discarded where possible.");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void Rename_Click(object sender, RoutedEventArgs e)
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

        try
        {
            SetBusy(true, "Renaming entry…", "Rice2k is updating the authenticated encrypted manifest and verifying the replacement vault.");
            await _service.RenameEntryAsync(_session!, entry.Id, newPath);
            RefreshEntries();
            SetStatus("✓ Entry renamed", newPath);
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
            SetStatus("⚠ Rename did not complete", "The previous authenticated vault state remains preferred.");
        }
        finally
        {
            SetBusy(false);
            RefreshBackupWarning();
        }
    }

    private async void Remove_Click(object sender, RoutedEventArgs e)
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

        try
        {
            SetBusy(true, "Removing entry…", entry.Path);
            await _service.RemoveEntryAsync(_session!, entry.Id);
            RefreshEntries();
            SetStatus("✓ Entry removed", "The replacement vault authenticated successfully before the old state was released.");
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
            SetStatus("⚠ Remove did not complete", "Review any recovery-backup warning before making another change.");
        }
        finally
        {
            SetBusy(false);
            RefreshBackupWarning();
        }
    }

    private async void VerifyVault_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureUnlocked() || _busy)
            return;

        try
        {
            SetBusy(true, "Verifying vault…", "Authenticating the manifest and every encrypted file chunk.");
            await _service.VerifyAsync(_session!);
            SetStatus("✓ Vault verification passed", $"{_session!.Entries.Count:N0} file(s) authenticated successfully.");
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
            SetStatus("⚠ Vault requires attention", "Do not add/remove/rename data until the problem is understood. Preserve any .backup file.");
        }
        finally
        {
            SetBusy(false);
            RefreshBackupWarning();
        }
    }

    private void LockVault_Click(object sender, RoutedEventArgs e) => LockVault("Vault locked by user");

    private void LockVault(string reason)
    {
        _session?.Dispose();
        _session = null;
        VaultEntriesGrid.ItemsSource = null;
        SearchBox.Clear();
        ShowLocked(reason);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshEntries();

    private void RefreshEntries()
    {
        if (_session is null)
        {
            VaultEntriesGrid.ItemsSource = null;
            return;
        }

        var query = SearchBox.Text.Trim();
        var entries = string.IsNullOrEmpty(query)
            ? _session.Entries.OrderBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase).ToArray()
            : _session.Entries
                .Where(entry => entry.Path.Contains(query, StringComparison.OrdinalIgnoreCase))
                .OrderBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        VaultEntriesGrid.ItemsSource = entries;
        VaultFooterText.Text = $"Unlocked • {_session.Entries.Count:N0} protected file(s) • sequence {_session.Sequence:N0} • decrypted names exist only in this process while unlocked";
    }

    private void ShowUnlocked(string status, string detail)
    {
        LockedPanel.Visibility = Visibility.Collapsed;
        UnlockedPanel.Visibility = Visibility.Visible;
        VaultLockStatusText.Text = "🔓 Unlocked";
        VaultPathStatusText.Text = _session is null ? "No vault open" : Path.GetFileName(_session.VaultPath);
        _lastActivityUtc = DateTimeOffset.UtcNow;
        RefreshEntries();
        RefreshBackupWarning();
        SetStatus(status, detail);
    }

    private void ShowLocked(string detail)
    {
        LockedPanel.Visibility = Visibility.Visible;
        UnlockedPanel.Visibility = Visibility.Collapsed;
        VaultLockStatusText.Text = "🔒 Locked";
        VaultPathStatusText.Text = "No vault open";
        BackupWarningBorder.Visibility = Visibility.Collapsed;
        VaultFooterText.Text = "Locked • no decrypted filenames or vault content key retained in this window";
        if (VaultOperationText is not null)
            VaultOperationText.Text = "Locked";
        if (VaultDetailText is not null)
            VaultDetailText.Text = detail;
    }

    private void RefreshBackupWarning()
    {
        if (_session is null || !SecureVaultService.HasRecoveryBackup(_session.VaultPath))
        {
            BackupWarningBorder.Visibility = Visibility.Collapsed;
            return;
        }

        BackupWarningBorder.Visibility = Visibility.Visible;
        BackupWarningText.Text = $"A preserved backup exists at {Path.GetFileName(SecureVaultService.RecoveryBackupPath(_session.VaultPath))}. Rice2k blocks further vault mutations until it is reviewed or moved so potential recovery data is not overwritten.";
    }

    private bool EnsureUnlocked()
    {
        if (_session is not null && !_session.IsLocked)
            return true;
        ShowError("Unlock a vault first.");
        return false;
    }

    private void SetBusy(bool busy, string? status = null, string? detail = null)
    {
        _busy = busy;
        VaultBusyProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        if (status is not null)
            VaultOperationText.Text = status;
        if (detail is not null)
            VaultDetailText.Text = detail;
        _lastActivityUtc = DateTimeOffset.UtcNow;
    }

    private void SetStatus(string status, string detail)
    {
        VaultOperationText.Text = status;
        VaultDetailText.Text = detail;
        _lastActivityUtc = DateTimeOffset.UtcNow;
    }

    private void AutoLockTimer_Tick(object? sender, EventArgs e)
    {
        if (_busy || _session is null || AutoLockCheckBox.IsChecked != true)
            return;
        if (DateTimeOffset.UtcNow - _lastActivityUtc >= TimeSpan.FromMinutes(10))
            LockVault("Auto-locked after 10 minutes of inactivity");
    }

    private void Window_UserActivity(object sender, MouseEventArgs e) => _lastActivityUtc = DateTimeOffset.UtcNow;
    private void Window_UserActivity(object sender, KeyEventArgs e) => _lastActivityUtc = DateTimeOffset.UtcNow;

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        _autoLockTimer.Stop();
        _session?.Dispose();
        _session = null;
    }

    private HashSet<string> CurrentOccupiedPaths() =>
        _session is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(_session.Entries.Select(entry => entry.Path), StringComparer.OrdinalIgnoreCase);

    private static string MakeUniqueVaultPath(string proposed, HashSet<string> occupied)
    {
        var normalized = SecureVaultService.NormalizeVaultPath(proposed);
        if (!occupied.Contains(normalized))
            return normalized;

        var directory = Path.GetDirectoryName(normalized.Replace('/', Path.DirectorySeparatorChar))?.Replace('\\', '/') ?? string.Empty;
        var extension = Path.GetExtension(normalized);
        var name = Path.GetFileNameWithoutExtension(normalized);
        for (var index = 2; index < 10000; index++)
        {
            var filename = $"{name} ({index}){extension}";
            var candidate = string.IsNullOrEmpty(directory) ? filename : $"{directory}/{filename}";
            if (!occupied.Contains(candidate))
                return candidate;
        }
        throw new IOException("Rice2k could not create a unique path for this file inside the vault.");
    }

    private static string MakeUniqueVaultRoot(string proposedRoot, HashSet<string> occupied)
    {
        var root = SecureVaultService.NormalizeVaultPath(proposedRoot);
        if (!occupied.Any(path => string.Equals(path, root, StringComparison.OrdinalIgnoreCase) || path.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase)))
            return root;

        for (var index = 2; index < 10000; index++)
        {
            var candidate = $"{root} ({index})";
            if (!occupied.Any(path => string.Equals(path, candidate, StringComparison.OrdinalIgnoreCase) || path.StartsWith(candidate + "/", StringComparison.OrdinalIgnoreCase)))
                return candidate;
        }
        throw new IOException("Rice2k could not create a unique folder name inside the vault.");
    }

    private static FolderScanResult ScanFolder(string rootPath)
    {
        var files = new List<(string SourcePath, string RelativePath)>();
        var skipped = 0;
        var root = new DirectoryInfo(rootPath);
        var pending = new Stack<DirectoryInfo>();
        pending.Push(root);

        while (pending.Count > 0)
        {
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
                        files.Add((file.FullName, Path.GetRelativePath(root.FullName, file.FullName).Replace('\\', '/')));
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
                {
                    skipped++;
                }

                try
                {
                    foreach (var child in current.EnumerateDirectories())
                    {
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

    private string? PromptForPassword(string title, string message, bool confirm)
    {
        var first = new PasswordBox { Margin = new Thickness(0, 6, 0, 10) };
        var second = new PasswordBox { Margin = new Thickness(0, 6, 0, 12), Visibility = confirm ? Visibility.Visible : Visibility.Collapsed };
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 12), Opacity = 0.82 });
        panel.Children.Add(new TextBlock { Text = "Password" });
        panel.Children.Add(first);
        if (confirm)
        {
            panel.Children.Add(new TextBlock { Text = "Confirm password" });
            panel.Children.Add(second);
        }

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var ok = new Button { Content = confirm ? "Create Vault" : "Unlock", IsDefault = true, MinWidth = 110 };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 90, Margin = new Thickness(8, 0, 0, 0) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);

        var dialog = new Window
        {
            Title = title,
            Owner = this,
            Width = 500,
            Height = confirm ? 330 : 280,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = panel
        };

        ok.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(first.Password) || first.Password.Length < 12)
            {
                MessageBox.Show(dialog, "Use a password of at least 12 characters.", "Rice2k Secure Vault", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (confirm && !string.Equals(first.Password, second.Password, StringComparison.Ordinal))
            {
                MessageBox.Show(dialog, "The two passwords do not match.", "Rice2k Secure Vault", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            dialog.DialogResult = true;
        };

        if (dialog.ShowDialog() != true)
            return null;
        var password = first.Password;
        first.Clear();
        second.Clear();
        return password;
    }

    private string? PromptForText(string title, string message, string initialValue)
    {
        var textBox = new TextBox { Text = initialValue, Margin = new Thickness(0, 8, 0, 14) };
        textBox.SelectAll();
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), Opacity = 0.82 });
        panel.Children.Add(textBox);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var ok = new Button { Content = "Save", IsDefault = true, MinWidth = 90 };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 90, Margin = new Thickness(8, 0, 0, 0) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        var dialog = new Window
        {
            Title = title,
            Owner = this,
            Width = 560,
            Height = 250,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = panel
        };
        ok.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(textBox.Text))
            {
                MessageBox.Show(dialog, "Enter a path inside the vault.", "Rice2k Secure Vault", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            dialog.DialogResult = true;
        };
        return dialog.ShowDialog() == true ? textBox.Text : null;
    }

    private void ShowError(string message)
    {
        MessageBox.Show(this, message, "Rice2k Secure Vault", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
