using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;

namespace Rice2k.Encryption;

public partial class MainWindow
{
    private bool _accessibilityUiInitialized;

    private void InitializeAccessibilityUi()
    {
        if (_accessibilityUiInitialized)
            return;

        _accessibilityUiInitialized = true;
        PreviewKeyDown += MainWindow_PreviewKeyDown;

        AutomationProperties.SetName(EncryptSourceBox, "File to encrypt");
        AutomationProperties.SetHelpText(EncryptSourceBox, "The original source file. Rice2k does not modify this file during encryption.");
        AutomationProperties.SetName(EncryptDestinationBox, "Encrypted output location");
        AutomationProperties.SetHelpText(EncryptDestinationBox, "The new Rice2k encrypted .r2kenc file that will be created.");
        AutomationProperties.SetName(EncryptPasswordBox, "Encryption password");
        AutomationProperties.SetHelpText(EncryptPasswordBox, "A password of at least 12 characters. Rice2k cannot recover a forgotten password.");
        AutomationProperties.SetName(EncryptConfirmPasswordBox, "Confirm encryption password");
        AutomationProperties.SetName(EncryptStartButton, "Encrypt now");
        AutomationProperties.SetHelpText(EncryptStartButton, "Run safety checks, encrypt the selected source and verify the encrypted output before finalizing it.");

        AutomationProperties.SetName(DecryptSourceBox, "Rice2k encrypted file");
        AutomationProperties.SetHelpText(DecryptSourceBox, "A .r2kenc file to authenticate and decrypt.");
        AutomationProperties.SetName(DecryptDestinationBox, "Restored file location");
        AutomationProperties.SetHelpText(DecryptDestinationBox, "Where Rice2k will save the restored plaintext file.");
        AutomationProperties.SetName(DecryptPasswordBox, "Decryption password");
        AutomationProperties.SetName(DecryptStartButton, "Decrypt now");
        AutomationProperties.SetHelpText(DecryptStartButton, "Authenticate the encrypted container and restore its contents to a new output file.");

        AutomationProperties.SetName(GlobalStatusText, "Rice2k operation status");
        AutomationProperties.SetLiveSetting(GlobalStatusText, AutomationLiveSetting.Polite);
        AutomationProperties.SetName(ActivityList, "Rice2k activity list");
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0)
        {
            if (e.Key == Key.F1)
            {
                ShowKeyboardHelp();
                e.Handled = true;
            }
            return;
        }

        switch (e.Key)
        {
            case Key.E:
                NavigateTo("Encrypt");
                e.Handled = true;
                break;
            case Key.D:
                NavigateTo("Decrypt");
                e.Handled = true;
                break;
            case Key.B:
                OpenBatchQueue();
                e.Handled = true;
                break;
            case Key.T:
                NavigateTo("Text");
                e.Handled = true;
                break;
            case Key.I:
                NavigateTo("Integrity");
                e.Handled = true;
                break;
            case Key.OemComma:
                NavigateTo("Settings");
                e.Handled = true;
                break;
        }
    }

    private void ShowKeyboardHelp()
    {
        MessageBox.Show(
            this,
            "Keyboard shortcuts\n\nCtrl+E   Encrypt file\nCtrl+D   Decrypt file\nCtrl+B   Batch Queue\nCtrl+T   Text Encryption\nCtrl+I   File Integrity\nCtrl+,   Settings\nF1       Show this help\n\nUse Tab and Shift+Tab to move through controls. Enter activates the focused button, and Space toggles check boxes.",
            "Rice2k keyboard shortcuts",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }
}
