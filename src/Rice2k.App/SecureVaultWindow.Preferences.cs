using System.Windows;
using System.Windows.Controls;
using Rice2k.Encryption.Services;

namespace Rice2k.Encryption;

public partial class SecureVaultWindow
{
    private readonly AppSettingsService _vaultSettingsService = new();
    private bool _vaultPreferencesInitialized;
    private bool _loadingVaultPreferences;

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (_vaultPreferencesInitialized)
            return;

        _vaultPreferencesInitialized = true;
        _autoLockTimer.Tick -= AutoLockTimer_Tick;
        _autoLockTimer.Tick += ConfigurableAutoLockTimer_Tick;

        LoadVaultPreferences();
        AutoLockCheckBox.Checked += VaultAutoLockPreference_Changed;
        AutoLockCheckBox.Unchecked += VaultAutoLockPreference_Changed;
        AutoLockMinutesComboBox.SelectionChanged += VaultAutoLockPreference_Changed;
    }

    private void LoadVaultPreferences()
    {
        _loadingVaultPreferences = true;
        try
        {
            var settings = _vaultSettingsService.Load();
            var enabled = settings.VaultAutoLockEnabled ?? true;
            var minutes = NormalizeAutoLockMinutes(settings.VaultAutoLockMinutes ?? 10);

            AutoLockCheckBox.IsChecked = enabled;
            foreach (var item in AutoLockMinutesComboBox.Items.OfType<ComboBoxItem>())
            {
                if (int.TryParse(item.Tag?.ToString(), out var value) && value == minutes)
                {
                    AutoLockMinutesComboBox.SelectedItem = item;
                    break;
                }
            }

            AutoLockMinutesComboBox.IsEnabled = enabled;
        }
        finally
        {
            _loadingVaultPreferences = false;
        }
    }

    private void VaultAutoLockPreference_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingVaultPreferences)
            return;

        var enabled = AutoLockCheckBox.IsChecked == true;
        AutoLockMinutesComboBox.IsEnabled = enabled;
        var minutes = GetSelectedAutoLockMinutes();
        var current = _vaultSettingsService.Load();
        _vaultSettingsService.TrySave(current with
        {
            VaultAutoLockEnabled = enabled,
            VaultAutoLockMinutes = minutes
        });

        _lastActivityUtc = DateTimeOffset.UtcNow;
        if (_session is not null)
        {
            VaultFooterText.Text = enabled
                ? $"Unlocked • {_session.Entries.Count:N0} protected file(s) • auto-lock after {minutes} minute(s) of inactivity"
                : $"Unlocked • {_session.Entries.Count:N0} protected file(s) • auto-lock disabled";
        }
    }

    private void ConfigurableAutoLockTimer_Tick(object? sender, EventArgs e)
    {
        if (_busy || _session is null || AutoLockCheckBox.IsChecked != true)
            return;

        var minutes = GetSelectedAutoLockMinutes();
        if (DateTimeOffset.UtcNow - _lastActivityUtc >= TimeSpan.FromMinutes(minutes))
            LockVault($"Auto-locked after {minutes} minute(s) of inactivity");
    }

    private int GetSelectedAutoLockMinutes()
    {
        if (AutoLockMinutesComboBox.SelectedItem is ComboBoxItem item &&
            int.TryParse(item.Tag?.ToString(), out var minutes))
            return NormalizeAutoLockMinutes(minutes);

        return 10;
    }

    private static int NormalizeAutoLockMinutes(int minutes) =>
        minutes is 1 or 5 or 10 or 15 or 30 ? minutes : 10;
}
