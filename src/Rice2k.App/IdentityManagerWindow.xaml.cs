using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Rice2k.Encryption.Models;
using Rice2k.Encryption.Services;

namespace Rice2k.Encryption;

public partial class IdentityManagerWindow : Window
{
    private readonly IdentityService _service = new();
    private readonly ProtectedClipboardService _clipboard = new();
    private readonly ObservableCollection<Rice2kIdentity> _identities = [];

    public IdentityManagerWindow()
    {
        InitializeComponent();
        IdentityList.ItemsSource = _identities;
    }

    private void GenerateIdentity_Click(object sender, RoutedEventArgs e)
    {
        var name = IdentityNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            ShowError("Enter a display name for the identity first.");
            return;
        }

        var identity = _service.Generate(name);
        _identities.Add(identity);
        IdentityList.SelectedItem = identity;
        IdentityNameBox.Clear();
        StatusText.Text = $"✓ Identity generated • {identity.Fingerprint}. Export the private .r2kid before closing if you want to keep it.";
    }

    private async void ImportPrivate_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import Rice2k private identity",
            Filter = "Rice2k private identities (*.r2kid)|*.r2kid|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true)
            return;

        var password = PromptForPassword(
            "Unlock Private Identity",
            "Enter the password protecting this .r2kid package.",
            confirm: false);
        if (password is null)
            return;

        Rice2kIdentity? identity = null;
        try
        {
            identity = await _service.ImportPrivateAsync(dialog.FileName, password);
            if (_identities.Any(existing => existing.Id == identity.Id || existing.Fingerprint == identity.Fingerprint))
            {
                identity.Dispose();
                ShowError("That identity is already loaded in this Identity Manager session.");
                return;
            }

            _identities.Add(identity);
            IdentityList.SelectedItem = identity;
            StatusText.Text = $"✓ Imported {identity.Name} • {identity.Fingerprint}";
            identity = null;
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            identity?.Dispose();
            password = string.Empty;
        }
    }

    private async void ExportPrivate_Click(object sender, RoutedEventArgs e)
    {
        if (IdentityList.SelectedItem is not Rice2kIdentity identity)
        {
            ShowError("Select an identity first.");
            return;
        }

        var password = PromptForPassword(
            "Protect Private Identity",
            "Choose a new password of at least 12 characters for the exported .r2kid package.",
            confirm: true);
        if (password is null)
            return;

        var dialog = new SaveFileDialog
        {
            Title = "Export encrypted private identity",
            Filter = "Rice2k private identities (*.r2kid)|*.r2kid",
            DefaultExt = ".r2kid",
            AddExtension = true,
            OverwritePrompt = false,
            FileName = SanitizeFileName(identity.Name) + ".r2kid"
        };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            await _service.ExportPrivateAsync(identity, dialog.FileName, password);
            StatusText.Text = $"✓ Private identity exported: {Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            password = string.Empty;
        }
    }

    private async void ExportPublic_Click(object sender, RoutedEventArgs e)
    {
        if (IdentityList.SelectedItem is not Rice2kIdentity identity)
        {
            ShowError("Select an identity first.");
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Export shareable public identity",
            Filter = "Rice2k public identities (*.r2kpub)|*.r2kpub",
            DefaultExt = ".r2kpub",
            AddExtension = true,
            OverwritePrompt = false,
            FileName = SanitizeFileName(identity.Name) + ".r2kpub"
        };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            await _service.ExportPublicAsync(identity, dialog.FileName);
            StatusText.Text = $"✓ Public identity exported. Share {Path.GetFileName(dialog.FileName)} and compare fingerprint {identity.Fingerprint} through an independent channel.";
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private void CopyFingerprint_Click(object sender, RoutedEventArgs e)
    {
        if (IdentityList.SelectedItem is not Rice2kIdentity identity)
        {
            ShowError("Select an identity first.");
            return;
        }

        try
        {
            _clipboard.CopyText(identity.Fingerprint, message => StatusText.Text = message + " Compare the fingerprint through a trusted independent channel before treating the public identity as verified.");
        }
        catch (Exception ex)
        {
            ShowError($"Windows could not access the clipboard. {ex.Message}");
        }
    }

    private void RemoveIdentity_Click(object sender, RoutedEventArgs e)
    {
        if (IdentityList.SelectedItem is not Rice2kIdentity identity)
            return;

        var result = MessageBox.Show(
            this,
            $"Remove '{identity.Name}' from this session?\n\nThis clears the in-memory private keys. It does not delete any exported .r2kid file.",
            "Remove loaded identity?",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
            return;

        _identities.Remove(identity);
        identity.Dispose();
        StatusText.Text = "Identity removed from memory.";
    }

    private string? PromptForPassword(string title, string guidance, bool confirm)
    {
        var first = new PasswordBox { Margin = new Thickness(0, 5, 0, 10) };
        var second = new PasswordBox { Margin = new Thickness(0, 5, 0, 12) };
        var ok = new Button { Content = "Continue", IsDefault = true, MinWidth = 105 };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 90, Margin = new Thickness(8, 0, 0, 0) };
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 19, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = guidance, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 12), Opacity = 0.82 });
        panel.Children.Add(new TextBlock { Text = "Password" });
        panel.Children.Add(first);
        if (confirm)
        {
            panel.Children.Add(new TextBlock { Text = "Confirm password" });
            panel.Children.Add(second);
        }
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        actions.Children.Add(ok);
        actions.Children.Add(cancel);
        panel.Children.Add(actions);

        var dialog = new Window
        {
            Title = title,
            Owner = this,
            Width = 480,
            Height = confirm ? 320 : 265,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = panel
        };

        ok.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(first.Password))
            {
                MessageBox.Show(dialog, "Enter the password first.", title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (confirm && first.Password.Length < 12)
            {
                MessageBox.Show(dialog, "Use at least 12 characters.", title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (confirm && !string.Equals(first.Password, second.Password, StringComparison.Ordinal))
            {
                MessageBox.Show(dialog, "The two passwords do not match.", title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            dialog.DialogResult = true;
        };

        if (dialog.ShowDialog() != true)
            return null;

        var result = first.Password;
        first.Clear();
        second.Clear();
        return result;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        foreach (var identity in _identities)
            identity.Dispose();
        _identities.Clear();
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "Rice2k-Identity" : sanitized;
    }

    private void ShowError(string message) =>
        MessageBox.Show(this, message, "Rice2k Identity Manager", MessageBoxButton.OK, MessageBoxImage.Warning);
}
