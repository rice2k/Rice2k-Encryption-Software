using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using Microsoft.Win32;
using Rice2k.Encryption.Models;
using Rice2k.Encryption.Services;

namespace Rice2k.Encryption;

public partial class BatchQueueWindow : Window
{
    private readonly ObservableCollection<BatchQueueItem> _items = [];
    private readonly FileEncryptionService _fileCrypto = new();
    private readonly OperationPreflightService _preflight = new();
    private readonly PasswordGeneratorService _passwordGenerator = new();
    private CancellationTokenSource? _batchCts;
    private bool _closeWhenFinished;

    public BatchQueueWindow(IEnumerable<string>? initialFiles = null)
    {
        InitializeComponent();
        BatchQueueGrid.ItemsSource = _items;

        if (initialFiles is not null)
            AddFiles(initialFiles);

        UpdateQueueCount();
    }

    private void AddFiles_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose files to encrypt",
            CheckFileExists = true,
            Multiselect = true
        };

        if (dialog.ShowDialog(this) == true)
            AddFiles(dialog.FileNames);
    }

    private void RemoveSelected_Click(object sender, RoutedEventArgs e)
    {
        if (_batchCts is not null)
            return;

        var selected = BatchQueueGrid.SelectedItems.Cast<BatchQueueItem>().ToList();
        foreach (var item in selected)
            _items.Remove(item);

        UpdateQueueCount();
    }

    private void ClearQueue_Click(object sender, RoutedEventArgs e)
    {
        if (_batchCts is not null)
            return;

        _items.Clear();
        BatchOverallProgress.Value = 0;
        BatchStatusText.Text = "Ready";
        BatchProgressDetails.Text = "Add files to begin.";
        UpdateQueueCount();
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (_batchCts is not null)
            return;

        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
            AddFiles(paths.Where(File.Exists));
    }

    private void AddFiles(IEnumerable<string> paths)
    {
        var skippedEncrypted = 0;
        var added = 0;

        foreach (var path in paths)
        {
            if (!File.Exists(path))
                continue;

            if (path.EndsWith(".r2kenc", StringComparison.OrdinalIgnoreCase))
            {
                skippedEncrypted++;
                continue;
            }

            if (_items.Any(item => string.Equals(item.SourcePath, path, StringComparison.OrdinalIgnoreCase)))
                continue;

            _items.Add(new BatchQueueItem(path));
            added++;
        }

        UpdateQueueCount();

        if (added > 0)
        {
            BatchStatusText.Text = "Ready";
            BatchProgressDetails.Text = $"Added {added} file(s). Review the queue, choose a password, then start the batch.";
        }

        if (skippedEncrypted > 0)
        {
            MessageBox.Show(
                this,
                $"Skipped {skippedEncrypted} existing .r2kenc file(s). This batch window is for encryption; use the Decrypt workflow to restore encrypted files.",
                "Rice2k Batch Queue",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    private void BatchPassword_Changed(object sender, RoutedEventArgs e)
    {
        var password = BatchPasswordBox.Password;
        var confirmation = BatchConfirmPasswordBox.Password;
        var guidance = password.Length switch
        {
            < 12 => "Too short — use at least 12 characters",
            < 16 => "Meets the minimum; a longer password is recommended",
            < 24 => "Good length",
            _ => "Strong length"
        };

        if (!string.IsNullOrEmpty(confirmation))
            guidance += password == confirmation ? "  •  passwords match" : "  •  passwords do not match";

        BatchPasswordStatusText.Text = guidance;
    }

    private void GeneratePassword_Click(object sender, RoutedEventArgs e)
    {
        var password = _passwordGenerator.GeneratePassword(28);
        BatchPasswordBox.Password = password;
        BatchConfirmPasswordBox.Password = password;
        BatchPasswordStatusText.Text = "Strong generated password  •  passwords match";
    }

    private async void StartBatch_Click(object sender, RoutedEventArgs e)
    {
        if (_items.Count == 0)
        {
            ShowFriendlyError("Add at least one file to the queue first.");
            return;
        }

        if (!ValidatePassword())
            return;

        if (_batchCts is not null)
            return;

        foreach (var item in _items)
        {
            item.Status = "Waiting";
            item.Progress = 0;
            item.OutputPath = string.Empty;
        }

        _batchCts = new CancellationTokenSource();
        SetRunning(true);
        BatchOverallProgress.Value = 0;
        BatchStatusText.Text = "Starting batch…";

        var completed = 0;
        var failed = 0;
        var total = _items.Count;

        try
        {
            for (var index = 0; index < total; index++)
            {
                _batchCts.Token.ThrowIfCancellationRequested();
                var item = _items[index];
                var destination = CreateNonCollidingPath(item.SourcePath + ".r2kenc");
                item.OutputPath = destination;
                item.Status = "Checking destination";
                BatchStatusText.Text = $"Checking {item.FileName}";

                try
                {
                    _preflight.Validate(item.SourcePath, destination);
                    item.Status = "Encrypting";

                    var currentIndex = index;
                    var progress = new Progress<CryptoProgress>(p =>
                    {
                        item.Progress = p.Percentage;
                        item.Status = p.Stage;
                        BatchOverallProgress.Value = ((currentIndex + (p.Percentage / 100.0)) / total) * 100.0;

                        var eta = p.EstimatedRemaining is { } remaining
                            ? $" • current file about {FormatDuration(remaining)} remaining"
                            : string.Empty;

                        BatchStatusText.Text = $"{p.Stage}: {item.FileName}";
                        BatchProgressDetails.Text = $"File {currentIndex + 1} of {total} • {p.Percentage:0.0}% • {FormatBytes(p.BytesProcessed)} / {FormatBytes(p.TotalBytes)} • {FormatBytes((long)p.BytesPerSecond)}/s{eta}";
                    });

                    await _fileCrypto.EncryptFileAsync(
                        item.SourcePath,
                        destination,
                        BatchPasswordBox.Password,
                        progress,
                        _batchCts.Token,
                        BatchVerifyCheck.IsChecked != false);

                    item.Progress = 100;
                    item.Status = "✓ Complete";
                    completed++;
                    BatchOverallProgress.Value = ((index + 1.0) / total) * 100.0;
                }
                catch (OperationCanceledException)
                {
                    item.Status = "Cancelled";
                    throw;
                }
                catch (Exception ex)
                {
                    item.Status = $"⚠ Failed: {ShortError(ex)}";
                    item.Progress = 0;
                    failed++;
                    BatchOverallProgress.Value = ((index + 1.0) / total) * 100.0;
                    BatchProgressDetails.Text = $"{item.FileName} failed safely. Continuing with the remaining queue.";
                }
            }

            BatchOverallProgress.Value = 100;
            BatchStatusText.Text = failed == 0
                ? "✓ Batch complete"
                : "Batch complete with some files needing attention";
            BatchProgressDetails.Text = $"{completed} completed • {failed} failed • {total} total. Successful encrypted outputs were kept.";
        }
        catch (OperationCanceledException)
        {
            BatchStatusText.Text = "Batch cancelled safely";
            BatchProgressDetails.Text = $"{completed} completed • {failed} failed. The active file's incomplete temporary output was removed; completed outputs were kept.";
        }
        finally
        {
            _batchCts.Dispose();
            _batchCts = null;
            SetRunning(false);
            BatchPasswordBox.Clear();
            BatchConfirmPasswordBox.Clear();

            if (_closeWhenFinished)
            {
                _closeWhenFinished = false;
                Dispatcher.InvokeAsync(Close);
            }
        }
    }

    private void CancelBatch_Click(object sender, RoutedEventArgs e)
    {
        if (_batchCts is null)
            return;

        var result = MessageBox.Show(
            this,
            "Stop the batch?\n\nCompleted encrypted files will be kept. Rice2k will remove the incomplete temporary output for the file currently being processed.",
            "Stop batch?",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            BatchStatusText.Text = "Cancelling safely…";
            _batchCts.Cancel();
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_batchCts is null)
            return;

        var result = MessageBox.Show(
            this,
            "A batch is still running. Stop it and close after the active file is cleaned up?",
            "Batch still running",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        e.Cancel = true;
        if (result == MessageBoxResult.Yes)
        {
            _closeWhenFinished = true;
            BatchStatusText.Text = "Cancelling safely before closing…";
            _batchCts.Cancel();
        }
    }

    private bool ValidatePassword()
    {
        if (string.IsNullOrWhiteSpace(BatchPasswordBox.Password) || BatchPasswordBox.Password.Length < 12)
        {
            ShowFriendlyError("Use a password of at least 12 characters. A longer generated password is recommended.");
            return false;
        }

        if (!string.Equals(BatchPasswordBox.Password, BatchConfirmPasswordBox.Password, StringComparison.Ordinal))
        {
            ShowFriendlyError("The two passwords do not match. Re-enter the confirmation before starting the batch.");
            return false;
        }

        return true;
    }

    private void SetRunning(bool running)
    {
        StartBatchButton.IsEnabled = !running && _items.Count > 0;
        CancelBatchButton.IsEnabled = running;
        AddFilesButton.IsEnabled = !running;
        RemoveSelectedButton.IsEnabled = !running;
        ClearQueueButton.IsEnabled = !running;
        BatchQueueGrid.IsEnabled = !running;
    }

    private void UpdateQueueCount()
    {
        var totalBytes = _items.Sum(item => item.SizeBytes);
        QueueCountText.Text = $"{_items.Count} file(s) in queue  •  {FormatBytes(totalBytes)} total";
        StartBatchButton.IsEnabled = _batchCts is null && _items.Count > 0;
    }

    private void ShowFriendlyError(string message)
    {
        MessageBox.Show(
            this,
            message,
            "Rice2k Batch Encryption",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private static string ShortError(Exception ex)
    {
        var text = ex.Message.Replace(Environment.NewLine, " ").Trim();
        return text.Length <= 72 ? text : text[..69] + "…";
    }

    private static string CreateNonCollidingPath(string path)
    {
        if (!File.Exists(path))
            return path;

        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        var extension = Path.GetExtension(path);
        var name = Path.GetFileNameWithoutExtension(path);
        var index = 2;
        string candidate;

        do
        {
            candidate = Path.Combine(directory, $"{name} ({index}){extension}");
            index++;
        }
        while (File.Exists(candidate));

        return candidate;
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

    private static string FormatDuration(TimeSpan value)
    {
        if (value.TotalHours >= 1)
            return $"{(int)value.TotalHours}h {value.Minutes}m";
        if (value.TotalMinutes >= 1)
            return $"{(int)value.TotalMinutes}m {value.Seconds}s";
        return $"{Math.Max(0, (int)Math.Ceiling(value.TotalSeconds))}s";
    }
}
