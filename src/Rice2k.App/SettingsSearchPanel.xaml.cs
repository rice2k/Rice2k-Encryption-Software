using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using Rice2k.Encryption.Services;

namespace Rice2k.Encryption;

public partial class SettingsSearchPanel : UserControl
{
    private readonly AppSettingsService _settingsService;
    private readonly AppLockCredentialService _appLockService = new();
    private readonly Window _owner;
    private readonly Action<bool> _hintsChanged;
    private readonly Action<Rice2kAppSettings>? _privacyChanged;
    private bool _loading;

    public SettingsSearchPanel(
        AppSettingsService settingsService,
        Window owner,
        Action<bool> hintsChanged,
        Action<Rice2kAppSettings>? privacyChanged = null)
    {
        _settingsService = settingsService;
        _owner = owner;
        _hintsChanged = hintsChanged;
        _privacyChanged = privacyChanged;

        InitializeComponent();
        LoadSettings();
        ApplySearch();
    }

    public void RefreshPrivacySettings(Rice2kAppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _loading = true;
        try
        {
            PrivacyModeCheck.IsChecked = settings.PrivacyModeEnabled;
            HideActivityCheck.IsChecked = settings.HideActivityInPrivacyMode;
            ClearPreviewsCheck.IsChecked = settings.ClearSensitivePreviewsWhenPrivacyModeStarts;
            LockOnMinimizeCheck.IsChecked = settings.LockOnMinimize;
            LockOnWindowsSessionCheck.IsChecked = settings.LockOnWindowsSessionLock;
            SelectClipboardSeconds(settings.ClipboardAutoClearSeconds);
            SelectAppLockMinutes(settings.AppLockInactivityMinutes);
            RefreshAppLockControls(settings);
            PrivacyStatusText.Text = settings.PrivacyModeEnabled
                ? "Privacy Mode is on. Session activity is hidden according to your selected privacy options."
                : "Privacy Mode is off. Clipboard and App Lock controls remain independent.";
        }
        finally
        {
            _loading = false;
        }
    }

    private void LoadSettings()
    {
        _loading = true;
        try
        {
            var settings = _settingsService.Load();
            HelpfulHintsCheck.IsChecked = settings.ShowHelpfulHints;
            PrivacyModeCheck.IsChecked = settings.PrivacyModeEnabled;
            HideActivityCheck.IsChecked = settings.HideActivityInPrivacyMode;
            ClearPreviewsCheck.IsChecked = settings.ClearSensitivePreviewsWhenPrivacyModeStarts;
            LockOnMinimizeCheck.IsChecked = settings.LockOnMinimize;
            LockOnWindowsSessionCheck.IsChecked = settings.LockOnWindowsSessionLock;
            SelectClipboardSeconds(settings.ClipboardAutoClearSeconds);
            SelectAppLockMinutes(settings.AppLockInactivityMinutes);
            RefreshAppLockControls(settings);
            _hintsChanged(settings.ShowHelpfulHints);
            _privacyChanged?.Invoke(settings);
        }
        finally
        {
            _loading = false;
        }
    }

    private void HelpfulHints_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;

