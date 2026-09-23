using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;
using Rice2k.Encryption.Models;
using Rice2k.Encryption.Services;

namespace Rice2k.Encryption;

public partial class FolderProtectionWindow : Window
{
    private readonly FolderProtectionService _service = new();
    private CancellationTokenSource? _cts;
    private Stopwatch? _stageTimer;
    private string? _stage;
    private bool _busy;
    private bool _closeWhenFinished;
    private string? _completedPath;

    public FolderProtectionWindow()
    {
        InitializeComponent();
    }

    private void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
            return;

        var dialog = new OpenFolderDialog
        {
            Title = "Choose a folder to protect",
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true)
            return;

        SourceFolderBox.Text = Path.GetFullPath(dialog.FolderName);
        DestinationBox.Text = SuggestDestination(SourceFolderBox.Text);
        FolderSummaryText.Text = "Folder selected. Rice2k will scan it safely before creating the encrypted container.";
        StatusText.Text = "Ready";
        ProgressDetailText.Text = "Enter a password and choose Protect Folder.";
        OpenFolderButton.Visibility = Visibility.Collapsed;
        _completedPath = null;
    }

    private void BrowseDestination_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
            return;
        if (!Directory.Exists(SourceFolderBox.Text))
        {
            ShowError("Choose the folder you want to protect first.");
            return;
        }

        var suggested = string.IsNullOrWhiteSpace(DestinationBox.Text)
            ? SuggestDestination(SourceFolderBox.Text)
            : DestinationBox.Text;

        var dialog = new SaveFileDialog
        {
            Title = "Save protected folder as a Rice2k secure container",
            Filter = "Rice2k secure vaults (*.r2kvault)|*.r2kvault",
            DefaultExt = ".r2kvault",
            AddExtension = true,
            OverwritePrompt = false,
            FileName = Path.GetFileName(suggested),
            InitialDirectory = Path.GetDirectoryName(suggested)
        };

        if (dialog.ShowDialog(this) == true)
            DestinationBox.Text = CreateNonCollidingPath(dialog.FileName);
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
            return;

        if (!Directory.Exists(SourceFolderBox.Text))
        {
            ShowError("Choose an existing folder to protect.");
            return;
        }
        if (string.IsNullOrWhiteSpace(DestinationBox.Text))
        {
            ShowError("Choose where to save the protected folder container.");
            return;
        }
        if (PasswordBox.Password.Length < 12)
        {
            ShowError("Use a vault password containing at least 12 characters.");
            return;
        }
        if (!string.Equals(PasswordBox.Password, ConfirmPasswordBox.Password, StringComparison.Ordinal))
        {
            ShowError("The vault password and confirmation do not match.");
            return;
        }

        // Capture once before the first await, then clear the visible password fields.
        // This prevents later workflow stages from depending on mutable UI state and
        // reduces how long the secret remains visible in WPF control storage.
        var password = PasswordBox.Password;
        PasswordBox.Clear();
        ConfirmPasswordBox.Clear();

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        SetBusy(true);
        _completedPath = null;
        OpenFolderButton.Visibility = Visibility.Collapsed;

        try
        {
            var progress = new Progress<VaultOperationProgress>(UpdateProgress);
            StatusText.Text = "Scanning folder…";
            ProgressBar.IsIndeterminate = true;
            ProgressDetailText.Text = "Discovering regular files without following reparse points or junctions.";

            var plan = await _service.PrepareAsync(
                SourceFolderBox.Text,
                DestinationBox.Text,
                progress,
                token);

            var skipped = plan.SkippedFiles + plan.SkippedFolders;
            FolderSummaryText.Text =
                $"{plan.Files.Count:N0} file(s) • {FormatBytes(plan.TotalBytes)}" +
                (skipped > 0 ? $" • {skipped:N0} inaccessible/reparse item(s) skipped" : string.Empty);

            _stage = null;
            _stageTimer = Stopwatch.StartNew();
            await _service.ProtectAsync(plan, password, progress, token);

            token.ThrowIfCancellationRequested();
            _completedPath = plan.DestinationPath;
            ProgressBar.IsIndeterminate = false;
            ProgressBar.Value = 100;
            StatusText.Text = "✓ Folder protected and verified";
            ProgressDetailText.Text =
                $"Saved {plan.Files.Count:N0} file(s) in {Path.GetFileName(plan.DestinationPath)}. " +
                "The original folder was not changed.";
            OpenFolderButton.Visibility = Visibility.Visible;
        }
        catch (OperationCanceledException)
        {
            ProgressBar.IsIndeterminate = false;
            ProgressBar.Value = 0;
            StatusText.Text = "Operation cancelled safely";
            ProgressDetailText.Text = "The source folder was not changed. Incomplete temporary output was discarded where possible.";
        }
        catch (Exception ex)
        {
            ProgressBar.IsIndeterminate = false;
            ProgressBar.Value = 0;
            StatusText.Text = "⚠ Folder protection did not complete";
            ProgressDetailText.Text = "The source folder was not changed. Review the message and try again.";
            ShowError(ex.Message);
        }
        finally
        {
            password = string.Empty;
            PasswordBox.Clear();
            ConfirmPasswordBox.Clear();
            SetBusy(false);
            _cts?.Dispose();
            _cts = null;
            _stageTimer = null;
            _stage = null;

            if (_closeWhenFinished)
            {
                _closeWhenFinished = false;
                Dispatcher.InvokeAsync(Close);
            }
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (_cts is null || _cts.IsCancellationRequested)
            return;

        CancelButton.IsEnabled = false;
        StatusText.Text = "Cancelling safely…";
        ProgressDetailText.Text = "Rice2k will stop at the next safe boundary and preserve the source folder.";
        _cts.Cancel();
    }

    private void OpenOutputFolder_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_completedPath) || !File.Exists(_completedPath))
            return;

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{_completedPath}\"",
            UseShellExecute = true
        });
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_busy)
            return;

        e.Cancel = true;
        _closeWhenFinished = true;

        if (_cts is not null && !_cts.IsCancellationRequested)
        {
            CancelButton.IsEnabled = false;
            StatusText.Text = "Cancelling before close…";
            ProgressDetailText.Text = "The window will close automatically after the operation reaches a safe boundary.";
            _cts.Cancel();
        }
        else
        {
            StatusText.Text = "Finishing cancellation before close…";
            ProgressDetailText.Text = "The window will close automatically when cleanup is complete.";
        }
    }

    private void UpdateProgress(VaultOperationProgress value)
    {
        if (!string.Equals(_stage, value.Stage, StringComparison.Ordinal))
        {
            _stage = value.Stage;
            _stageTimer = Stopwatch.StartNew();
        }

        StatusText.Text = value.Stage;
        if (!value.IsDeterminate)
        {
            ProgressBar.IsIndeterminate = true;
            var count = value.ItemsProcessed > 0 ? $"{value.ItemsProcessed:N0} file(s) found" : "Working…";
            ProgressDetailText.Text = string.IsNullOrWhiteSpace(value.CurrentItem)
                ? count
                : $"{count} • {value.CurrentItem}";
            return;
        }

        ProgressBar.IsIndeterminate = false;
        ProgressBar.Value = value.Percentage;
        var elapsed = _stageTimer?.Elapsed ?? TimeSpan.Zero;
        var detail = $"{value.Percentage:0}%";

        if (value.TotalBytes > 0)
        {
            detail += $" • {FormatBytes(value.BytesProcessed)} of {FormatBytes(value.TotalBytes)}";
            if (elapsed.TotalSeconds >= 0.5 && value.BytesProcessed > 0)
            {
                var rate = value.BytesProcessed / Math.Max(elapsed.TotalSeconds, 0.001);
                detail += $" • {FormatBytes((long)rate)}/s";
                if (value.BytesProcessed < value.TotalBytes && rate > 0)
                {
                    var remaining = TimeSpan.FromSeconds((value.TotalBytes - value.BytesProcessed) / rate);
                    detail += $" • about {FormatDuration(remaining)} remaining";
                }
            }
        }
        else if (value.TotalItems > 0)
        {
            detail += $" • {value.ItemsProcessed:N0} of {value.TotalItems:N0} item(s)";
        }

        if (elapsed > TimeSpan.Zero)
            detail += $" • elapsed {FormatDuration(elapsed)}";
        if (!string.IsNullOrWhiteSpace(value.CurrentItem))
            detail += $" • {value.CurrentItem}";
        ProgressDetailText.Text = detail;
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        StartButton.IsEnabled = !busy;
        CancelButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        CancelButton.IsEnabled = busy;
    }

    private static string SuggestDestination(string sourceFolder)
    {
        var info = new DirectoryInfo(sourceFolder);
        var parent = info.Parent?.FullName ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var name = string.IsNullOrWhiteSpace(info.Name) ? "Protected Folder" : info.Name;
        return CreateNonCollidingPath(Path.Combine(parent, name + ".r2kvault"));
    }

    private static string CreateNonCollidingPath(string path)
    {
        if (!File.Exists(path))
            return path;

        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        var extension = Path.GetExtension(path);
        var name = Path.GetFileNameWithoutExtension(path);
        for (var index = 2; index < 10_000; index++)
        {
            var candidate = Path.Combine(directory, $"{name} ({index}){extension}");
            if (!File.Exists(candidate))
                return candidate;
        }
        throw new IOException("Rice2k could not create a unique output filename. Choose another destination.");
    }

    private static string FormatDuration(TimeSpan value)
    {
        if (value.TotalHours >= 1)
            return $"{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}";
        if (value.TotalMinutes >= 1)
            return $"{(int)value.TotalMinutes}:{value.Seconds:00}";
        return $"{Math.Max(0, (int)Math.Ceiling(value.TotalSeconds))} sec";
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

    private void ShowError(string message) =>
        MessageBox.Show(this, message, "Rice2k — Protect Folder", MessageBoxButton.OK, MessageBoxImage.Warning);
}
