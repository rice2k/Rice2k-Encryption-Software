using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Microsoft.Win32;
using Rice2k.Encryption.Models;
using Rice2k.Encryption.Services;

namespace Rice2k.Encryption;

public partial class MainWindow
{
    private readonly KeyFileEncryptionService _keyFileCrypto = new();
    private readonly KeyManagerService _workflowKeyManager = new();
    private bool _keyFileProtectionUiInitialized;
    private bool _keyFileOperationActive;
    private bool _decryptRequiresKeyFile;

    private RadioButton? _encryptPasswordOnlyRadio;
    private RadioButton? _encryptPasswordKeyRadio;
    private StackPanel? _encryptKeyPanel;
    private TextBox? _encryptKeyPathBox;
    private PasswordBox? _encryptKeyPackagePasswordBox;
    private TextBlock? _encryptProtectionReviewText;

    private StackPanel? _decryptKeyPanel;
    private TextBox? _decryptKeyPathBox;
    private PasswordBox? _decryptKeyPackagePasswordBox;
    private TextBlock? _decryptKeyRequirementText;
    private TextBlock? _decryptProtectionReviewText;

    private void InitializeKeyFileProtectionUi()
    {
        if (_keyFileProtectionUiInitialized)
            return;

        _keyFileProtectionUiInitialized = true;
        BuildEncryptKeyFileUi();
        BuildDecryptKeyFileUi();

        EncryptStartButton.Click -= StartEncrypt_Click;
        EncryptStartButton.Click += StartEncryptProtectionAware_Click;
        DecryptStartButton.Click -= StartDecrypt_Click;
        DecryptStartButton.Click += StartDecryptProtectionAware_Click;

        DecryptSourceBox.TextChanged += (_, _) => DetectDecryptProtection();
        DetectDecryptProtection();
    }

