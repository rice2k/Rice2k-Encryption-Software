using System.ComponentModel;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Input;
using Rice2k.Encryption.Services;

namespace Rice2k.Encryption;

public partial class AppLockWindow : Window
{
    private readonly AppLockCredentialService _credentialService;
    private readonly bool _reduceMotion;
    private bool _unlocked;
    private bool _allowClose;
    private bool _busy;
    private int _failedAttempts;

    public AppLockWindow(AppLockCredentialService credentialService, string reason)
    {
        _credentialService = credentialService ?? throw new ArgumentNullException(nameof(credentialService));
        _reduceMotion = new AppSettingsService().Load().ReduceMotion;
        InitializeComponent();
        ReasonText.Text = string.IsNullOrWhiteSpace(reason)
            ? "Enter your app-lock password to continue."
            : reason;
        BusyProgress.IsIndeterminate = !_reduceMotion;
        Loaded += (_, _) => PasswordBox.Focus();
    }

    public bool WasUnlocked => _unlocked;

    private async void Unlock_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
            return;

        var password = PasswordBox.Password;
        if (string.IsNullOrWhiteSpace(password))
        {
            StatusText.Text = "Enter the app-lock password first.";
            PasswordBox.Focus();
            return;
        }

        SetBusy(true);
        try
        {
            var delaySeconds = _failedAttempts < 3 ? 0 : Math.Min(5, _failedAttempts - 1);
            if (delaySeconds > 0)
            {
                StatusText.Text = $"Too many failed attempts. Retrying in {delaySeconds} second(s)…";
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
            }

            var valid = await Task.Run(() => _credentialService.Verify(password));
            if (!valid)
            {
                _failedAttempts++;
                StatusText.Text = "The app-lock password is incorrect.";
                PasswordBox.Clear();
                PasswordBox.Focus();
                return;
            }

            _unlocked = true;
            _allowClose = true;
            PasswordBox.Clear();
            DialogResult = true;
        }
        catch (CryptographicException)
        {
            _failedAttempts++;
            StatusText.Text = "The app-lock password is incorrect.";
            PasswordBox.Clear();
            PasswordBox.Focus();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Rice2k could not verify the app-lock credential: {ex.Message}";
            PasswordBox.Clear();
        }
        finally
        {
            password = string.Empty;
            if (!_unlocked)
                SetBusy(false);
        }
    }

    private void PasswordBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            PasswordBox.Clear();
            StatusText.Text = "Rice2k remains locked.";
            e.Handled = true;
        }
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        _allowClose = true;
        Application.Current.Shutdown();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        PasswordBox.Clear();
        if (!_allowClose && !_unlocked)
        {
            e.Cancel = true;
            StatusText.Text = "Unlock Rice2k or choose Exit Rice2k.";
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        PasswordBox.IsEnabled = !busy;
        UnlockButton.IsEnabled = !busy;
        BusyProgress.IsIndeterminate = busy && !_reduceMotion;
        BusyProgress.Value = busy && _reduceMotion ? 50 : 0;
        BusyProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }
}
