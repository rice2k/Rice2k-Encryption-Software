using System.ComponentModel;
using System.Windows;
using Microsoft.Win32;
using Rice2k.Encryption.Models;
using Rice2k.Encryption.Services;

namespace Rice2k.Encryption;

public partial class DigitalSignatureWindow : Window
{
    private readonly IdentityService _identityService = new();
    private readonly FileSignatureService _signatureService = new();
    private CancellationTokenSource? _operationCts;
    private bool _busy;
    private bool _closeWhenFinished;

    public DigitalSignatureWindow()
    {
        InitializeComponent();
        Closing += DigitalSignatureWindow_Closing;
    }

    private void BrowseSignSource_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
            return;

        var dialog = new OpenFileDialog { Title = "Choose a file to sign", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true)
            return;
        SignSourceBox.Text = dialog.FileName;
        SignatureOutputBox.Text = CreateNonCollidingPath(dialog.FileName + ".r2ksig");
    }

    private void BrowseSignatureOutput_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || !File.Exists(SignSourceBox.Text))
            return;
        var dialog = new SaveFileDialog
        {
            Title = "Save Rice2k detached signature",
            Filter = "Rice2k signatures (*.r2ksig)|*.r2ksig",
            DefaultExt = ".r2ksig",
            AddExtension = true,
            OverwritePrompt = false,
            FileName = Path.GetFileName(SignatureOutputBox.Text)
        };
        if (dialog.ShowDialog(this) == true)
            SignatureOutputBox.Text = CreateNonCollidingPath(dialog.FileName);
    }

    private void BrowseSigningIdentity_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
            return;

        var dialog = new OpenFileDialog
        {
            Title = "Choose signing private identity",
            Filter = "Rice2k private identities (*.r2kid)|*.r2kid|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) == true)
            SigningIdentityBox.Text = dialog.FileName;
    }

    private async void SignFile_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
            return;
        if (!File.Exists(SignSourceBox.Text))
        {
            ShowError("Choose the file you want to sign.");
            return;
        }
        if (!File.Exists(SigningIdentityBox.Text))
        {
            ShowError("Choose the .r2kid private identity that will sign the file.");
            return;
        }

        var password = SigningIdentityPasswordBox.Password;
        if (string.IsNullOrWhiteSpace(password))
        {
            ShowError("Enter the private identity package password.");
            return;
        }
        if (string.IsNullOrWhiteSpace(SignatureOutputBox.Text))
        {
            ShowError("Choose where to save the .r2ksig file.");
            return;
        }

        SigningIdentityPasswordBox.Clear();
        BeginOperation("Unlocking signing identity…");
        Rice2kIdentity? identity = null;
        try
        {
            identity = await _identityService.ImportPrivateAsync(
                SigningIdentityBox.Text,
                password,
                _operationCts!.Token);
            _operationCts.Token.ThrowIfCancellationRequested();

            SignStatusText.Text = "Hashing and signing file…";
            await _signatureService.SignAsync(
                SignSourceBox.Text,
                SignatureOutputBox.Text,
                identity,
                _operationCts.Token);
            _operationCts.Token.ThrowIfCancellationRequested();

            SignStatusText.Text = "✓ Signature created";
            SignDetailText.Text =
                $"Signed by key {identity.Fingerprint}. Share the .r2ksig beside the file. Recipients should compare this fingerprint against a trusted copy of your .r2kpub if identity matters.";
        }
        catch (OperationCanceledException)
        {
            SignStatusText.Text = "Signature operation cancelled safely";
            SignDetailText.Text = "No incomplete signature output is intentionally retained.";
        }
        catch (Exception ex)
        {
            SignStatusText.Text = "⚠ Signature was not created";
            ShowError(ex.Message);
        }
        finally
        {
            identity?.Dispose();
            password = string.Empty;
            SigningIdentityPasswordBox.Clear();
            EndOperation();
        }
    }

    private void BrowseVerifySource_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
            return;

        var dialog = new OpenFileDialog { Title = "Choose the file to verify", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true)
            return;
        VerifySourceBox.Text = dialog.FileName;
        var adjacent = dialog.FileName + ".r2ksig";
        if (File.Exists(adjacent))
            VerifySignatureBox.Text = adjacent;
    }

    private void BrowseVerifySignature_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
            return;

        var dialog = new OpenFileDialog
        {
            Title = "Choose Rice2k detached signature",
            Filter = "Rice2k signatures (*.r2ksig)|*.r2ksig|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) == true)
            VerifySignatureBox.Text = dialog.FileName;
    }

    private void BrowseExpectedIdentity_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
            return;

        var dialog = new OpenFileDialog
        {
            Title = "Choose trusted Rice2k public identity",
            Filter = "Rice2k public identities (*.r2kpub)|*.r2kpub|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) == true)
            ExpectedPublicIdentityBox.Text = dialog.FileName;
    }

    private void ClearExpectedIdentity_Click(object sender, RoutedEventArgs e)
    {
        if (!_busy)
            ExpectedPublicIdentityBox.Clear();
    }

    private async void VerifySignature_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
            return;
        if (!File.Exists(VerifySourceBox.Text))
        {
            ShowError("Choose the file you want to verify.");
            return;
        }
        if (!File.Exists(VerifySignatureBox.Text))
        {
            ShowError("Choose its .r2ksig file.");
            return;
        }

        BeginOperation("Verifying…");
        Rice2kPublicIdentity? expectedIdentity = null;
        try
        {
            VerifyStatusText.Text = "Verifying…";
            VerifyDetailText.Text = "Checking the signature document and hashing the current file.";

            if (!string.IsNullOrWhiteSpace(ExpectedPublicIdentityBox.Text))
            {
                if (!File.Exists(ExpectedPublicIdentityBox.Text))
                    throw new FileNotFoundException("The selected trusted .r2kpub file could not be found.");
                expectedIdentity = await _identityService.ImportPublicAsync(ExpectedPublicIdentityBox.Text, _operationCts!.Token);
                _operationCts.Token.ThrowIfCancellationRequested();
            }

            var result = await _signatureService.VerifyAsync(
                VerifySourceBox.Text,
                VerifySignatureBox.Text,
                expectedIdentity,
                _operationCts!.Token);
            _operationCts.Token.ThrowIfCancellationRequested();

            SignerNameText.Text = result.SignerName;
            SignerFingerprintText.Text = result.SignerFingerprint;
            VerifiedFileNameText.Text = result.CurrentNameMatchesOriginal
                ? result.OriginalFileName
                : $"Signed as: {result.OriginalFileName}   •   Current name: {result.CurrentFileName}";

            if (!result.FileContentMatches)
            {
                VerifyStatusText.Text = "✗ File does not match the signature";
                VerifyDetailText.Text = "The .r2ksig itself is cryptographically valid, but the current file length/hash differs from the file that was signed. Do not treat this file as verified.";
                return;
            }

            if (result.MatchesExpectedIdentity == false)
            {
                VerifyStatusText.Text = "✗ Signature does not match the trusted identity";
                VerifyDetailText.Text = "The file matches the embedded signing key, but that key does not match the selected .r2kpub identity. Do not treat the signer identity as verified.";
                return;
            }

            if (result.MatchesExpectedIdentity == true)
            {
                VerifyStatusText.Text = "✓ File and trusted signing identity verified";
                VerifyDetailText.Text = $"File contents match the signature, and the signing/encryption key set matches the selected trusted public identity fingerprint. Signed {result.SignedUtc.ToLocalTime():g}.";
            }
            else
            {
                VerifyStatusText.Text = "✓ File matches signature • identity not independently established";
                VerifyDetailText.Text = $"The file contents match the valid embedded signing key. No trusted .r2kpub was supplied, so Rice2k is not claiming who owns fingerprint {result.SignerFingerprint}.";
            }
        }
        catch (OperationCanceledException)
        {
            VerifyStatusText.Text = "Signature verification cancelled";
            VerifyDetailText.Text = "No verification result was retained.";
            SignerNameText.Text = "—";
            SignerFingerprintText.Text = "—";
            VerifiedFileNameText.Text = "—";
        }
        catch (Exception ex)
        {
            VerifyStatusText.Text = "✗ Signature verification failed";
            VerifyDetailText.Text = "Rice2k could not establish a valid detached signature for this file.";
            SignerNameText.Text = "—";
            SignerFingerprintText.Text = "—";
            VerifiedFileNameText.Text = "—";
            ShowError(ex.Message);
        }
        finally
        {
            EndOperation();
        }
    }

    private void BeginOperation(string status)
    {
        _operationCts?.Dispose();
        _operationCts = new CancellationTokenSource();
        _busy = true;
        SignStatusText.Text = status;
    }

    private void EndOperation()
    {
        _busy = false;
        _operationCts?.Dispose();
        _operationCts = null;

        if (_closeWhenFinished)
        {
            _closeWhenFinished = false;
            Dispatcher.InvokeAsync(Close);
        }
    }

    private void DigitalSignatureWindow_Closing(object? sender, CancelEventArgs e)
    {
        SigningIdentityPasswordBox.Clear();
        if (!_busy)
            return;

        e.Cancel = true;
        _closeWhenFinished = true;
        SignStatusText.Text = "Cancelling the active signature operation before closing…";
        VerifyStatusText.Text = "Cancelling the active signature operation before closing…";
        _operationCts?.Cancel();
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
        throw new IOException("Rice2k could not create a unique signature filename.");
    }

    private void ShowError(string message) =>
        MessageBox.Show(this, message, "Rice2k Digital Signatures", MessageBoxButton.OK, MessageBoxImage.Warning);
}
