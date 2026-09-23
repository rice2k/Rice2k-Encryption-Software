using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Rice2k.Encryption.Models;
using Rice2k.Encryption.Services;

namespace Rice2k.Encryption;

public partial class RecoveryCenterWindow : Window
{
    private readonly KeyManagerService _keyManager = new();
    private readonly RecoveryPackageService _recovery = new();
    private readonly AppSettingsService _settingsService;

    public RecoveryCenterWindow() : this(new AppSettingsService())
    {
    }

    public RecoveryCenterWindow(AppSettingsService settingsService)
    {
        _settingsService = settingsService;
        InitializeComponent();
    }

    private void BrowseSourceKeyPackage_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose the .r2kkey package to back up",
            Filter = "Rice2k key packages (*.r2kkey)|*.r2kkey|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == true)
            SourceKeyPackageBox.Text = dialog.FileName;
    }

    private void BrowseRecoveryPackage_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose a Rice2k recovery package",
            Filter = "Rice2k recovery packages (*.r2krecovery)|*.r2krecovery|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == true)
        {
            TestRecoveryPackageBox.Text = dialog.FileName;
            RecoveryTestStatusText.Text = "Ready to test this recovery package.";
            RecoveryFingerprintBox.Clear();
        }
    }

    private async void CreateRecoveryPackage_Click(object sender, RoutedEventArgs e)
    {
        if (!File.Exists(SourceKeyPackageBox.Text))
        {
            ShowError("Choose an existing .r2kkey package first.");
            return;
        }

        if (string.IsNullOrWhiteSpace(SourceKeyPasswordBox.Password))
        {
            ShowError("Enter the current password for the .r2kkey package.");
            return;
        }

        if (string.IsNullOrWhiteSpace(RecoveryPasswordBox.Password) || RecoveryPasswordBox.Password.Length < 12)
        {
            ShowError("Use a recovery password of at least 12 characters. A longer unique password is recommended.");
            return;
        }

        if (!string.Equals(RecoveryPasswordBox.Password, RecoveryConfirmPasswordBox.Password, StringComparison.Ordinal))
        {
            ShowError("The recovery password and confirmation do not match.");
            return;
        }

        ManagedKey? key = null;
        try
        {
            RecoveryStatusText.Text = "Authenticating the source key package…";
            key = await _keyManager.ImportAsync(SourceKeyPackageBox.Text, SourceKeyPasswordBox.Password);

            var dialog = new SaveFileDialog
            {
                Title = "Save Rice2k recovery package",
                Filter = "Rice2k recovery packages (*.r2krecovery)|*.r2krecovery",
                DefaultExt = ".r2krecovery",
                AddExtension = true,
                OverwritePrompt = false,
                FileName = SanitizeFileName(key.Name) + ".r2krecovery"
            };

            if (dialog.ShowDialog(this) != true)
            {
                RecoveryStatusText.Text = "Recovery package creation cancelled. No recovery file was written.";
                return;
            }

            RecoveryStatusText.Text = "Creating authenticated recovery package…";
            await _recovery.CreateAsync(key, dialog.FileName, RecoveryPasswordBox.Password);

            TestRecoveryPackageBox.Text = dialog.FileName;
            RecoveryTestStatusText.Text = "Recovery package created. Test it now before storing it.";
            RecoveryFingerprintBox.Text = key.Fingerprint;
            RecoveryStatusText.Text = $"✓ Recovery package created for {key.Name}. Recommended next step: Test Recovery using the recovery password.";
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
            RecoveryStatusText.Text = "⚠ Recovery package was not created.";
        }
        finally
        {
            key?.Dispose();
            SourceKeyPasswordBox.Clear();
            RecoveryPasswordBox.Clear();
            RecoveryConfirmPasswordBox.Clear();
        }
    }

    private async void TestRecovery_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidateRecoveryTestInputs())
            return;

        ManagedKey? recovered = null;
        try
        {
            RecoveryTestStatusText.Text = "Testing authentication and key recovery…";
            RecoveryFingerprintBox.Clear();
            recovered = await _recovery.OpenAsync(TestRecoveryPackageBox.Text, TestRecoveryPasswordBox.Password);

            RecoveryFingerprintBox.Text = recovered.Fingerprint;
            RecoveryTestStatusText.Text = $"✓ Recovery test passed for '{recovered.Name}'. The package authenticated and contains a valid 256-bit key.";
            RecoveryStatusText.Text = "✓ Test passed. Compare this fingerprint with the original key's fingerprint when available.";
            RecordSuccessfulRecoveryTest(recovered);
        }
        catch (Exception ex)
        {
            RecoveryTestStatusText.Text = "⚠ Recovery test failed. Do not rely on this package until the problem is resolved.";
            RecoveryFingerprintBox.Clear();
            ShowError(ex.Message);
        }
        finally
        {
            recovered?.Dispose();
            TestRecoveryPasswordBox.Clear();
        }
    }

    private async void RestoreKeyPackage_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidateRecoveryTestInputs())
            return;

        var newPassword = PromptForNewPackagePassword();
        if (newPassword is null)
            return;

        ManagedKey? recovered = null;
        try
        {
            RecoveryStatusText.Text = "Authenticating recovery package…";
            recovered = await _recovery.OpenAsync(TestRecoveryPackageBox.Text, TestRecoveryPasswordBox.Password);

            var dialog = new SaveFileDialog
            {
                Title = "Restore a new encrypted Rice2k key package",
                Filter = "Rice2k key packages (*.r2kkey)|*.r2kkey",
                DefaultExt = ".r2kkey",
                AddExtension = true,
                OverwritePrompt = false,
                FileName = SanitizeFileName(recovered.Name) + "-restored.r2kkey"
            };

            if (dialog.ShowDialog(this) != true)
            {
                RecoveryStatusText.Text = "Restore cancelled. The recovery package was not changed.";
                return;
            }

            await _keyManager.ExportAsync(recovered, dialog.FileName, newPassword);
            RecoveryFingerprintBox.Text = recovered.Fingerprint;
            RecoveryTestStatusText.Text = $"✓ Restored a new .r2kkey package for '{recovered.Name}'.";
            RecoveryStatusText.Text = $"✓ Restore complete. Fingerprint: {recovered.Fingerprint}";
            RecordSuccessfulRecoveryTest(recovered);
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
            RecoveryStatusText.Text = "⚠ Restore failed safely. Existing recovery data was not modified.";
        }
        finally
        {
            recovered?.Dispose();
            TestRecoveryPasswordBox.Clear();
        }
    }

    private void RecordSuccessfulRecoveryTest(ManagedKey recovered)
    {
        var current = _settingsService.Load();
        _settingsService.TrySave(current with
        {
            LastRecoveryTestUtc = DateTimeOffset.UtcNow,
            LastRecoveryFingerprint = recovered.Fingerprint,
            LastRecoveryKeyName = recovered.Name
        });
    }

    private bool ValidateRecoveryTestInputs()
    {
        if (!File.Exists(TestRecoveryPackageBox.Text))
        {
            ShowError("Choose a .r2krecovery package first.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(TestRecoveryPasswordBox.Password))
        {
            ShowError("Enter the recovery password first.");
            return false;
        }

        return true;
    }

    private string? PromptForNewPackagePassword()
    {
        var first = new PasswordBox { Margin = new Thickness(0, 5, 0, 8) };
        var confirm = new PasswordBox { Margin = new Thickness(0, 5, 0, 12) };
        var ok = new Button { Content = "Use Password", IsDefault = true, MinWidth = 115 };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 90, Margin = new Thickness(8, 0, 0, 0) };

        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = "Protect the restored .r2kkey", FontSize = 19, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = "Choose a new password of at least 12 characters for the restored key package.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 10), Opacity = 0.82 });
        panel.Children.Add(new TextBlock { Text = "New key-package password" });
        panel.Children.Add(first);
        panel.Children.Add(new TextBlock { Text = "Confirm password" });
        panel.Children.Add(confirm);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        actions.Children.Add(ok);
        actions.Children.Add(cancel);
        panel.Children.Add(actions);

        var dialog = new Window
        {
            Title = "Restore Key Package",
            Owner = this,
            Width = 470,
            Height = 290,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = panel
        };

        ok.Click += (_, _) =>
        {
            if (first.Password.Length < 12)
            {
                MessageBox.Show(dialog, "Use at least 12 characters.", "Rice2k Recovery Center", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!string.Equals(first.Password, confirm.Password, StringComparison.Ordinal))
            {
                MessageBox.Show(dialog, "The two passwords do not match.", "Rice2k Recovery Center", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            dialog.DialogResult = true;
        };

        var result = dialog.ShowDialog();
        if (result != true)
            return null;

        var password = first.Password;
        first.Clear();
        confirm.Clear();
        return password;
    }

    private void ShowError(string message)
    {
        MessageBox.Show(
            this,
            message,
            "Rice2k Recovery Center",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "Rice2k-Recovery" : sanitized;
    }
}