        var enabled = HelpfulHintsCheck.IsChecked != false;
        var current = _settingsService.Load();
        var updated = current with { ShowHelpfulHints = enabled };
        var saved = _settingsService.TrySave(updated);
        _hintsChanged(enabled);
        PreferenceStatusText.Text = saved
            ? enabled ? "Helpful hints enabled." : "Helpful hints hidden."
            : "Preference could not be saved; it will apply for this session only.";
    }

    private void PrivacyPreference_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;

        var current = _settingsService.Load();
        var updated = current with
        {
            PrivacyModeEnabled = PrivacyModeCheck.IsChecked == true,
            HideActivityInPrivacyMode = HideActivityCheck.IsChecked != false,
            ClearSensitivePreviewsWhenPrivacyModeStarts = ClearPreviewsCheck.IsChecked != false
        };

        var saved = _settingsService.TrySave(updated);
        _privacyChanged?.Invoke(updated);
        PrivacyStatusText.Text = saved
            ? updated.PrivacyModeEnabled
                ? "Privacy Mode is on. Session activity is hidden according to your selected privacy options."
                : "Privacy Mode is off. Clipboard and App Lock controls remain independent."
            : "Privacy preference could not be saved; the current session was updated only.";
    }

    private void ClipboardSeconds_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ClipboardSecondsCombo.SelectedItem is not ComboBoxItem item ||
            !int.TryParse(item.Tag?.ToString(), out var seconds))
        {
            return;
        }

        var current = _settingsService.Load();
        var updated = current with { ClipboardAutoClearSeconds = seconds };
        var saved = _settingsService.TrySave(updated);
        _privacyChanged?.Invoke(updated);
        PrivacyStatusText.Text = saved
            ? seconds == 0
                ? "Clipboard auto-clear disabled. Use Clear Clipboard Now whenever needed."
                : $"Rice2k-owned clipboard values will be cleared after {seconds} seconds if they have not been replaced."
            : "Clipboard preference could not be saved; the current session was updated only.";
    }

    private void ClearClipboardNow_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.Clear();
            PrivacyStatusText.Text = "✓ Windows clipboard cleared now.";
        }
        catch (Exception ex)
        {
            PrivacyStatusText.Text = $"Windows clipboard could not be cleared: {ex.Message}";
        }
    }

    private async void ConfigureAppLock_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_appLockService.IsConfigured())
            {
                var currentPassword = PromptForPassword(
                    "Confirm Current App Lock",
                    "Enter the current Rice2k app-lock password before replacing it.");
                if (currentPassword is null)
                    return;

                bool currentValid;
                try
                {
                    currentValid = await Task.Run(() => _appLockService.Verify(currentPassword));
                }
                finally
                {
                    currentPassword = string.Empty;
                }

                if (!currentValid)
                {
                    PrivacyStatusText.Text = "The current app-lock password is incorrect. Nothing was changed.";
                    return;
                }
            }

            var newPassword = PromptForNewPassword(
                "Set Rice2k App Lock Password",
                "Use at least 12 characters. This password protects the running Rice2k interface; it does not replace any file or vault password.");
            if (newPassword is null)
                return;

            try
            {
                await Task.Run(() => _appLockService.SetPassword(newPassword));
            }
            finally
            {
                newPassword = string.Empty;
            }

            var updated = _settingsService.Load() with { AppLockEnabled = true };
            _settingsService.TrySave(updated);
            _privacyChanged?.Invoke(updated);
            RefreshPrivacySettings(updated);
            PrivacyStatusText.Text = "✓ App Lock configured. Rice2k will require this password on the next startup and whenever an enabled lock trigger fires.";
        }
        catch (Exception ex)
        {
            PrivacyStatusText.Text = $"App Lock could not be configured: {ex.Message}";
        }
    }

    private async void RemoveAppLock_Click(object sender, RoutedEventArgs e)
    {
        if (!_appLockService.IsConfigured())
        {
            RefreshAppLockControls(_settingsService.Load());
            return;
        }

        var password = PromptForPassword(
            "Remove Rice2k App Lock",
            "Enter the current app-lock password. Removing App Lock does not change any encrypted file, vault, key, or identity password.");
        if (password is null)
            return;

        try
        {
            await Task.Run(() => _appLockService.Remove(password));
            var updated = _settingsService.Load() with
            {
                AppLockEnabled = false,
                LockOnMinimize = false,
                LockOnWindowsSessionLock = false,
                AppLockInactivityMinutes = 0
            };
            _settingsService.TrySave(updated);
            _privacyChanged?.Invoke(updated);
            RefreshPrivacySettings(updated);
            PrivacyStatusText.Text = "App Lock removed. File/vault encryption settings were not changed.";
        }
        catch (CryptographicException)
        {
            PrivacyStatusText.Text = "The app-lock password is incorrect. App Lock remains configured.";
        }
        catch (Exception ex)
        {
            PrivacyStatusText.Text = $"App Lock could not be removed: {ex.Message}";
        }
        finally
        {
            password = string.Empty;
        }
    }

    private void AppLockPreference_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;

        if (!_appLockService.IsConfigured())
        {
            _loading = true;
            try
            {
                LockOnMinimizeCheck.IsChecked = false;
                LockOnWindowsSessionCheck.IsChecked = false;
            }
            finally
            {
                _loading = false;
            }
            PrivacyStatusText.Text = "Set an App Lock password before enabling automatic lock triggers.";
            return;
        }

        var current = _settingsService.Load();
        var updated = current with
        {
            AppLockEnabled = true,
            LockOnMinimize = LockOnMinimizeCheck.IsChecked == true,
            LockOnWindowsSessionLock = LockOnWindowsSessionCheck.IsChecked == true
        };
        var saved = _settingsService.TrySave(updated);
        _privacyChanged?.Invoke(updated);
        PrivacyStatusText.Text = saved
            ? "App Lock trigger preferences saved."
            : "App Lock trigger preferences apply for this session but could not be saved.";
    }

    private void AppLockInactivity_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || AppLockInactivityCombo.SelectedItem is not ComboBoxItem item ||
            !int.TryParse(item.Tag?.ToString(), out var minutes))
        {
            return;
        }

        if (!_appLockService.IsConfigured())
        {
            _loading = true;
            try
            {
                SelectAppLockMinutes(0);
            }
            finally
            {
                _loading = false;
            }
            PrivacyStatusText.Text = "Set an App Lock password before enabling inactivity locking.";
            return;
        }

        var current = _settingsService.Load();
        var updated = current with
        {
            AppLockEnabled = true,
            AppLockInactivityMinutes = minutes
        };
        var saved = _settingsService.TrySave(updated);
        _privacyChanged?.Invoke(updated);
        PrivacyStatusText.Text = saved
            ? minutes == 0 ? "Inactivity App Lock disabled." : $"Rice2k will lock after {minutes} minute(s) without Rice2k input."
            : "Inactivity preference applies for this session but could not be saved.";
    }

    private void RefreshAppLockControls(Rice2kAppSettings settings)
    {
        var configured = _appLockService.IsConfigured() && settings.AppLockEnabled;
        ConfigureAppLockButton.Content = configured ? "Change App Lock Password…" : "Set App Lock Password…";
        RemoveAppLockButton.IsEnabled = configured;
        LockOnMinimizeCheck.IsEnabled = configured;
        LockOnWindowsSessionCheck.IsEnabled = configured;
        AppLockInactivityCombo.IsEnabled = configured;
        AppLockConfiguredText.Text = configured
            ? "✓ App Lock configured — startup locking is active"
            : "App Lock is not configured.";
    }

    private static string? PromptForPassword(string title, string guidance)
    {
        var password = new PasswordBox { Margin = new Thickness(0, 5, 0, 12) };
        var ok = new Button { Content = "Continue", IsDefault = true, MinWidth = 105 };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 90, Margin = new Thickness(8, 0, 0, 0) };
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 19, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = guidance, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 12), Opacity = 0.82 });
        panel.Children.Add(new TextBlock { Text = "Password" });
        panel.Children.Add(password);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        actions.Children.Add(ok);
        actions.Children.Add(cancel);
        panel.Children.Add(actions);

        var dialog = new Window
        {
            Title = title,
            Width = 500,
            Height = 275,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = panel
        };
        ok.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(password.Password))
            {
                MessageBox.Show(dialog, "Enter the password first.", title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            dialog.DialogResult = true;
        };

        if (dialog.ShowDialog() != true)
        {
            password.Clear();
            return null;
        }

        var result = password.Password;
        password.Clear();
        return result;
    }

    private static string? PromptForNewPassword(string title, string guidance)
    {
        var first = new PasswordBox { Margin = new Thickness(0, 5, 0, 10) };
        var second = new PasswordBox { Margin = new Thickness(0, 5, 0, 12) };
        var ok = new Button { Content = "Set Password", IsDefault = true, MinWidth = 115 };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 90, Margin = new Thickness(8, 0, 0, 0) };
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 19, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = guidance, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 12), Opacity = 0.82 });
        panel.Children.Add(new TextBlock { Text = "New password" });
        panel.Children.Add(first);
        panel.Children.Add(new TextBlock { Text = "Confirm password" });
        panel.Children.Add(second);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        actions.Children.Add(ok);
        actions.Children.Add(cancel);
        panel.Children.Add(actions);

        var dialog = new Window
        {
            Title = title,
            Width = 500,
            Height = 345,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = panel
        };
        ok.Click += (_, _) =>
        {
            if (first.Password.Length < 12)
            {
                MessageBox.Show(dialog, "Use at least 12 characters.", title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!string.Equals(first.Password, second.Password, StringComparison.Ordinal))
            {
                MessageBox.Show(dialog, "The two passwords do not match.", title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            dialog.DialogResult = true;
        };

        if (dialog.ShowDialog() != true)
        {
            first.Clear();
            second.Clear();
            return null;
        }

        var result = first.Password;
        first.Clear();
        second.Clear();
        return result;
    }

    private void SelectClipboardSeconds(int seconds)
    {
        foreach (var candidate in ClipboardSecondsCombo.Items.OfType<ComboBoxItem>())
        {
            if (int.TryParse(candidate.Tag?.ToString(), out var value) && value == seconds)
            {
                ClipboardSecondsCombo.SelectedItem = candidate;
                return;
            }
        }
        ClipboardSecondsCombo.SelectedIndex = 2;
    }

    private void SelectAppLockMinutes(int minutes)
    {
        foreach (var candidate in AppLockInactivityCombo.Items.OfType<ComboBoxItem>())
        {
            if (int.TryParse(candidate.Tag?.ToString(), out var value) && value == minutes)
            {
                AppLockInactivityCombo.SelectedItem = candidate;
                return;
            }
        }
        AppLockInactivityCombo.SelectedIndex = 0;
    }

    private void ReplayTour_Click(object sender, RoutedEventArgs e)
    {
        var tour = new WelcomeTourWindow(_settingsService)
        {
            Owner = _owner
        };
        tour.ShowDialog();
        PreferenceStatusText.Text = "Welcome tour completed.";
    }

    private void SettingsSearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplySearch();

    private void ApplySearch()
    {
        var query = SettingsSearchBox.Text.Trim();
        var cards = new[] { ExperienceCard, ModeCard, SafetyCard, PrivacyCard };

        if (string.IsNullOrWhiteSpace(query))
        {
            foreach (var card in cards)
                card.Visibility = Visibility.Visible;

            SearchStatusText.Text = "Showing all settings.";
            return;
        }

        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var matches = 0;

        foreach (var card in cards)
        {
            var searchable = card.Tag?.ToString() ?? string.Empty;
            var visible = terms.All(term => searchable.Contains(term, StringComparison.OrdinalIgnoreCase));
            card.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            if (visible)
                matches++;
        }

        SearchStatusText.Text = matches == 0
            ? "No settings matched. Try privacy, lock, clipboard, hints, verify, or advanced."
            : $"{matches} matching setting section(s).";
    }
}
