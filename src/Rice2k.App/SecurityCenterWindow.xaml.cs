using System.Reflection;
using System.Windows;
using Rice2k.Encryption.Services;

namespace Rice2k.Encryption;

public partial class SecurityCenterWindow : Window
{
    private readonly AppSettingsService _settingsService = new();
    private readonly AppLockCredentialService _appLockService = new();

    public SecurityCenterWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => RefreshStatus();
    }

    public bool OpenSettingsRequested { get; private set; }
    public bool LockRequested { get; private set; }

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshStatus();

    private void RefreshStatus()
    {
        var settings = _settingsService.Load();
        var assembly = Assembly.GetEntryAssembly();
        var informational = assembly?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        var displayVersion = string.IsNullOrWhiteSpace(informational)
            ? assembly?.GetName().Version?.ToString(3) ?? "preview"
            : informational.Split('+', 2)[0];
        VersionText.Text = $"Rice2k Encryption Software {displayVersion}";

        PrivacyModeText.Text = settings.PrivacyModeEnabled
            ? "✓ Privacy Mode is ON"
            : "○ Privacy Mode is OFF";
        ClipboardText.Text = settings.ClipboardAutoClearSeconds <= 0
            ? "Clipboard auto-clear: Never"
            : $"Clipboard auto-clear: {FormatClipboardDuration(settings.ClipboardAutoClearSeconds)}";
        ActivityPrivacyText.Text = settings.HideActivityInPrivacyMode
            ? "Activity list is hidden/cleared when Privacy Mode is active"
            : "Activity list remains visible when Privacy Mode is active";

        var credentialPresent = _appLockService.IsConfigured();
        var appLockEnabled = credentialPresent && settings.AppLockEnabled;
        AppLockText.Text = appLockEnabled
            ? "✓ App Lock is configured and enabled"
            : credentialPresent
                ? "⚠ App Lock credential exists, but locking is disabled"
                : "○ App Lock is not configured";
        AppLockTriggersText.Text = appLockEnabled
            ? BuildAppLockTriggerSummary(settings)
            : "Automatic App Lock triggers are inactive.";
        LockNowButton.IsEnabled = appLockEnabled;

        var vaultAutoLockEnabled = settings.VaultAutoLockEnabled ?? true;
        var vaultAutoLockMinutes = settings.VaultAutoLockMinutes is > 0 ? settings.VaultAutoLockMinutes.Value : 10;
        VaultLockText.Text = vaultAutoLockEnabled
            ? $"✓ Vault inactivity auto-lock: {vaultAutoLockMinutes} minute(s)"
            : "○ Vault inactivity auto-lock is disabled";

        if (settings.LastRecoveryTestUtc is { } recoveryUtc)
        {
            RecoveryText.Text = "✓ A successful local recovery test is recorded";
            var keyName = string.IsNullOrWhiteSpace(settings.LastRecoveryKeyName)
                ? "Unnamed key"
                : settings.LastRecoveryKeyName;
            var fingerprint = string.IsNullOrWhiteSpace(settings.LastRecoveryFingerprint)
                ? "fingerprint not recorded"
                : settings.LastRecoveryFingerprint;
            RecoveryDetailText.Text = $"{keyName} • {fingerprint} • tested {recoveryUtc.ToLocalTime():g}. A past successful test does not guarantee that every current recovery package is valid; test the recovery material you intend to rely on.";
        }
        else
        {
            RecoveryText.Text = "○ No successful recovery test is recorded yet";
            RecoveryDetailText.Text = "Use Recovery Center → Test Recovery for recovery material you intend to rely on. Testing is performed in memory and does not reveal the recovered secret key.";
        }

        StatusText.Text = $"Refreshed {DateTime.Now:t} • no secret material displayed";
    }

    private void LockNow_Click(object sender, RoutedEventArgs e)
    {
        LockRequested = true;
        DialogResult = true;
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        OpenSettingsRequested = true;
        DialogResult = true;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private static string BuildAppLockTriggerSummary(Rice2kAppSettings settings)
    {
        var triggers = new List<string> { "startup", "manual" };
        if (settings.LockOnMinimize)
            triggers.Add("minimize");
        if (settings.LockOnWindowsSessionLock)
            triggers.Add("Windows session lock");
        if (settings.AppLockInactivityMinutes > 0)
            triggers.Add($"{settings.AppLockInactivityMinutes}-minute Rice2k inactivity");

        return "Enabled triggers: " + string.Join(", ", triggers) + ".";
    }

    private static string FormatClipboardDuration(int seconds) => seconds switch
    {
        60 => "1 minute",
        120 => "2 minutes",
        _ => $"{seconds} seconds"
    };
}
