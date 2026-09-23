using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using Microsoft.Win32;
using Rice2k.Encryption.Models;
using Rice2k.Encryption.Services;

namespace Rice2k.Encryption;

public partial class KeyManagerWindow : Window
{
    private readonly ObservableCollection<ManagedKey> _keys = [];
    private readonly KeyManagerService _keyManager = new();
    private CancellationTokenSource? _operationCts;
    private bool _busy;
    private bool _closeWhenFinished;

    public KeyManagerWindow()
    {
        InitializeComponent();
        KeysGrid.ItemsSource = _keys;
        Closing += KeyManagerWindow_Closing;
        UpdateCount();
    }

    private void GenerateKey_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
            return;

        var name = string.IsNullOrWhiteSpace(KeyNameBox.Text)
            ? $"Rice2k Key {_keys.Count + 1}"
            : KeyNameBox.Text.Trim();

        var key = _keyManager.Generate(name);
        _keys.Add(key);
        KeysGrid.SelectedItem = key;
        KeysGrid.ScrollIntoView(key);
        KeyManagerStatusText.Text = $"✓ Created {key.Name}. Export it as an encrypted .r2kkey package if you want to keep it after this window closes.";
        UpdateCount();
    }

    private async void ExportKey_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
            return;

        if (KeysGrid.SelectedItem is not ManagedKey key)
        {
            ShowError("Select a key to export first.");
            return;
        }

        var password = PackagePasswordBox.Password;
        var confirmation = PackageConfirmPasswordBox.Password;
        if (string.IsNullOrWhiteSpace(password) || password.Length < 12)
        {
            ShowError("Use a package password of at least 12 characters. A longer unique password is recommended.");
            return;
        }

        if (!string.Equals(password, confirmation, StringComparison.Ordinal))
        {
            ShowError("The package password and confirmation do not match.");
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Export encrypted Rice2k key package",
            Filter = "Rice2k key packages (*.r2kkey)|*.r2kkey",
            DefaultExt = ".r2kkey",
            AddExtension = true,
            OverwritePrompt = false,
            FileName = SanitizeFileName(key.Name) + ".r2kkey"
        };

        if (dialog.ShowDialog(this) != true)
        {
            PackagePasswordBox.Clear();
            PackageConfirmPasswordBox.Clear();
            password = string.Empty;
            confirmation = string.Empty;
            return;
        }

        BeginOperation("Encrypting key package…");
        try
        {
            await _keyManager.ExportAsync(key, dialog.FileName, password, _operationCts!.Token);
            KeyManagerStatusText.Text = $"✓ Exported encrypted key package. Fingerprint: {key.Fingerprint}";
        }
        catch (OperationCanceledException)
        {
            KeyManagerStatusText.Text = "Key export cancelled safely. Incomplete temporary package output was removed where possible.";
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
            KeyManagerStatusText.Text = "⚠ Key export needs attention.";
        }
        finally
        {
            password = string.Empty;
            confirmation = string.Empty;
            PackagePasswordBox.Clear();
            PackageConfirmPasswordBox.Clear();
            EndOperation();
        }
    }

    private async void ImportKey_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
            return;

        var password = PackagePasswordBox.Password;
        if (string.IsNullOrWhiteSpace(password))
        {
            ShowError("Enter the password for the .r2kkey package before importing it.");
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "Import a Rice2k key package",
            Filter = "Rice2k key packages (*.r2kkey)|*.r2kkey|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true)
        {
            PackagePasswordBox.Clear();
            PackageConfirmPasswordBox.Clear();
            password = string.Empty;
            return;
        }

        BeginOperation("Authenticating encrypted key package…");
        ManagedKey? imported = null;
        try
        {
            imported = await _keyManager.ImportAsync(dialog.FileName, password, _operationCts!.Token);
            _operationCts.Token.ThrowIfCancellationRequested();

            if (_keys.Any(existing => existing.Id == imported.Id || string.Equals(existing.Fingerprint, imported.Fingerprint, StringComparison.Ordinal)))
            {
                imported.Dispose();
                imported = null;
                KeyManagerStatusText.Text = "That key is already loaded in this session.";
                return;
            }

            _keys.Add(imported);
            KeysGrid.SelectedItem = imported;
            KeysGrid.ScrollIntoView(imported);
            KeyManagerStatusText.Text = $"✓ Imported and authenticated {imported.Name}. Fingerprint: {imported.Fingerprint}";
            imported = null; // ownership transferred to _keys
            UpdateCount();
        }
        catch (OperationCanceledException)
        {
            imported?.Dispose();
            imported = null;
            KeyManagerStatusText.Text = "Key import cancelled safely. No new key was retained.";
        }
        catch (Exception ex)
        {
            imported?.Dispose();
            imported = null;
            ShowError(ex.Message);
            KeyManagerStatusText.Text = "⚠ Key import failed safely. No key was added.";
        }
        finally
        {
            imported?.Dispose();
            password = string.Empty;
            PackagePasswordBox.Clear();
            PackageConfirmPasswordBox.Clear();
            EndOperation();
        }
    }

    private void RemoveKey_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || KeysGrid.SelectedItem is not ManagedKey key)
            return;

        var result = MessageBox.Show(
            this,
            $"Remove '{key.Name}' from this session?\n\nThis does not delete any .r2kkey package you exported earlier.",
            "Remove key from session?",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
            return;

        _keys.Remove(key);
        key.Dispose();
        ClearSelectionDetails();
        KeyManagerStatusText.Text = "Key removed from this session and its in-memory secret bytes were cleared.";
        UpdateCount();
    }

    private void KeysGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (KeysGrid.SelectedItem is ManagedKey key)
        {
            SelectedKeyNameText.Text = key.Name;
            SelectedKeyTypeText.Text = $"{key.KeyType} • {key.Source} • created {key.CreatedDisplay}";
            SelectedFingerprintBox.Text = key.Fingerprint;
            ExportKeyButton.IsEnabled = !_busy;
            RemoveKeyButton.IsEnabled = !_busy;
        }
        else
        {
            ClearSelectionDetails();
        }
    }

    private void ClearSelectionDetails()
    {
        SelectedKeyNameText.Text = "No key selected";
        SelectedKeyTypeText.Text = "Select a key to view its safe identifying details.";
        SelectedFingerprintBox.Clear();
        ExportKeyButton.IsEnabled = false;
        RemoveKeyButton.IsEnabled = false;
    }

    private void BeginOperation(string status)
    {
        _operationCts?.Dispose();
        _operationCts = new CancellationTokenSource();
        _busy = true;
        ExportKeyButton.IsEnabled = false;
        RemoveKeyButton.IsEnabled = false;
        KeysGrid.IsEnabled = false;
        KeyNameBox.IsEnabled = false;
        KeyManagerStatusText.Text = status;
    }

    private void EndOperation()
    {
        _busy = false;
        KeysGrid.IsEnabled = true;
        KeyNameBox.IsEnabled = true;
        ExportKeyButton.IsEnabled = KeysGrid.SelectedItem is ManagedKey;
        RemoveKeyButton.IsEnabled = KeysGrid.SelectedItem is ManagedKey;
        _operationCts?.Dispose();
        _operationCts = null;

        if (_closeWhenFinished)
        {
            _closeWhenFinished = false;
            Dispatcher.InvokeAsync(Close);
        }
    }

    private void UpdateCount() =>
        KeyCountText.Text = $"Session keys — {_keys.Count}";

    private void KeyManagerWindow_Closing(object? sender, CancelEventArgs e)
    {
        PackagePasswordBox.Clear();
        PackageConfirmPasswordBox.Clear();

        if (_busy)
        {
            e.Cancel = true;
            _closeWhenFinished = true;
            KeyManagerStatusText.Text = "Cancelling the active key operation before closing…";
            _operationCts?.Cancel();
            return;
        }

        foreach (var key in _keys)
            key.Dispose();
        _keys.Clear();
    }

    private void ShowError(string message)
    {
        MessageBox.Show(
            this,
            message,
            "Rice2k Key Manager",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "Rice2k-Key" : sanitized;
    }
}