    private void BuildEncryptKeyFileUi()
    {
        if (EncryptStep2Panel.Child is not StackPanel root)
            return;

        var protectionCard = CreateNestedCard();
        var protectionPanel = new StackPanel();
        protectionPanel.Children.Add(new TextBlock
        {
            Text = "How should this file be protected?",
            FontWeight = FontWeights.SemiBold,
            FontSize = 15
        });

        _encryptPasswordOnlyRadio = new RadioButton
        {
            Content = "Password only — simplest option",
            GroupName = "FileProtectionMode",
            IsChecked = true,
            Margin = new Thickness(0, 10, 0, 5)
        };
        _encryptPasswordKeyRadio = new RadioButton
        {
            Content = "Password + Rice2k key file — requires both to decrypt",
            GroupName = "FileProtectionMode",
            Margin = new Thickness(0, 3, 0, 8)
        };
        _encryptPasswordOnlyRadio.Checked += (_, _) => UpdateEncryptKeyModeUi();
        _encryptPasswordKeyRadio.Checked += (_, _) => UpdateEncryptKeyModeUi();
        protectionPanel.Children.Add(_encryptPasswordOnlyRadio);
        protectionPanel.Children.Add(_encryptPasswordKeyRadio);

        _encryptKeyPanel = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 6, 0, 0) };
        _encryptKeyPanel.Children.Add(new TextBlock
        {
            Text = "Select an encrypted .r2kkey package. Rice2k unlocks it only in memory for this operation.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.82,
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 8)
        });
        _encryptKeyPanel.Children.Add(new Label { Content = "Key package" });

        var keyRow = new Grid();
        keyRow.ColumnDefinitions.Add(new ColumnDefinition());
        keyRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _encryptKeyPathBox = new TextBox { IsReadOnly = true };
        AutomationProperties.SetName(_encryptKeyPathBox, "Selected encryption key package");
        var browse = new Button { Content = "Browse…", Margin = new Thickness(10, 0, 0, 0) };
        browse.Click += BrowseEncryptKeyPackage_Click;
        Grid.SetColumn(browse, 1);
        keyRow.Children.Add(_encryptKeyPathBox);
        keyRow.Children.Add(browse);
        _encryptKeyPanel.Children.Add(keyRow);
        _encryptKeyPanel.Children.Add(new Label { Content = "Key-package password", Margin = new Thickness(0, 8, 0, 0) });
        _encryptKeyPackagePasswordBox = new PasswordBox();
        AutomationProperties.SetName(_encryptKeyPackagePasswordBox, "Encryption key package password");
        AutomationProperties.SetHelpText(_encryptKeyPackagePasswordBox, "Password used to unlock the selected .r2kkey package. This is separate from the file password above.");
        _encryptKeyPanel.Children.Add(_encryptKeyPackagePasswordBox);
        _encryptKeyPanel.Children.Add(new TextBlock
        {
            Text = "You will need the file password, this same key package, and its package password to decrypt the file later.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Margin = new Thickness(0, 7, 0, 0)
        });

        protectionPanel.Children.Add(_encryptKeyPanel);
        protectionCard.Child = protectionPanel;
        root.Children.Insert(Math.Min(3, root.Children.Count), protectionCard);

        if (EncryptStep3Panel.Child is StackPanel reviewRoot)
        {
            _encryptProtectionReviewText = new TextBlock
            {
                Text = "Protection: Password only",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 12)
            };
            reviewRoot.Children.Insert(Math.Min(4, reviewRoot.Children.Count), _encryptProtectionReviewText);
        }
    }

    private void BuildDecryptKeyFileUi()
    {
        if (DecryptStep2Panel.Child is not StackPanel root)
            return;

        _decryptKeyPanel = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 12, 0, 0) };
        _decryptKeyRequirementText = new TextBlock
        {
            Text = "This container also requires a Rice2k key package.",
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 7)
        };
        _decryptKeyPanel.Children.Add(_decryptKeyRequirementText);
        _decryptKeyPanel.Children.Add(new Label { Content = "Required key package" });

        var keyRow = new Grid();
        keyRow.ColumnDefinitions.Add(new ColumnDefinition());
        keyRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _decryptKeyPathBox = new TextBox { IsReadOnly = true };
        AutomationProperties.SetName(_decryptKeyPathBox, "Selected decryption key package");
        var browse = new Button { Content = "Browse…", Margin = new Thickness(10, 0, 0, 0) };
        browse.Click += BrowseDecryptKeyPackage_Click;
        Grid.SetColumn(browse, 1);
        keyRow.Children.Add(_decryptKeyPathBox);
        keyRow.Children.Add(browse);
        _decryptKeyPanel.Children.Add(keyRow);
        _decryptKeyPanel.Children.Add(new Label { Content = "Key-package password", Margin = new Thickness(0, 8, 0, 0) });
        _decryptKeyPackagePasswordBox = new PasswordBox();
        AutomationProperties.SetName(_decryptKeyPackagePasswordBox, "Decryption key package password");
        _decryptKeyPanel.Children.Add(_decryptKeyPackagePasswordBox);
        _decryptKeyPanel.Children.Add(new TextBlock
        {
            Text = "Rice2k checks the key fingerprint before decrypting file data. The key package itself remains password-protected on disk.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Opacity = 0.82,
            Margin = new Thickness(0, 7, 0, 0)
        });

        root.Children.Insert(Math.Min(4, root.Children.Count), _decryptKeyPanel);

        if (DecryptStep3Panel.Child is StackPanel reviewRoot)
        {
            _decryptProtectionReviewText = new TextBlock
            {
                Text = "Protection: Password only",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 12)
            };
            reviewRoot.Children.Insert(Math.Min(4, reviewRoot.Children.Count), _decryptProtectionReviewText);
        }
    }

    private Border CreateNestedCard()
    {
        var card = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 10, 0, 2),
            BorderThickness = new Thickness(1)
        };
        if (TryFindResource("SurfaceBrush") is System.Windows.Media.Brush surface)
            card.Background = surface;
        if (TryFindResource("BorderBrush") is System.Windows.Media.Brush border)
            card.BorderBrush = border;
        return card;
    }

    private void UpdateEncryptKeyModeUi()
    {
        var useKey = _encryptPasswordKeyRadio?.IsChecked == true;
        if (_encryptKeyPanel is not null)
            _encryptKeyPanel.Visibility = useKey ? Visibility.Visible : Visibility.Collapsed;
        if (_encryptProtectionReviewText is not null)
            _encryptProtectionReviewText.Text = useKey
                ? "Protection: Password + encrypted Rice2k key file"
                : "Protection: Password only";
    }

    private void DetectDecryptProtection()
    {
        var path = DecryptSourceBox.Text;
        _decryptRequiresKeyFile = File.Exists(path) && KeyFileEncryptionService.IsKeyFileProtectedContainer(path);
        var fingerprint = _decryptRequiresKeyFile
            ? KeyFileEncryptionService.TryGetRequiredKeyFingerprint(path)
            : null;

        if (_decryptKeyPanel is not null)
            _decryptKeyPanel.Visibility = _decryptRequiresKeyFile ? Visibility.Visible : Visibility.Collapsed;
        if (_decryptKeyRequirementText is not null)
            _decryptKeyRequirementText.Text = fingerprint is { Length: > 0 }
                ? $"This file requires password + key file. Required key fingerprint: {fingerprint}"
                : "This file requires password + a Rice2k .r2kkey package.";
        if (_decryptProtectionReviewText is not null)
            _decryptProtectionReviewText.Text = _decryptRequiresKeyFile
                ? $"Protection: Password + key file{(fingerprint is { Length: > 0 } ? $"  •  {fingerprint}" : string.Empty)}"
                : "Protection: Password only";
    }

    private void BrowseEncryptKeyPackage_Click(object sender, RoutedEventArgs e)
    {
        if (_encryptKeyPathBox is null)
            return;
        var dialog = CreateKeyPackageOpenDialog("Choose the Rice2k key package required for this encrypted file");
        if (dialog.ShowDialog(this) == true)
            _encryptKeyPathBox.Text = dialog.FileName;
    }

    private void BrowseDecryptKeyPackage_Click(object sender, RoutedEventArgs e)
    {
        if (_decryptKeyPathBox is null)
            return;
        var dialog = CreateKeyPackageOpenDialog("Choose the Rice2k key package needed to decrypt this file");
        if (dialog.ShowDialog(this) == true)
            _decryptKeyPathBox.Text = dialog.FileName;
    }

    private static OpenFileDialog CreateKeyPackageOpenDialog(string title) => new()
    {
        Title = title,
        Filter = "Rice2k key packages (*.r2kkey)|*.r2kkey|All files (*.*)|*.*",
        CheckFileExists = true,
        Multiselect = false
    };

    private async void StartEncryptProtectionAware_Click(object sender, RoutedEventArgs e)
    {
        if (_encryptPasswordKeyRadio?.IsChecked != true)
        {
            _keyFileOperationActive = false;
            StartEncrypt_Click(sender, e);
            return;
        }

        if (!ValidateEncryptPassword())
            return;
        if (_encryptKeyPathBox is null || !File.Exists(_encryptKeyPathBox.Text))
        {
            ShowFriendlyError("Choose the .r2kkey package that should be required for this encrypted file.");
            return;
        }
        if (_encryptKeyPackagePasswordBox is null || string.IsNullOrWhiteSpace(_encryptKeyPackagePasswordBox.Password))
        {
            ShowFriendlyError("Enter the password that unlocks the selected .r2kkey package.");
            return;
        }
        if (!ResolveLateDestinationCollision(
                EncryptDestinationBox.Text,
                path => { EncryptDestinationBox.Text = path; PopulateEncryptReview(); RunEncryptPreflight(showDialog: false); },
                () => BrowseEncryptDestination_Click(EncryptStartButton, new RoutedEventArgs())))
            return;
        if (!RunEncryptPreflight(showDialog: true))
            return;
        if (_operationCts is not null)
        {
            ShowFriendlyError("Another encryption or decryption job is already running.");
            return;
        }

        _operationCts = new CancellationTokenSource();
        _operationStartedUtc = DateTimeOffset.UtcNow;
        _keyFileOperationActive = true;
        SetOperationUi(isRunning: true, encrypting: true);
        ShowEncryptOperation();
        EncryptProgressBar.Value = 0;
        EncryptStatusText.Text = "Unlocking key package…";
        EncryptProgressDetails.Text = "The encrypted .r2kkey is being authenticated in memory before file encryption begins.";
        SetEncryptStages("prepare");
        AddActivity("Password + key-file encryption started", Path.GetFileName(EncryptSourceBox.Text));

        ManagedKey? managedKey = null;
        try
        {
            managedKey = await _workflowKeyManager.ImportAsync(
                _encryptKeyPathBox.Text,
                _encryptKeyPackagePasswordBox.Password,
                _operationCts.Token);

            EncryptStatusText.Text = $"Key package authenticated • {managedKey.Fingerprint}";
            var progress = new Progress<CryptoProgress>(p => UpdateProgress(p, encrypting: true));
            SetEncryptStages("process");
            await _keyFileCrypto.EncryptFileAsync(
                EncryptSourceBox.Text,
                EncryptDestinationBox.Text,
                EncryptPasswordBox.Password,
                managedKey,
                progress,
                _operationCts.Token,
                VerifyAfterEncryptCheck.IsChecked != false);

            SetEncryptStages("complete");
            EncryptProgressBar.Value = 100;
            EncryptStatusText.Text = "✓ Password + key-file encryption complete";
            EncryptProgressDetails.Text = "100% — encrypted output authenticated, verified, and finalized safely";
            GlobalStatusText.Text = "● Ready   |   Last job: password + key-file encryption successful   |   v0.3-dev";
            AddActivity("Password + key-file encryption complete", Path.GetFileName(EncryptDestinationBox.Text));
            EncryptCompletePathText.Text = EncryptDestinationBox.Text;
            EncryptCompleteTimeText.Text = $"Completed in {FormatDuration(DateTimeOffset.UtcNow - _operationStartedUtc)}  •  verification passed  •  key {managedKey.Fingerprint}";
            EncryptOperationPanel.Visibility = Visibility.Collapsed;
            EncryptCompletePanel.Visibility = Visibility.Visible;
            EncryptStepIndicator.Text = "✓ Step 1  Choose file    ✓ Step 2  Password + Key    ✓ Step 3  Complete";
        }
        catch (OperationCanceledException)
        {
            AddActivity("Encryption cancelled", Path.GetFileName(EncryptSourceBox.Text));
            GlobalStatusText.Text = "● Encryption cancelled safely   |   original unchanged";
            ShowEncryptStep(2);
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
            managedKey?.Dispose();
            _keyFileCrypto.Resume();
            _keyFileOperationActive = false;
            _operationCts?.Dispose();
            _operationCts = null;
            SetOperationUi(isRunning: false, encrypting: true);
            EncryptPasswordBox.Clear();
            EncryptConfirmPasswordBox.Clear();
            _encryptKeyPackagePasswordBox?.Clear();
        }
    }

    private async void StartDecryptProtectionAware_Click(object sender, RoutedEventArgs e)
    {
        DetectDecryptProtection();
        if (!_decryptRequiresKeyFile)
        {
            _keyFileOperationActive = false;
            StartDecrypt_Click(sender, e);
            return;
        }

        try
        {
            RequirePassword(DecryptPasswordBox.Password);
        }
        catch (ArgumentException ex)
        {
            ShowFriendlyError(ex.Message);
            return;
        }

        if (_decryptKeyPathBox is null || !File.Exists(_decryptKeyPathBox.Text))
        {
            ShowFriendlyError("This encrypted file requires a .r2kkey package. Choose the matching key package before decrypting.");
            return;
        }
        if (_decryptKeyPackagePasswordBox is null || string.IsNullOrWhiteSpace(_decryptKeyPackagePasswordBox.Password))
        {
            ShowFriendlyError("Enter the password that unlocks the selected .r2kkey package.");
            return;
        }
        if (!ResolveLateDestinationCollision(
                DecryptDestinationBox.Text,
                path => { DecryptDestinationBox.Text = path; PopulateDecryptReview(); RunDecryptPreflight(showDialog: false); },
                () => BrowseDecryptDestination_Click(DecryptStartButton, new RoutedEventArgs())))
            return;
        if (!RunDecryptPreflight(showDialog: true))
            return;
        if (_operationCts is not null)
        {
            ShowFriendlyError("Another encryption or decryption job is already running.");
            return;
        }

        _operationCts = new CancellationTokenSource();
        _operationStartedUtc = DateTimeOffset.UtcNow;
        _keyFileOperationActive = true;
        SetOperationUi(isRunning: true, encrypting: false);
        ShowDecryptOperation();
        DecryptProgressBar.Value = 0;
        DecryptStatusText.Text = "Unlocking required key package…";
        DecryptProgressDetails.Text = "Rice2k is authenticating the selected .r2kkey package in memory.";
        SetDecryptStages("authenticate");
        AddActivity("Password + key-file decryption started", Path.GetFileName(DecryptSourceBox.Text));

        ManagedKey? managedKey = null;
        try
        {
            managedKey = await _workflowKeyManager.ImportAsync(
                _decryptKeyPathBox.Text,
                _decryptKeyPackagePasswordBox.Password,
                _operationCts.Token);

            var required = KeyFileEncryptionService.TryGetRequiredKeyFingerprint(DecryptSourceBox.Text);
            if (required is { Length: > 0 } && !string.Equals(required, managedKey.Fingerprint, StringComparison.Ordinal))
                throw new System.Security.Cryptography.CryptographicException($"This file requires key {required}. The selected key package contains {managedKey.Fingerprint}.");

            var progress = new Progress<CryptoProgress>(p => UpdateProgress(p, encrypting: false));
            await _keyFileCrypto.DecryptFileAsync(
                DecryptSourceBox.Text,
                DecryptDestinationBox.Text,
                DecryptPasswordBox.Password,
                managedKey,
                progress,
                _operationCts.Token);

            SetDecryptStages("complete");
            DecryptProgressBar.Value = 100;
            DecryptStatusText.Text = "✓ Password + key-file decryption complete";
            DecryptProgressDetails.Text = "100% — password, key fingerprint, authentication, and file-length checks passed";
            GlobalStatusText.Text = "● Ready   |   Last job: password + key-file decryption successful   |   v0.3-dev";
            AddActivity("Password + key-file decryption complete", Path.GetFileName(DecryptDestinationBox.Text));
            DecryptCompletePathText.Text = DecryptDestinationBox.Text;
            DecryptCompleteTimeText.Text = $"Completed in {FormatDuration(DateTimeOffset.UtcNow - _operationStartedUtc)}  •  key {managedKey.Fingerprint}";
            DecryptOperationPanel.Visibility = Visibility.Collapsed;
            DecryptCompletePanel.Visibility = Visibility.Visible;
            DecryptStepIndicator.Text = "✓ Step 1  Choose file    ✓ Step 2  Password + Key    ✓ Step 3  Complete";
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
            managedKey?.Dispose();
            _keyFileCrypto.Resume();
            _keyFileOperationActive = false;
            _operationCts?.Dispose();
            _operationCts = null;
            SetOperationUi(isRunning: false, encrypting: false);
            DecryptPasswordBox.Clear();
            _decryptKeyPackagePasswordBox?.Clear();
        }
    }
}
