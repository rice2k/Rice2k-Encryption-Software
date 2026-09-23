using System.Collections.ObjectModel;
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

    public PublicIdentityContactsWindow()
    {
        InitializeComponent();
        ContactsList.ItemsSource = _contacts;
    }

    public IReadOnlyList<Rice2kPublicIdentity> SelectedContacts { get; private set; } = Array.Empty<Rice2kPublicIdentity>();

    private async void Window_Loaded(object sender, RoutedEventArgs e) => await ReloadAsync();

    private async Task ReloadAsync()
    {
        try
        {
            StatusText.Text = "Loading validated contacts…";
            var contacts = await _store.LoadAllAsync();
            _contacts.Clear();
            foreach (var contact in contacts)
                _contacts.Add(contact);

            StatusText.Text = $"{_contacts.Count:N0} saved public contact(s)";
            DetailText.Text = _contacts.Count == 0
                ? "Import a .r2kpub public identity card to save a reusable contact."
                : "Compare fingerprints independently before relying on a contact label for identity.";
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
            StatusText.Text = "⚠ Contacts could not be loaded";
        }
    }

    private async void ImportContact_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import Rice2k public identity contact",
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
                await _store.ImportAsync(path);
                added++;
            }
            catch (Exception ex)
            {
                skipped++;
                ShowError($"Could not save {Path.GetFileName(path)}:\n\n{ex.Message}");
            }
        }

        await ReloadAsync();
        DetailText.Text = $"Imported {added:N0}" + (skipped > 0 ? $" • skipped {skipped:N0}" : string.Empty) + ". Stored contacts remain public-key material only.";
    }

    private async void RemoveContact_Click(object sender, RoutedEventArgs e)
    {
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

        try
        {
            await _store.RemoveAsync(identity);
            await ReloadAsync();
            StatusText.Text = "✓ Saved contact removed";
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
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

    private void ShowError(string message) =>
        MessageBox.Show(this, message, "Rice2k Public Identity Contacts", MessageBoxButton.OK, MessageBoxImage.Warning);
}
