using System.Diagnostics;
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
    private readonly OperationPreflightService _preflight = new();
    private CancellationTokenSource? _operationCts;
    private DateTimeOffset _operationStartedUtc;

    public MainWindow()
    {
        InitializeComponent();
        GeneratedPasswordBox.Text = _passwordGenerator.GeneratePassword(24);
        ActivityList.Items.Add($"{DateTime.Now:t}  Rice2k started — ready");
        ResetEncryptWorkflow(clearSource: true);
        ResetDecryptWorkflow(clearSource: true);
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

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths)
            return;

        var files = paths.Where(File.Exists).ToArray();
        if (files.Length == 0)
        {
            ShowFriendlyError("Drop a file onto Rice2k. Folder and multi-file queue support is the next development milestone.");
            return;
        }

        var path = files[0];
        if (path.EndsWith(".r2kenc", StringComparison.OrdinalIgnoreCase))
        {
            SetDecryptSource(path);
            ShowDecryptStep(1);
            NavigateTo("Decrypt");
        }
        else
        {
            SetEncryptSource(path);
            ShowEncryptStep(1);
            NavigateTo("Encrypt");
        }

        if (files.Length > 1)
            GlobalStatusText.Text = $"● Loaded {Path.GetFileName(path)}   |   {files.Length - 1} additional file(s) will be supported by the batch queue milestone";
    }

    private void BrowseEncryptSource_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose a file to encrypt",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == true)
            SetEncryptSource(dialog.FileName);
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

        if (dialog.ShowDialog(this) == true)
            SetDecryptSource(dialog.FileName);
    }

    private void BrowseEncryptDestination_Click(object sender, RoutedEventArgs e)
    {
        if (!File.Exists(EncryptSourceBox.Text))
        {
            ShowFriendlyError("Choose the file you want to encrypt first.");
            return;
        }

        var current = string.IsNullOrWhiteSpace(EncryptDestinationBox.Text)
            ? EncryptSourceBox.Text + ".r2kenc"
            : EncryptDestinationBox.Text;

        var dialog = new SaveFileDialog
        {
            Title = "Choose where to save the encrypted file",
            Filter = "Rice2k encrypted files (*.r2kenc)|*.r2kenc",
            DefaultExt = ".r2kenc",
            AddExtension = true,
            OverwritePrompt = false,
            FileName = Path.GetFileName(current),
            InitialDirectory = Path.GetDirectoryName(current)
        };

        if (dialog.ShowDialog(this) == true)
        {
            EncryptDestinationBox.Text = CreateNonCollidingPath(dialog.FileName);
            PopulateEncryptReview();
            RunEncryptPreflight(showDialog: false);
        }
    }

    private void BrowseDecryptDestination_Click(object sender, RoutedEventArgs e)
    {
        if (!File.Exists(DecryptSourceBox.Text))
        {
            ShowFriendlyError("Choose the encrypted file first.");
            return;
        }

        var current = string.IsNullOrWhiteSpace(DecryptDestinationBox.Text)
            ? SuggestDecryptedPath(DecryptSourceBox.Text)
            : DecryptDestinationBox.Text;

        var dialog = new SaveFileDialog
        {
            Title = "Choose where to restore the decrypted file",
            Filter = "All files (*.*)|*.*",
            OverwritePrompt = false,
            FileName = Path.GetFileName(current),
            InitialDirectory = Path.GetDirectoryName(current)
        };

        if (dialog.ShowDialog(this) == true)
        {
            DecryptDestinationBox.Text = CreateNonCollidingPath(dialog.FileName);
            PopulateDecryptReview();
            RunDecryptPreflight(showDialog: false);
        }
    }

    private void SetEncryptSource(string path)
    {
        EncryptSourceBox.Text = path;
        EncryptDestinationBox.Text = CreateNonCollidingPath(path + ".r2kenc");
        var info = new FileInfo(path);
        EncryptSourceSummaryText.Text = $"{info.Name}  •  {FormatBytes(info.Length)}";
        EncryptPreflightText.Text = "Safety check will run before encryption starts.";
        GlobalStatusText.Text = $"● Ready to protect {info.Name}";
    }

    private void SetDecryptSource(string path)
    {
        DecryptSourceBox.Text = path;
        DecryptDestinationBox.Text = CreateNonCollidingPath(SuggestDecryptedPath(path));
        var info = new FileInfo(path);
        DecryptSourceSummaryText.Text = $"{info.Name}  •  {FormatBytes(info.Length)}";
        DecryptPreflightText.Text = "Safety check will run before decryption starts.";
        GlobalStatusText.Text = $"● Ready to restore {info.Name}";
    }

    private void EncryptStep1Next_Click(object sender, RoutedEventArgs e)
    {
        if (!File.Exists(EncryptSourceBox.Text))
        {
            ShowFriendlyError("Choose a file to protect before continuing.");
            return;
        }

        ShowEncryptStep(2);
        EncryptPasswordBox.Focus();
    }

    private void EncryptStep2Next_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidateEncryptPassword())
            return;

        PopulateEncryptReview();
        ShowEncryptStep(3);
        RunEncryptPreflight(showDialog: false);
    }

    private void EncryptBackTo1_Click(object sender, RoutedEventArgs e) => ShowEncryptStep(1);
    private void EncryptBackTo2_Click(object sender, RoutedEventArgs e) => ShowEncryptStep(2);

    private void DecryptStep1Next_Click(object sender, RoutedEventArgs e)
    {
        if (!File.Exists(DecryptSourceBox.Text))
        {
            ShowFriendlyError("Choose a Rice2k encrypted file before continuing.");
            return;
        }

        ShowDecryptStep(2);
        DecryptPasswordBox.Focus();
    }

    private void DecryptStep2Next_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            RequirePassword(DecryptPasswordBox.Password);
        }
        catch (ArgumentException ex)
        {
            ShowFriendlyError(ex.Message);
            return;
        }

        if (string.IsNullOrWhiteSpace(DecryptDestinationBox.Text))
        {
            ShowFriendlyError("Choose where the restored file should be saved.");
            return;
        }

        PopulateDecryptReview();
        ShowDecryptStep(3);
        RunDecryptPreflight(showDialog: false);
    }

    private void DecryptBackTo1_Click(object sender, RoutedEventArgs e) => ShowDecryptStep(1);
    private void DecryptBackTo2_Click(object sender, RoutedEventArgs e) => ShowDecryptStep(2);

    private void ShowEncryptStep(int step)
    {
        EncryptStep1Panel.Visibility = step == 1 ? Visibility.Visible : Visibility.Collapsed;
        EncryptStep2Panel.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
        EncryptStep3Panel.Visibility = step == 3 ? Visibility.Visible : Visibility.Collapsed;
        EncryptOperationPanel.Visibility = Visibility.Collapsed;
        EncryptCompletePanel.Visibility = Visibility.Collapsed;
        EncryptStepIndicator.Visibility = Visibility.Visible;
        EncryptStepIndicator.Text = step switch
        {
            1 => "● Step 1  Choose file    ○ Step 2  Protection    ○ Step 3  Review",
            2 => "✓ Step 1  Choose file    ● Step 2  Protection    ○ Step 3  Review",
            _ => "✓ Step 1  Choose file    ✓ Step 2  Protection    ● Step 3  Review"
        };
    }

    private void ShowDecryptStep(int step)
    {
        DecryptStep1Panel.Visibility = step == 1 ? Visibility.Visible : Visibility.Collapsed;
        DecryptStep2Panel.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
        DecryptStep3Panel.Visibility = step == 3 ? Visibility.Visible : Visibility.Collapsed;
        DecryptOperationPanel.Visibility = Visibility.Collapsed;
        DecryptCompletePanel.Visibility = Visibility.Collapsed;
        DecryptStepIndicator.Visibility = Visibility.Visible;
        DecryptStepIndicator.Text = step switch
        {
            1 => "● Step 1  Choose file    ○ Step 2  Unlock    ○ Step 3  Review",
            2 => "✓ Step 1  Choose file    ● Step 2  Unlock    ○ Step 3  Review",
            _ => "✓ Step 1  Choose file    ✓ Step 2  Unlock    ● Step 3  Review"
        };
    }

    private void EncryptPassword_Changed(object sender, RoutedEventArgs e)
    {
        var password = EncryptPasswordBox.Password;
        var confirmation = EncryptConfirmPasswordBox.Password;

        var guidance = password.Length switch
        {
            < 12 => "Too short — use at least 12 characters",
            < 16 => "Meets the minimum; a longer password is recommended",
            < 24 => "Good length",
            _ => "Strong length"
        };

        if (!string.IsNullOrEmpty(confirmation))
            guidance += password == confirmation ? "  •  passwords match" : "  •  passwords do not match";

        EncryptPasswordStrengthText.Text = guidance;
    }

    private void GenerateEncryptPassword_Click(object sender, RoutedEventArgs e)
    {
        var password = _passwordGenerator.GeneratePassword(28);
        EncryptPasswordBox.Password = password;
        EncryptConfirmPasswordBox.Password = password;
        EncryptPasswordStrengthText.Text = "Strong generated password  •  passwords match";
        GlobalStatusText.Text = "● Strong password generated locally";
    }

    private bool ValidateEncryptPassword()
    {
        try
        {
            RequirePassword(EncryptPasswordBox.Password);
        }
        catch (ArgumentException ex)
        {
            ShowFriendlyError(ex.Message);
            return false;
        }

        if (!string.Equals(EncryptPasswordBox.Password, EncryptConfirmPasswordBox.Password, StringComparison.Ordinal))
        {
            ShowFriendlyError("The two passwords do not match. Re-enter the confirmation before continuing.");
            return false;
        }

        return true;
    }

    private void PopulateEncryptReview()
    {
        if (!File.Exists(EncryptSourceBox.Text))
            return;

        var info = new FileInfo(EncryptSourceBox.Text);
        EncryptReviewFileText.Text = info.Name;
        EncryptReviewSizeText.Text = $"{FormatBytes(info.Length)}  •  Recommended protection: XChaCha20-Poly1305 + Argon2id";
    }

    private void PopulateDecryptReview()
    {
        if (!File.Exists(DecryptSourceBox.Text))
            return;

        var info = new FileInfo(DecryptSourceBox.Text);
        DecryptReviewFileText.Text = info.Name;
        DecryptReviewSizeText.Text = $"Encrypted container size: {FormatBytes(info.Length)}";
        DecryptReviewDestinationText.Text = DecryptDestinationBox.Text;
    }

    private void RunEncryptPreflight_Click(object sender, RoutedEventArgs e) => RunEncryptPreflight(showDialog: true);
    private void RunDecryptPreflight_Click(object sender, RoutedEventArgs e) => RunDecryptPreflight(showDialog: true);

    private bool RunEncryptPreflight(bool showDialog)
    {
        try
        {
            var result = _preflight.Validate(EncryptSourceBox.Text, EncryptDestinationBox.Text);
            EncryptPreflightText.Text = $"✓ {result.Summary}";
            return true;
        }
        catch (Exception ex)
        {
            EncryptPreflightText.Text = $"⚠ {ex.Message}";
            if (showDialog)
                ShowFriendlyError(ex.Message);
            return false;
        }
    }

    private bool RunDecryptPreflight(bool showDialog)
    {
        try
        {
            var result = _preflight.Validate(DecryptSourceBox.Text, DecryptDestinationBox.Text);
            DecryptPreflightText.Text = $"✓ {result.Summary}";
            return true;
        }
        catch (Exception ex)
        {
            DecryptPreflightText.Text = $"⚠ {ex.Message}";
            if (showDialog)
                ShowFriendlyError(ex.Message);
            return false;
        }
    }

    private async void StartEncrypt_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidateEncryptPassword() || !RunEncryptPreflight(showDialog: true))
            return;

        if (_operationCts is not null)
        {
            ShowFriendlyError("Another encryption or decryption job is already running.");
            return;
        }

        _operationCts = new CancellationTokenSource();
        _operationStartedUtc = DateTimeOffset.UtcNow;
        SetOperationUi(isRunning: true, encrypting: true);
        ShowEncryptOperation();
        EncryptProgressBar.Value = 0;
        EncryptStatusText.Text = "Checking source and destination…";
        EncryptProgressDetails.Text = "Preflight checks passed. Preparing authenticated encryption.";
        SetEncryptStages("prepare");
        AddActivity("Encryption started", Path.GetFileName(EncryptSourceBox.Text));

        var progress = new Progress<CryptoProgress>(p => UpdateProgress(p, encrypting: true));

        try
        {
            SetEncryptStages("process");
            await _fileCrypto.EncryptFileAsync(
                EncryptSourceBox.Text,
                EncryptDestinationBox.Text,
                EncryptPasswordBox.Password,
                progress,
                _operationCts.Token,
                VerifyAfterEncryptCheck.IsChecked != false);

            SetEncryptStages("complete");
            EncryptProgressBar.Value = 100;
            EncryptStatusText.Text = "✓ Encryption complete and verified";
            EncryptProgressDetails.Text = "100% — encrypted output finalized safely";
            GlobalStatusText.Text = "● Ready   |   Last job: encryption successful   |   v0.2-dev";
            AddActivity("Encryption complete", Path.GetFileName(EncryptDestinationBox.Text));

            EncryptCompletePathText.Text = EncryptDestinationBox.Text;
            EncryptCompleteTimeText.Text = $"Completed in {FormatDuration(DateTimeOffset.UtcNow - _operationStartedUtc)}  •  verification passed";
            EncryptOperationPanel.Visibility = Visibility.Collapsed;
            EncryptCompletePanel.Visibility = Visibility.Visible;
            EncryptStepIndicator.Text = "✓ Step 1  Choose file    ✓ Step 2  Protection    ✓ Step 3  Complete";
        }
        catch (OperationCanceledException)
        {
            AddActivity("Encryption cancelled", Path.GetFileName(EncryptSourceBox.Text));
            GlobalStatusText.Text = "● Encryption cancelled safely   |   original unchanged";
            ShowEncryptStep(2);
            EncryptPasswordStrengthText.Text = "Encryption was cancelled. Re-enter your password when you are ready to try again.";
        }
        catch (Exception ex)
        {
            AddActivity("Encryption failed", Path.GetFileName(EncryptSourceBox.Text));
            GlobalStatusText.Text = "● Encryption needs attention";
            ShowEncryptStep(2);
            ShowFriendlyError(FriendlyOperationMessage(ex, "encrypt"));
        }
        finally
        {
            _operationCts.Dispose();
            _operationCts = null;
            SetOperationUi(isRunning: false, encrypting: true);
            EncryptPasswordBox.Clear();
            EncryptConfirmPasswordBox.Clear();
        }
    }

    private async void StartDecrypt_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            RequirePassword(DecryptPasswordBox.Password);
        }
        catch (ArgumentException ex)
        {
            ShowFriendlyError(ex.Message);
            return;
        }

        if (!RunDecryptPreflight(showDialog: true))
            return;

        if (_operationCts is not null)
        {
            ShowFriendlyError("Another encryption or decryption job is already running.");
            return;
        }

        _operationCts = new CancellationTokenSource();
        _operationStartedUtc = DateTimeOffset.UtcNow;
        SetOperationUi(isRunning: true, encrypting: false);
        ShowDecryptOperation();
        DecryptProgressBar.Value = 0;
        DecryptStatusText.Text = "Authenticating encrypted file…";
        DecryptProgressDetails.Text = "Preflight checks passed. Rice2k is validating the encrypted container.";
        SetDecryptStages("authenticate");
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

            SetDecryptStages("complete");
            DecryptProgressBar.Value = 100;
            DecryptStatusText.Text = "✓ Decryption complete";
            DecryptProgressDetails.Text = "100% — authentication and file-length checks passed";
            GlobalStatusText.Text = "● Ready   |   Last job: decryption successful   |   v0.2-dev";
            AddActivity("Decryption complete", Path.GetFileName(DecryptDestinationBox.Text));

            DecryptCompletePathText.Text = DecryptDestinationBox.Text;
            DecryptCompleteTimeText.Text = $"Completed in {FormatDuration(DateTimeOffset.UtcNow - _operationStartedUtc)}  •  authentication passed";
            DecryptOperationPanel.Visibility = Visibility.Collapsed;
            DecryptCompletePanel.Visibility = Visibility.Visible;
            DecryptStepIndicator.Text = "✓ Step 1  Choose file    ✓ Step 2  Unlock    ✓ Step 3  Complete";
        }
        catch (OperationCanceledException)
        {
            AddActivity("Decryption cancelled", Path.GetFileName(DecryptSourceBox.Text));
            GlobalStatusText.Text = "● Decryption cancelled safely   |   incomplete output removed";
            ShowDecryptStep(2);
        }
        catch (Exception ex)
        {
            AddActivity("Decryption failed", Path.GetFileName(DecryptSourceBox.Text));
            GlobalStatusText.Text = "● Decryption needs attention";
            ShowDecryptStep(2);
            ShowFriendlyError(FriendlyOperationMessage(ex, "decrypt"));
        }
        finally
        {
            _operationCts.Dispose();
            _operationCts = null;
            SetOperationUi(isRunning: false, encrypting: false);
            DecryptPasswordBox.Clear();
        }
    }

    private void ShowEncryptOperation()
    {
        EncryptStep1Panel.Visibility = Visibility.Collapsed;
        EncryptStep2Panel.Visibility = Visibility.Collapsed;
        EncryptStep3Panel.Visibility = Visibility.Collapsed;
        EncryptCompletePanel.Visibility = Visibility.Collapsed;
        EncryptOperationPanel.Visibility = Visibility.Visible;
        EncryptStepIndicator.Text = "✓ Step 1  Choose file    ✓ Step 2  Protection    ● Encrypting and verifying";
    }

    private void ShowDecryptOperation()
    {
        DecryptStep1Panel.Visibility = Visibility.Collapsed;
        DecryptStep2Panel.Visibility = Visibility.Collapsed;
        DecryptStep3Panel.Visibility = Visibility.Collapsed;
        DecryptCompletePanel.Visibility = Visibility.Collapsed;
        DecryptOperationPanel.Visibility = Visibility.Visible;
        DecryptStepIndicator.Text = "✓ Step 1  Choose file    ✓ Step 2  Unlock    ● Authenticating and restoring";
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

    private void EncryptAnother_Click(object sender, RoutedEventArgs e)
    {
        ResetEncryptWorkflow(clearSource: true);
        ShowEncryptStep(1);
    }

    private void DecryptAnother_Click(object sender, RoutedEventArgs e)
    {
        ResetDecryptWorkflow(clearSource: true);
        ShowDecryptStep(1);
    }

    private void OpenOutputFolder_Click(object sender, RoutedEventArgs e)
    {
        var path = sender is Button { Tag: string tag } && tag == "Decrypt"
            ? DecryptCompletePathText.Text
            : EncryptCompletePathText.Text;

        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            if (File.Exists(path))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{path}\"",
                    UseShellExecute = true
                });
            }
            else
            {
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
                    Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            ShowFriendlyError($"Rice2k could not open the output folder. {ex.Message}");
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
            GlobalStatusText.Text = "● Ready   |   Integrity calculation complete   |   v0.2-dev";
            AddActivity($"{algorithm} calculated", Path.GetFileName(IntegrityFileBox.Text));
        }
        catch (Exception ex)
        {
            ShowFriendlyError(ex.Message);
            GlobalStatusText.Text = "● Ready   |   v0.2-dev";
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

        if (encrypting)
        {
            if (p.Stage.Contains("Verifying", StringComparison.OrdinalIgnoreCase))
                SetEncryptStages("verify");
            else if (p.Stage.Contains("Complete", StringComparison.OrdinalIgnoreCase))
                SetEncryptStages("complete");
            else
                SetEncryptStages("process");
        }
        else
        {
            if (p.Stage.Contains("Complete", StringComparison.OrdinalIgnoreCase))
                SetDecryptStages("complete");
            else
                SetDecryptStages("process");
        }
    }

    private void SetEncryptStages(string stage)
    {
        EncryptStagePrepare.Text = stage == "prepare" ? "● Checking source and destination" : "✓ Source and destination checked";
        EncryptStageProcess.Text = stage switch
        {
            "prepare" => "○ Encrypting file data",
            "process" => "● Encrypting file data",
            _ => "✓ File data encrypted"
        };
        EncryptStageVerify.Text = stage switch
        {
            "verify" => "● Verifying encrypted data",
            "complete" => "✓ Encrypted data verified",
            _ => "○ Verifying encrypted data"
        };
        EncryptStageFinalize.Text = stage == "complete" ? "✓ Output finalized" : "○ Finalizing output";
    }

    private void SetDecryptStages(string stage)
    {
        DecryptStagePrepare.Text = "✓ Source and destination checked";
        DecryptStageAuthenticate.Text = stage switch
        {
            "authenticate" => "● Authenticating encrypted file",
            _ => "✓ Encrypted file authenticated"
        };
        DecryptStageProcess.Text = stage switch
        {
            "authenticate" => "○ Restoring file data",
            "process" => "● Restoring file data",
            _ => "✓ File data restored"
        };
        DecryptStageFinalize.Text = stage == "complete" ? "✓ Restored file finalized" : "○ Finalizing restored file";
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
    }

    private void ResetEncryptWorkflow(bool clearSource)
    {
        if (clearSource)
        {
            EncryptSourceBox.Clear();
            EncryptDestinationBox.Clear();
            EncryptSourceSummaryText.Text = "No file selected";
        }

        EncryptPasswordBox.Clear();
        EncryptConfirmPasswordBox.Clear();
        EncryptPasswordStrengthText.Text = "Enter at least 12 characters";
        EncryptPreflightText.Text = "Not checked yet";
        EncryptProgressBar.Value = 0;
        EncryptProgressDetails.Text = "Starting safety checks…";
        EncryptStatusText.Text = "Preparing…";
        EncryptCompletePathText.Text = string.Empty;
        EncryptCompleteTimeText.Text = string.Empty;
        SetEncryptStages("prepare");
        ShowEncryptStep(1);
    }

    private void ResetDecryptWorkflow(bool clearSource)
    {
        if (clearSource)
        {
            DecryptSourceBox.Clear();
            DecryptDestinationBox.Clear();
            DecryptSourceSummaryText.Text = "No encrypted file selected";
        }

        DecryptPasswordBox.Clear();
        DecryptPreflightText.Text = "Not checked yet";
        DecryptProgressBar.Value = 0;
        DecryptProgressDetails.Text = "Starting safety checks…";
        DecryptStatusText.Text = "Preparing…";
        DecryptCompletePathText.Text = string.Empty;
        DecryptCompleteTimeText.Text = string.Empty;
        SetDecryptStages("authenticate");
        ShowDecryptStep(1);
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

    private static string FriendlyOperationMessage(Exception ex, string verb)
    {
        return ex switch
        {
            CryptographicException => $"Rice2k could not {verb} the file because authentication failed. Check the password and make sure the encrypted file has not been modified.",
            UnauthorizedAccessException => $"Rice2k does not have permission to {verb} at the selected location. Choose another folder or adjust the file permissions.",
            IOException => ex.Message,
            _ => $"Rice2k could not {verb} the file. {ex.Message}"
        };
    }

    private static string SuggestDecryptedPath(string encryptedPath)
    {
        return encryptedPath.EndsWith(".r2kenc", StringComparison.OrdinalIgnoreCase)
            ? encryptedPath[..^7]
            : encryptedPath + ".decrypted";
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
