using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;
using Rice2k.Encryption.Models;
using Rice2k.Encryption.Services;

namespace Rice2k.Encryption;

public partial class PublicIdentityContactsWindow : Window
{
    private readonly PublicIdentityContactStore _store = new();
    private readonly ProtectedClipboardService _clipboard = new();
    private readonly ObservableCollection<Rice2kPublicIdentity> _contacts = [];
    private CancellationTokenSource? _operationCts;
    private bool _busy;
    private bool _closeWhenFinished;

    public PublicIdentityContactsWindow()
    {
        InitializeComponent();
        ContactsList.ItemsSource = _contacts;
        Closing += Window_Closing;
    }

    public IReadOnlyList<Rice2kPublicIdentity> SelectedContacts { get; private set; } = Array.Empty<Rice2kPublicIdentity>();

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (_busy)
            return;

        var token = BeginOperation("Loading validated contacts…");
        try
        {
            await ReloadAsync(token);
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Contact loading cancelled";
            DetailText.Text = "No saved contact was changed.";
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
            StatusText.Text = "⚠ Contacts could not be loaded";
        }
        finally
        {
            EndOperation();
        }
    }

    private async Task ReloadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var contacts = await _store.LoadAllAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        _contacts.Clear();
        foreach (var contact in contacts)
            _contacts.Add(contact);

        StatusText.Text = $"{_contacts.Count:N0} saved public contact(s)";
        DetailText.Text = _contacts.Count == 0
            ? "Import a .r2kpub public identity card to save a reusable contact."
            : "Compare fingerprints independently before relying on a contact label for identity.";
    }

    private async void ImportContact_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
            return;

        var dialog = new OpenFileDialog
        {
            Title = "Import Rice2k public identity contact",
            Filter = "Rice2k public identities (*.r2kpub)|*.r2kpub|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = true
        };
        if (dialog.ShowDialog(this) != true)
            return;

        var token = BeginOperation("Importing validated public contacts…");
        var added = 0;
        var skipped = 0;
        try
        {
            foreach (var path in dialog.FileNames)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    await _store.ImportAsync(path, token);
                    added++;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    skipped++;
                    ShowError($"Could not save {Path.GetFileName(path)}:\n\n{ex.Message}");
                }
            }

            await ReloadAsync(token);
            DetailText.Text = $"Imported {added:N0}" + (skipped > 0 ? $" • skipped {skipped:N0}" : string.Empty) + ". Stored contacts remain public-key material only.";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Contact import cancelled safely";
            DetailText.Text = $"Imported {added:N0}" + (skipped > 0 ? $" • skipped {skipped:N0}" : string.Empty) + ". Incomplete temporary contact copies were discarded where possible.";
        }
        finally
        {
            EndOperation();
        }
    }

    private async void RemoveContact_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
            return;

        if (ContactsList.SelectedItem is not Rice2kPublicIdentity identity)
        {
            ShowError("Select a saved contact first.");
            return;
        }

        var result = MessageBox.Show(
            this,
            $"Remove this saved public contact?\n\n{identity.Name}\n{identity.Fingerprint}\n\nThis removes only the local public contact card. It cannot remove copies shared elsewhere.",
            "Remove saved contact?",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
            return;

        var token = BeginOperation("Removing saved public contact…");
        try
        {
            await _store.RemoveAsync(identity, token);
            await ReloadAsync(token);
            StatusText.Text = "✓ Saved contact removed";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Contact removal cancelled";
            DetailText.Text = "Rice2k stopped before the next destructive boundary where possible.";
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
            StatusText.Text = "⚠ Saved contact was not removed";
        }
        finally
        {
            EndOperation();
        }
    }

    private void CopyFingerprint_Click(object sender, RoutedEventArgs e)
    {
        if (ContactsList.SelectedItem is not Rice2kPublicIdentity identity)
        {
            ShowError("Select a saved contact first.");
            return;
        }

        try
        {
            _clipboard.CopyText(identity.Fingerprint, message =>
            {
                StatusText.Text = message;
                DetailText.Text = "Compare the fingerprint through an independent trusted channel before treating the contact label as verified.";
            });
        }
        catch (Exception ex)
        {
            ShowError($"Windows could not access the clipboard. {ex.Message}");
        }
    }

    private void UseSelected_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            StatusText.Text = "Wait for the current contact operation to finish.";
            return;
        }

        var selected = ContactsList.SelectedItems.Cast<Rice2kPublicIdentity>().ToArray();
        if (selected.Length == 0)
        {
            ShowError("Select at least one contact to use as a recipient.");
            return;
        }

        SelectedContacts = selected;
        DialogResult = true;
    }

    private void OpenContactsFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_store.ContactsDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = _store.ContactsDirectory,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private CancellationToken BeginOperation(string status)
    {
        _operationCts?.Dispose();
        _operationCts = new CancellationTokenSource();
        _busy = true;
        ContactsList.IsEnabled = false;
        StatusText.Text = status;
        return _operationCts.Token;
    }

    private void EndOperation()
    {
        _operationCts?.Dispose();
        _operationCts = null;
        _busy = false;
        ContactsList.IsEnabled = true;

        if (_closeWhenFinished)
        {
            _closeWhenFinished = false;
            Dispatcher.BeginInvoke(new Action(Close));
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_busy)
            return;

        e.Cancel = true;
        _closeWhenFinished = true;
        StatusText.Text = "Cancelling contact operation before close…";
        DetailText.Text = "Rice2k will close after the current public-contact operation reaches a safe boundary.";
        _operationCts?.Cancel();
    }

    private void ShowError(string message) =>
        MessageBox.Show(this, message, "Rice2k Public Identity Contacts", MessageBoxButton.OK, MessageBoxImage.Warning);
}
