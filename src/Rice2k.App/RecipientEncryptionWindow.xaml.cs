using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using Microsoft.Win32;
using Rice2k.Encryption.Models;
using Rice2k.Encryption.Services;

namespace Rice2k.Encryption;

public partial class RecipientEncryptionWindow : Window
{
    private readonly IdentityService _identityService = new();
    private readonly RecipientFileEncryptionService _recipientService = new();
    private readonly ObservableCollection<Rice2kPublicIdentity> _recipients = [];
    private CancellationTokenSource? _cts;
    private bool _busy;
    private bool _closeWhenFinished;

    public RecipientEncryptionWindow()
    {
        InitializeComponent();
        RecipientsList.ItemsSource = _recipients;
    }

    private void BrowseEncryptSource_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
            return;
        var dialog = new OpenFileDialog { Title = "Choose a file to encrypt for recipients", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true)
            return;

        EncryptSourceBox.Text = dialog.FileName;
        EncryptDestinationBox.Text = CreateNonCollidingPath(dialog.FileName + ".r2kenc");
    }

    private void BrowseEncryptDestination_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || !File.Exists(EncryptSourceBox.Text))
            return;
        var dialog = new SaveFileDialog
        {
            Title = "Save recipient-encrypted file",
            Filter = "Rice2k encrypted files (*.r2kenc)|*.r2kenc",
            DefaultExt = ".r2kenc",
            AddExtension = true,
            OverwritePrompt = false,
            FileName = Path.GetFileName(EncryptDestinationBox.Text)
        };
        if (dialog.ShowDialog(this) == true)
            EncryptDestinationBox.Text = CreateNonCollidingPath(dialog.FileName);
    }

    private async void AddRecipient_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
            return;
        var dialog = new OpenFileDialog
        {
            Title = "Add Rice2k public recipient identities",
            Filter = "Rice2k public identities (*.r2kpub)|*.r2kpub|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = true
        };
        if (dialog.ShowDialog(this) != true)
            return;

        var added = 0;
        var skipped = 0;
        foreach (var path in dialog.FileNames)
        {
            try
            {
                var identity = await _identityService.ImportPublicAsync(path);
                if (_recipients.Any(existing => existing.Id == identity.Id || existing.Fingerprint == identity.Fingerprint))
                {
                    skipped++;
                    continue;
                }
                _recipients.Add(identity);
                added++;
            }
            catch (Exception ex)
            {
                skipped++;
                ShowError($"Could not import {Path.GetFileName(path)}:\n\n{ex.Message}");
            }
        }

        EncryptStatusText.Text = $"{_recipients.Count:N0} recipient(s) selected";
        EncryptProgressDetailText.Text = $"Added {added:N0}" + (skipped > 0 ? $" • skipped {skipped:N0}" : string.Empty) + ". Compare fingerprints before encrypting if recipient identity matters.";
    }

    private void RemoveRecipient_Click(object sender, RoutedEventArgs e)
    {
        if (!_busy && RecipientsList.SelectedItem is Rice2kPublicIdentity identity)
            _recipients.Remove(identity);
    }

    private void CopyRecipientFingerprint_Click(object sender, RoutedEventArgs e)
    {
        if (RecipientsList.SelectedItem is not Rice2kPublicIdentity identity)
        {
            ShowError("Select a recipient first.");
            return;
        }
        Clipboard.SetText(identity.Fingerprint);
        EncryptProgressDetailText.Text = "Fingerprint copied. Compare it through a trusted independent channel before treating the recipient label as verified.";
    }

    private async void EncryptForRecipients_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
            return;
        if (!File.Exists(EncryptSourceBox.Text))
        {
            ShowError("Choose a file to encrypt first.");
            return;
        }
        if (_recipients.Count == 0)
        {
            ShowError("Add at least one recipient public identity first.");
            return;
        }
        if (string.IsNullOrWhiteSpace(EncryptDestinationBox.Text))
        {
            ShowError("Choose an encrypted output location.");
            return;
        }

        BeginOperation(encryptMode: true);
        var progress = new Progress<CryptoProgress>(value =>
            UpdateProgress(value, EncryptStatusText, EncryptProgressBar, EncryptProgressDetailText));

        try
        {
            await _recipientService.EncryptForRecipientsAsync(
                EncryptSourceBox.Text,
                EncryptDestinationBox.Text,
                _recipients.ToArray(),
                progress,
                _cts!.Token,
                verifyAfterEncrypt: true);
            EncryptStatusText.Text = "✓ Recipient encryption complete";
            EncryptProgressBar.Value = 100;
            EncryptProgressDetailText.Text = $"Encrypted for {_recipients.Count:N0} recipient(s). The sender is not authenticated by recipient encryption alone; attach a Rice2k digital signature if that matters.";
        }
        catch (OperationCanceledException)
        {
            EncryptStatusText.Text = "Encryption cancelled safely";
            EncryptProgressDetailText.Text = "Incomplete temporary output was discarded where possible. The source file was not changed.";
        }
        catch (Exception ex)
        {
            EncryptStatusText.Text = "⚠ Recipient encryption did not complete";
            ShowError(ex.Message);
        }
        finally
        {
            EndOperation();
        }
    }

    private async void BrowseDecryptSource_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
            return;
        var dialog = new OpenFileDialog
        {
            Title = "Choose a Rice2k recipient-encrypted file",
            Filter = "Rice2k encrypted files (*.r2kenc)|*.r2kenc|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            var info = await _recipientService.InspectAsync(dialog.FileName);
            DecryptSourceBox.Text = dialog.FileName;
            RecipientCountText.Text = $"R2KENC03 • encrypted for {info.RecipientCount:N0} recipient(s) • container {FormatBytes(info.ContainerLength)}";
            DecryptDestinationBox.Text = SuggestRestoredPath(dialog.FileName);
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private void BrowsePrivateIdentity_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
            return;
        var dialog = new OpenFileDialog
        {
            Title = "Choose your Rice2k private identity",
            Filter = "Rice2k private identities (*.r2kid)|*.r2kid|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) == true)
            PrivateIdentityBox.Text = dialog.FileName;
    }

    private void BrowseDecryptDestination_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || !File.Exists(DecryptSourceBox.Text))
            return;
        var suggested = string.IsNullOrWhiteSpace(DecryptDestinationBox.Text)
            ? SuggestRestoredPath(DecryptSourceBox.Text)
            : DecryptDestinationBox.Text;
        var dialog = new SaveFileDialog
        {
            Title = "Save restored recipient file",
            FileName = Path.GetFileName(suggested),
            OverwritePrompt = false
        };
        if (dialog.ShowDialog(this) == true)
            DecryptDestinationBox.Text = CreateNonCollidingPath(dialog.FileName);
    }

    private async void DecryptRecipientFile_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
            return;
        if (!File.Exists(DecryptSourceBox.Text))
        {
            ShowError("Choose an R2KENC03 recipient-encrypted file first.");
            return;
        }
        if (!File.Exists(PrivateIdentityBox.Text))
        {
            ShowError("Choose your encrypted .r2kid private identity package.");
            return;
        }
        if (string.IsNullOrWhiteSpace(PrivateIdentityPasswordBox.Password))
        {
            ShowError("Enter the private identity package password.");
            return;
        }
        if (string.IsNullOrWhiteSpace(DecryptDestinationBox.Text))
        {
            ShowError("Choose where to restore the decrypted file.");
            return;
        }

        var privateIdentityPassword = PrivateIdentityPasswordBox.Password;
        PrivateIdentityPasswordBox.Clear();
        BeginOperation(encryptMode: false);
        Rice2kIdentity? identity = null;
        var progress = new Progress<CryptoProgress>(value =>
            UpdateProgress(value, DecryptStatusText, DecryptProgressBar, DecryptProgressDetailText));

        try
        {
            DecryptStatusText.Text = "Unlocking private identity…";
            identity = await _identityService.ImportPrivateAsync(
                PrivateIdentityBox.Text,
                privateIdentityPassword,
                _cts!.Token);

            await _recipientService.DecryptAsync(
                DecryptSourceBox.Text,
                DecryptDestinationBox.Text,
                identity,
                progress,
                _cts.Token);

            DecryptStatusText.Text = "✓ Recipient file restored";
            DecryptProgressBar.Value = 100;
            DecryptProgressDetailText.Text = $"Unlocked with {identity.Name} • {identity.Fingerprint}. Authentication checks passed before the restored file was finalized.";
        }
        catch (OperationCanceledException)
        {
            DecryptStatusText.Text = "Decryption cancelled safely";
            DecryptProgressDetailText.Text = "Incomplete temporary output was discarded where possible.";
        }
        catch (Exception ex)
        {
            DecryptStatusText.Text = "⚠ Recipient file was not restored";
            ShowError(ex.Message);
        }
        finally
        {
            identity?.Dispose();
            privateIdentityPassword = string.Empty;
            PrivateIdentityPasswordBox.Clear();
            EndOperation();
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (_cts is null || _cts.IsCancellationRequested)
            return;
        EncryptCancelButton.IsEnabled = false;
        DecryptCancelButton.IsEnabled = false;
        _cts.Cancel();
    }

    private void BeginOperation(bool encryptMode)
    {
        _busy = true;
        _cts = new CancellationTokenSource();
        EncryptStartButton.IsEnabled = false;
        DecryptStartButton.IsEnabled = false;
        EncryptCancelButton.Visibility = encryptMode ? Visibility.Visible : Visibility.Collapsed;
        DecryptCancelButton.Visibility = encryptMode ? Visibility.Collapsed : Visibility.Visible;
        EncryptCancelButton.IsEnabled = encryptMode;
        DecryptCancelButton.IsEnabled = !encryptMode;
    }

    private void EndOperation()
    {
        _busy = false;
        EncryptStartButton.IsEnabled = true;
        DecryptStartButton.IsEnabled = true;
        EncryptCancelButton.Visibility = Visibility.Collapsed;
        DecryptCancelButton.Visibility = Visibility.Collapsed;
        _cts?.Dispose();
        _cts = null;

        if (_closeWhenFinished)
        {
            _closeWhenFinished = false;
            Dispatcher.BeginInvoke(new Action(Close));
        }
    }

    private static void UpdateProgress(
        CryptoProgress value,
        System.Windows.Controls.TextBlock status,
        System.Windows.Controls.ProgressBar bar,
        System.Windows.Controls.TextBlock details)
    {
        status.Text = value.Stage;
        bar.Value = value.Percentage;
        var text = $"{value.Percentage:0}% • {FormatBytes(value.BytesProcessed)} of {FormatBytes(value.TotalBytes)}";
        if (value.BytesPerSecond > 0)
            text += $" • {FormatBytes((long)value.BytesPerSecond)}/s";
        text += $" • elapsed {FormatDuration(value.Elapsed)}";
        if (value.EstimatedRemaining is TimeSpan remaining)
            text += $" • about {FormatDuration(remaining)} remaining";
        details.Text = text;
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_busy)
            return;

        e.Cancel = true;
        _closeWhenFinished = true;
        EncryptCancelButton.IsEnabled = false;
        DecryptCancelButton.IsEnabled = false;
        if (_cts is not null && !_cts.IsCancellationRequested)
            _cts.Cancel();
    }

    private static string SuggestRestoredPath(string encryptedPath)
    {
        var directory = Path.GetDirectoryName(encryptedPath) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(encryptedPath);
        if (string.IsNullOrWhiteSpace(name))
            name = "restored-file";
        return CreateNonCollidingPath(Path.Combine(directory, name));
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
        throw new IOException("Rice2k could not create a unique output filename.");
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
            return $"{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}";
        if (value.TotalMinutes >= 1)
            return $"{(int)value.TotalMinutes}:{value.Seconds:00}";
        return $"{Math.Max(0, (int)Math.Ceiling(value.TotalSeconds))} sec";
    }

    private void ShowError(string message) =>
        MessageBox.Show(this, message, "Rice2k Recipient Encryption", MessageBoxButton.OK, MessageBoxImage.Warning);
}
