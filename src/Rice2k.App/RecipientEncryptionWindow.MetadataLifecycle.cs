using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using Rice2k.Encryption.Models;

namespace Rice2k.Encryption;

public partial class RecipientEncryptionWindow
{
    private bool _metadataLifecycleInitialized;
    private Button? _addRecipientButton;
    private Button? _inspectRecipientFileButton;

    private void InitializeMetadataLifecycleUi()
    {
        if (_metadataLifecycleInitialized)
            return;

        _addRecipientButton = FindRecipientVisualChildren<Button>(this)
            .FirstOrDefault(button => string.Equals(button.Content?.ToString(), "Add Public Identity…", StringComparison.Ordinal));

        _inspectRecipientFileButton = FindRecipientVisualChildren<Button>(this)
            .FirstOrDefault(button =>
                string.Equals(button.Content?.ToString(), "Browse…", StringComparison.Ordinal) &&
                button.Parent is Grid grid &&
                grid.Children.Contains(DecryptSourceBox));

        if (_addRecipientButton is not null)
        {
            _addRecipientButton.Click -= AddRecipient_Click;
            _addRecipientButton.Click += AddRecipientSafe_Click;
        }

        if (_inspectRecipientFileButton is not null)
        {
            _inspectRecipientFileButton.Click -= BrowseDecryptSource_Click;
            _inspectRecipientFileButton.Click += BrowseDecryptSourceSafe_Click;
        }

        _metadataLifecycleInitialized = true;
    }

    private async void AddRecipientSafe_Click(object sender, RoutedEventArgs e)
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

        BeginMetadataOperation();
        var added = 0;
        var skipped = 0;
        try
        {
            foreach (var path in dialog.FileNames)
            {
                _cts!.Token.ThrowIfCancellationRequested();
                try
                {
                    var identity = await _identityService.ImportPublicAsync(path, _cts.Token);
                    if (_recipients.Any(existing => existing.Id == identity.Id || existing.Fingerprint == identity.Fingerprint))
                    {
                        skipped++;
                        continue;
                    }

                    _recipients.Add(identity);
                    added++;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    skipped++;
                    ShowError($"Could not import {Path.GetFileName(path)}:\n\n{ex.Message}");
                }
            }

            EncryptStatusText.Text = $"{_recipients.Count:N0} recipient(s) selected";
            EncryptProgressDetailText.Text = $"Added {added:N0}" +
                (skipped > 0 ? $" • skipped {skipped:N0}" : string.Empty) +
                ". Compare fingerprints before encrypting if recipient identity matters.";
        }
        catch (OperationCanceledException)
        {
            EncryptStatusText.Text = "Recipient import cancelled";
            EncryptProgressDetailText.Text = $"Added {added:N0}" +
                (skipped > 0 ? $" • skipped {skipped:N0}" : string.Empty) +
                ". No private key material was involved.";
        }
        finally
        {
            EndMetadataOperation();
        }
    }

    private async void BrowseDecryptSourceSafe_Click(object sender, RoutedEventArgs e)
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

        BeginMetadataOperation();
        try
        {
            DecryptStatusText.Text = "Inspecting recipient container…";
            var info = await _recipientService.InspectAsync(dialog.FileName, _cts!.Token);
            _cts.Token.ThrowIfCancellationRequested();
            DecryptSourceBox.Text = dialog.FileName;
            RecipientCountText.Text = $"R2KENC03 • encrypted for {info.RecipientCount:N0} recipient(s) • container {FormatBytes(info.ContainerLength)}";
            DecryptDestinationBox.Text = SuggestRestoredPath(dialog.FileName);
            DecryptStatusText.Text = "Ready";
        }
        catch (OperationCanceledException)
        {
            DecryptStatusText.Text = "Container inspection cancelled";
        }
        catch (Exception ex)
        {
            DecryptStatusText.Text = "⚠ Container inspection failed";
            ShowError(ex.Message);
        }
        finally
        {
            EndMetadataOperation();
        }
    }

    private void BeginMetadataOperation()
    {
        _busy = true;
        _cts = new CancellationTokenSource();
        EncryptStartButton.IsEnabled = false;
        DecryptStartButton.IsEnabled = false;
        if (_addRecipientButton is not null)
            _addRecipientButton.IsEnabled = false;
        if (_inspectRecipientFileButton is not null)
            _inspectRecipientFileButton.IsEnabled = false;
    }

    private void EndMetadataOperation()
    {
        _cts?.Dispose();
        _cts = null;
        _busy = false;
        EncryptStartButton.IsEnabled = true;
        DecryptStartButton.IsEnabled = true;
        if (_addRecipientButton is not null)
            _addRecipientButton.IsEnabled = true;
        if (_inspectRecipientFileButton is not null)
            _inspectRecipientFileButton.IsEnabled = true;

        if (_closeWhenFinished)
        {
            _closeWhenFinished = false;
            Dispatcher.BeginInvoke(new Action(Close));
        }
    }

    private static IEnumerable<T> FindRecipientVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is null)
            yield break;

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
                yield return match;
            foreach (var descendant in FindRecipientVisualChildren<T>(child))
                yield return descendant;
        }
    }
}
