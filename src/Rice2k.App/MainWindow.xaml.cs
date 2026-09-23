using System.Globalization;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Rice2k.Encryption.Models;
using Rice2k.Encryption.Services;

namespace Rice2k.Encryption;

public partial class MainWindow : Window
{
    private readonly FileEncryptionService _fileCrypto = new();
    private readonly TextCryptoService _textCrypto = new();
    private readonly IntegrityService _integrity = new();
    private readonly PasswordGeneratorService _passwordGenerator = new();
    private CancellationTokenSource? _operationCts;

    public MainWindow()
    {
        InitializeComponent();
        GeneratedPasswordBox.Text = _passwordGenerator.GeneratePassword(24);
        ActivityList.Items.Add($"{DateTime.Now:t}  Rice2k started — ready");
    }

    private void Navigate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag })
            NavigateTo(tag);
    }

    private void QuickNavigate_Click(object sender, RoutedEventArgs e) => Navigate_Click(sender, e);

    private void NavigateTo(string tag)
    {
        foreach (var page in AllPages())
            page.Visibility = Visibility.Collapsed;

        var target = tag switch
        {
            "Encrypt" => EncryptPage,
            "Decrypt" => DecryptPage,
            "Text" => TextPage,
            "Vault" => VaultPage,
            "Passwords" => PasswordsPage,
            "Integrity" => IntegrityPage,
            "Recovery" => RecoveryPage,
            "Activity" => ActivityPage,
            "Settings" => SettingsPage,
            _ => HomePage
        };

        target.Visibility = Visibility.Visible;
    }

    private IEnumerable<FrameworkElement> AllPages()
    {
        yield return HomePage;
        yield return EncryptPage;
        yield return DecryptPage;
        yield return TextPage;
        yield return VaultPage;
        yield return PasswordsPage;
        yield return IntegrityPage;
        yield return RecoveryPage;
        yield return ActivityPage;
        yield return SettingsPage;
    }

    private void BrowseEncryptSource_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose a file to encrypt",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true)
            return;

        EncryptSourceBox.Text = dialog.FileName;
        EncryptDestinationBox.Text = dialog.FileName + ".r2kenc";
        EncryptStatusText.Text = "Ready to encrypt";
        EncryptProgressDetails.Text = FormatBytes(new FileInfo(dialog.FileName).Length);
    }

    private void BrowseDecryptSource_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose a Rice2k encrypted file",
            Filter = "Rice2k encrypted files (*.r2kenc)|*.r2kenc|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true)
            return;

        DecryptSourceBox.Text = dialog.FileName;
        DecryptDestinationBox.Text = SuggestDecryptedPath(dialog.FileName);
        DecryptStatusText.Text = "Ready to decrypt";
    }

    private async void StartEncrypt_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidateOperationInput(
                EncryptSourceBox.Text,
                EncryptDestinationBox.Text,
                EncryptPasswordBox.Password,
                "encrypt"))
            return;

        if (_operationCts is not null)
        {
            ShowFriendlyError("Another encryption or decryption job is already running.");
            return;
        }

        _operationCts = new CancellationTokenSource();
        SetOperationUi(isRunning: true, encrypting: true);
        EncryptProgressBar.Value = 0;
        EncryptStatusText.Text = "Preparing encryption…";
        AddActivity("Encryption started", Path.GetFileName(EncryptSourceBox.Text));

        var progress = new Progress<CryptoProgress>(p => UpdateProgress(p, encrypting: true));

        try
        {
            await _fileCrypto.EncryptFileAsync(
                EncryptSourceBox.Text,
                EncryptDestinationBox.Text,
                EncryptPasswordBox.Password,
                progress,
                _operationCts.Token,
                VerifyAfterEncryptCheck.IsChecked != false);

            EncryptProgressBar.Value = 100;
            EncryptStatusText.Text = "✓ Encryption complete and verified";
            EncryptProgressDetails.Text = "100% — encrypted output finalized safely";
            GlobalStatusText.Text = "● Ready   |   Last job: encryption successful   |   v0.1.0";
            AddActivity("Encryption complete", Path.GetFileName(EncryptDestinationBox.Text));

            MessageBox.Show(
                this,
                $"Encryption completed successfully.\n\nEncrypted file:\n{EncryptDestinationBox.Text}\n\nYour original file was not removed or changed.",
                "Rice2k Encryption Complete",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (OperationCanceledException)
        {
            EncryptStatusText.Text = "Encryption cancelled";
            EncryptProgressDetails.Text = "The incomplete temporary output was removed. Your original file was not changed.";
            AddActivity("Encryption cancelled", Path.GetFileName(EncryptSourceBox.Text));
        }
        catch (Exception ex)
        {
            EncryptStatusText.Text = "Encryption could not be completed";
            EncryptProgressDetails.Text = ex.Message;
            AddActivity("Encryption failed", Path.GetFileName(EncryptSourceBox.Text));
            ShowFriendlyError(ex.Message);
        }
        finally
        {
            _operationCts.Dispose();
            _operationCts = null;
            SetOperationUi(isRunning: false, encrypting: true);
            EncryptPasswordBox.Clear();
        }
    }

    private async void StartDecrypt_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidateOperationInput(
                DecryptSourceBox.Text,
                DecryptDestinationBox.Text,
                DecryptPasswordBox.Password,
                "decrypt"))
            return;

        if (_operationCts is not null)
        {
            ShowFriendlyError("Another encryption or decryption job is already running.");
            return;
        }

        _operationCts = new CancellationTokenSource();
        SetOperationUi(isRunning: true, encrypting: false);
        DecryptProgressBar.Value = 0;
        DecryptStatusText.Text = "Preparing decryption…";
        AddActivity("Decryption started", Path.GetFileName(DecryptSourceBox.Text));

        var progress = new Progress<CryptoProgress>(p => UpdateProgress(p, encrypting: false));

        try
        {
            await _fileCrypto.DecryptFileAsync(
                DecryptSourceBox.Text,
                DecryptDestinationBox.Text,
                DecryptPasswordBox.Password,
                progress,
                _operationCts.Token);

            DecryptProgressBar.Value = 100;
            DecryptStatusText.Text = "✓ Decryption complete";
            DecryptProgressDetails.Text = "100% — authentication and file-length checks passed";
            GlobalStatusText.Text = "● Ready   |   Last job: decryption successful   |   v0.1.0";
            AddActivity("Decryption complete", Path.GetFileName(DecryptDestinationBox.Text));

            MessageBox.Show(
                this,
                $"Decryption completed successfully.\n\nRestored file:\n{DecryptDestinationBox.Text}",
                "Rice2k Decryption Complete",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (OperationCanceledException)
        {
            DecryptStatusText.Text = "Decryption cancelled";
            DecryptProgressDetails.Text = "The incomplete temporary output was removed.";
            AddActivity("Decryption cancelled", Path.GetFileName(DecryptSourceBox.Text));
        }
        catch (Exception ex)
        {
            DecryptStatusText.Text = "Decryption could not be completed";
            DecryptProgressDetails.Text = ex.Message;
            AddActivity("Decryption failed", Path.GetFileName(DecryptSourceBox.Text));
            ShowFriendlyError(ex.Message);
        }
        finally
        {
            _operationCts.Dispose();
            _operationCts = null;
            SetOperationUi(isRunning: false, encrypting: false);
            DecryptPasswordBox.Clear();
        }
    }

    private void CancelOperation_Click(object sender, RoutedEventArgs e)
    {
        if (_operationCts is null)
            return;

        var result = MessageBox.Show(
            this,
            "Stop the current operation?\n\nRice2k will remove the incomplete temporary output. Your original source file will not be changed.",
            "Stop operation?",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            GlobalStatusText.Text = "● Cancelling safely…";
            _operationCts.Cancel();
        }
    }

    private void EncryptText_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            RequirePassword(TextPasswordBox.Password);
            TextOutputBox.Text = _textCrypto.Encrypt(TextInputBox.Text, TextPasswordBox.Password);
            AddActivity("Text encrypted", $"{TextInputBox.Text.Length:N0} characters");
        }
        catch (Exception ex)
        {
            ShowFriendlyError(ex.Message);
        }
        finally
        {
            TextPasswordBox.Clear();
        }
    }

    private void DecryptText_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            RequirePassword(TextPasswordBox.Password);
            TextOutputBox.Text = _textCrypto.Decrypt(TextInputBox.Text, TextPasswordBox.Password);
            AddActivity("Text decrypted", "Authenticated text token");
        }
        catch (Exception ex)
        {
            ShowFriendlyError(ex.Message);
        }
        finally
        {
            TextPasswordBox.Clear();
        }
    }

    private void CopyTextOutput_Click(object sender, RoutedEventArgs e) => CopyToClipboard(TextOutputBox.Text, "Text output copied");

    private void ClearText_Click(object sender, RoutedEventArgs e)
    {
        TextInputBox.Clear();
        TextOutputBox.Clear();
        TextPasswordBox.Clear();
    }

    private void GeneratePassword_Click(object sender, RoutedEventArgs e)
    {
        var length = (int)Math.Round(PasswordLengthSlider.Value);
        GeneratedPasswordBox.Text = _passwordGenerator.GeneratePassword(length);
        PasswordLengthText.Text = $"{length} characters";
        AddActivity("Password generated", $"{length} characters");
    }

    private void CopyGeneratedPassword_Click(object sender, RoutedEventArgs e) =>
        CopyToClipboard(GeneratedPasswordBox.Text, "Generated password copied");

    private void BrowseIntegrityFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose a file to check",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == true)
        {
            IntegrityFileBox.Text = dialog.FileName;
            IntegrityResultBox.Clear();
        }
    }

    private async void ComputeSha256_Click(object sender, RoutedEventArgs e)
    {
        await ComputeIntegrityAsync("SHA-256", p => _integrity.ComputeSha256Async(p));
    }

    private async void ComputeSha512_Click(object sender, RoutedEventArgs e)
    {
        await ComputeIntegrityAsync("SHA-512", p => _integrity.ComputeSha512Async(p));
    }

    private async Task ComputeIntegrityAsync(string algorithm, Func<string, Task<string>> compute)
    {
        try
        {
            if (!File.Exists(IntegrityFileBox.Text))
                throw new FileNotFoundException("Choose a file first.");

            GlobalStatusText.Text = $"● Calculating {algorithm}…";
            IntegrityResultBox.Text = await compute(IntegrityFileBox.Text);
            GlobalStatusText.Text = "● Ready   |   Integrity calculation complete   |   v0.1.0";
            AddActivity($"{algorithm} calculated", Path.GetFileName(IntegrityFileBox.Text));
        }
        catch (Exception ex)
        {
            ShowFriendlyError(ex.Message);
            GlobalStatusText.Text = "● Ready   |   v0.1.0";
        }
    }

    private void CopyIntegrityResult_Click(object sender, RoutedEventArgs e) =>
        CopyToClipboard(IntegrityResultBox.Text, "Checksum copied");

    private void UpdateProgress(CryptoProgress p, bool encrypting)
    {
        var bar = encrypting ? EncryptProgressBar : DecryptProgressBar;
        var status = encrypting ? EncryptStatusText : DecryptStatusText;
        var details = encrypting ? EncryptProgressDetails : DecryptProgressDetails;

        bar.Value = p.Percentage;
        status.Text = p.Stage;

        var eta = p.EstimatedRemaining is { } remaining
            ? $" • about {FormatDuration(remaining)} remaining"
            : string.Empty;

        details.Text = $"{p.Percentage:0.0}% • {FormatBytes(p.BytesProcessed)} / {FormatBytes(p.TotalBytes)} • {FormatBytes((long)p.BytesPerSecond)}/s • elapsed {FormatDuration(p.Elapsed)}{eta}";
        GlobalStatusText.Text = $"● {p.Stage}   {p.Percentage:0}%   |   {FormatBytes((long)p.BytesPerSecond)}/s{eta}";
    }

    private void SetOperationUi(bool isRunning, bool encrypting)
    {
        if (encrypting)
        {
            EncryptStartButton.IsEnabled = !isRunning;
            EncryptCancelButton.IsEnabled = isRunning;
        }
        else
        {
            DecryptStartButton.IsEnabled = !isRunning;
            DecryptCancelButton.IsEnabled = isRunning;
        }

        if (!isRunning)
            GlobalStatusText.Text = "● Ready   |   Offline/local   |   v0.1.0";
    }

    private static bool ValidateOperationInput(string source, string destination, string password, string verb)
    {
        if (!File.Exists(source))
        {
            MessageBox.Show($"Choose a file to {verb} first.", "Rice2k", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        if (string.IsNullOrWhiteSpace(destination))
        {
            MessageBox.Show("Choose an output path first.", "Rice2k", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        try
        {
            RequirePassword(password);
            return true;
        }
        catch (ArgumentException ex)
        {
            MessageBox.Show(ex.Message, "Rice2k", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
    }

    private static void RequirePassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 12)
            throw new ArgumentException("Use a password of at least 12 characters. A longer generated password is recommended.");
    }

    private void AddActivity(string action, string detail)
    {
        ActivityList.Items.Insert(0, $"{DateTime.Now:G}   {action}   —   {detail}");
        while (ActivityList.Items.Count > 100)
            ActivityList.Items.RemoveAt(ActivityList.Items.Count - 1);
    }

    private void CopyToClipboard(string value, string status)
    {
        if (string.IsNullOrEmpty(value))
            return;

        try
        {
            Clipboard.SetText(value);
            GlobalStatusText.Text = $"● {status}";
        }
        catch (Exception ex)
        {
            ShowFriendlyError($"Windows could not access the clipboard. {ex.Message}");
        }
    }

    private void ShowFriendlyError(string message)
    {
        MessageBox.Show(
            this,
            message,
            "Rice2k Encryption Software",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private static string SuggestDecryptedPath(string encryptedPath)
    {
        var path = encryptedPath.EndsWith(".r2kenc", StringComparison.OrdinalIgnoreCase)
            ? encryptedPath[..^7]
            : encryptedPath + ".decrypted";

        if (!File.Exists(path))
            return path;

        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        return Path.Combine(directory, $"{name}.decrypted{extension}");
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

        return string.Create(CultureInfo.InvariantCulture, $"{value:0.##} {units[unit]}");
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
