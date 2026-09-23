using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

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
        var modifiers = Keyboard.Modifiers;
        if ((modifiers & ModifierKeys.Control) == 0)
        {
            if (e.Key == Key.F1)
            {
                ShowKeyboardHelp();
                e.Handled = true;
            }
            else if (e.Key == Key.F6)
            {
                if ((modifiers & ModifierKeys.Shift) != 0)
                    FocusNavigation();
                else
                    FocusActivePage();
                e.Handled = true;
            }
            return;
        }

        if ((modifiers & ModifierKeys.Shift) != 0)
        {
            if (e.Key == Key.P)
            {
                PrivacyQuickToggle_Click(this, new RoutedEventArgs());
                SyncPrivacySettingsPanel(_appSettingsService.Load());
                e.Handled = true;
                return;
            }

            if (e.Key == Key.S)
            {
                OpenSecurityCenter();
                e.Handled = true;
                return;
            }
        }

        switch (e.Key)
        {
            case Key.H:
                NavigateToAndFocus("Home");
                e.Handled = true;
                break;
            case Key.E:
                NavigateToAndFocus("Encrypt");
                e.Handled = true;
                break;
            case Key.D:
                NavigateToAndFocus("Decrypt");
                e.Handled = true;
                break;
            case Key.B:
                OpenBatchQueue();
                e.Handled = true;
                break;
            case Key.T:
                NavigateToAndFocus("Text");
                e.Handled = true;
                break;
            case Key.V:
                NavigateToAndFocus("Vault");
                e.Handled = true;
                break;
            case Key.K:
                NavigateToAndFocus("Passwords");
                e.Handled = true;
                break;
            case Key.I:
                NavigateToAndFocus("Integrity");
                e.Handled = true;
                break;
            case Key.L:
                if (_appLockSettings.AppLockEnabled && _appLockCredentialService.IsConfigured())
                    RequestAppLock("Rice2k was locked with Ctrl+L.");
                else
                {
                    NavigateToAndFocus("Settings");
                    GlobalStatusText.Text = "● Configure App Lock in Settings before using Ctrl+L.";
                }
                e.Handled = true;
                break;
            case Key.OemComma:
                NavigateToAndFocus("Settings", focusSettingsSearch: true);
                e.Handled = true;
                break;
        }
    }

    private void NavigateToAndFocus(string tag, bool focusSettingsSearch = false)
    {
        NavigateTo(tag);
        Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(() =>
            {
                if (focusSettingsSearch && SettingsPage.Content is StackPanel root)
                {
                    var settingsPanel = root.Children.OfType<SettingsSearchPanel>().FirstOrDefault();
                    if (settingsPanel?.FocusSearchBox() == true)
                        return;
                }
                FocusActivePage();
            }));
    }

    private void FocusActivePage()
    {
        var page = AllPages().FirstOrDefault(candidate => candidate.Visibility == Visibility.Visible);
        if (page is null)
            return;

        var target = FindVisualChildren<Control>(page)
            .FirstOrDefault(control => control.Focusable && control.IsEnabled && control.IsVisible && control.IsTabStop);
        target?.Focus();
    }

    private void FocusNavigation()
    {
        var target = FindVisualChildren<Button>(this)
            .FirstOrDefault(button =>
                button.IsVisible &&
                button.IsEnabled &&
                button.Tag is string tag &&
                tag is "Home" or "Encrypt" or "Decrypt" or "Text" or "Vault" or "Passwords" or "Integrity" or "Recovery" or "Activity" or "Settings");
        target?.Focus();
    }

    private void ShowKeyboardHelp()
    {
        MessageBox.Show(
            this,
            "Keyboard shortcuts\n\nCtrl+H   Command Center\nCtrl+E   Encrypt file\nCtrl+D   Decrypt file\nCtrl+B   Batch Queue\nCtrl+T   Text Encryption\nCtrl+V   Secure Vault\nCtrl+K   Passwords & Keys\nCtrl+I   File Integrity\nCtrl+L   Lock Rice2k\nCtrl+Shift+P   Toggle Privacy Mode\nCtrl+Shift+S   Security Center\nCtrl+,   Settings / settings search\nF6       Move focus into active page\nShift+F6 Move focus to navigation\nF1       Show this help\n\nUse Tab and Shift+Tab to move through controls. Enter activates the focused button, and Space toggles check boxes.",
            "Rice2k keyboard shortcuts",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }
}
