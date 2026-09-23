using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using Rice2k.Encryption.Services;

namespace Rice2k.Encryption;

public partial class MainWindow
{
    private readonly AppLockCredentialService _appLockCredentialService = new();
    private readonly ProtectedClipboardService _appLockClipboard = new();
    private Rice2kAppSettings _appLockSettings = new();
    private DispatcherTimer? _appLockTimer;
    private Button? _manualLockButton;
    private DateTimeOffset _lastAppInputUtc = DateTimeOffset.UtcNow;
    private bool _appLockUiInitialized;
    private bool _appLockActive;
    private bool _startupLockChecked;
    private bool _systemEventsSubscribed;

    private sealed record WindowPresentationState(
        Window Window,
        double Opacity,
        bool ShowInTaskbar,
        WindowState WindowState);

    private void InitializeAppLockUi()
    {
        if (_appLockUiInitialized)
            return;

        _appLockUiInitialized = true;
        _appLockSettings = _appSettingsService.Load();
        _lastAppInputUtc = DateTimeOffset.UtcNow;

        StateChanged += MainWindow_AppLockStateChanged;
        InputManager.Current.PreProcessInput += InputManager_PreProcessInput;

        try
        {
            SystemEvents.SessionSwitch += SystemEvents_SessionSwitch;
            _systemEventsSubscribed = true;
        }
        catch
        {
            _systemEventsSubscribed = false;
        }

        _appLockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _appLockTimer.Tick += AppLockTimer_Tick;
        _appLockTimer.Start();

        AddManualLockButton();
        ApplyAppLockSettings(_appLockSettings);

        Closed += (_, _) => DisposeAppLockHooks();
    }

    private void AddManualLockButton()
    {
        var settingsButton = FindVisualChildren<Button>(this)
            .FirstOrDefault(button => string.Equals(button.Tag?.ToString(), "Settings", StringComparison.Ordinal));
        if (settingsButton?.Parent is not Panel parent)
            return;

        _manualLockButton = new Button
        {
            Content = "🔐  Lock Rice2k",
            Tag = "Rice2kManualLock",
            ToolTip = "Hide Rice2k behind the configured app-lock password",
            Margin = new Thickness(0, 4, 0, 0)
        };
        if (TryFindResource("NavButtonStyle") is Style navStyle)
            _manualLockButton.Style = navStyle;
        _manualLockButton.Click += ManualAppLock_Click;

        var insertBefore = _privacyQuickButton is null
            ? parent.Children.IndexOf(settingsButton)
            : parent.Children.IndexOf(_privacyQuickButton);
        parent.Children.Insert(insertBefore >= 0 ? insertBefore : parent.Children.Count, _manualLockButton);
    }

    private void ApplyAppLockSettings(Rice2kAppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _appLockSettings = settings;
        var configured = _appLockCredentialService.IsConfigured();

        if (_manualLockButton is not null)
        {
            _manualLockButton.IsEnabled = settings.AppLockEnabled && configured;
            _manualLockButton.Visibility = configured ? Visibility.Visible : Visibility.Collapsed;
        }

        if (settings.AppLockEnabled && !configured && _appLockUiInitialized)
            GlobalStatusText.Text = "⚠ App Lock setting is enabled but its credential is missing or invalid. Reconfigure App Lock in Settings.";
    }

    private void RequestStartupLockIfNeeded()
    {
        if (_startupLockChecked)
            return;
        _startupLockChecked = true;

        var settings = _appSettingsService.Load();
        ApplyAppLockSettings(settings);
        if (settings.AppLockEnabled && _appLockCredentialService.IsConfigured())
        {
            Dispatcher.BeginInvoke(
                DispatcherPriority.ApplicationIdle,
                new Action(() => RequestAppLock("Rice2k requires your app-lock password at startup.")));
        }
    }

    private void ManualAppLock_Click(object sender, RoutedEventArgs e) =>
        RequestAppLock("Rice2k was locked manually.");

    private void MainWindow_AppLockStateChanged(object? sender, EventArgs e)
    {
        if (_appLockActive || WindowState != WindowState.Minimized)
            return;

        var settings = _appSettingsService.Load();
        ApplyAppLockSettings(settings);
        if (settings.AppLockEnabled && settings.LockOnMinimize && _appLockCredentialService.IsConfigured())
        {
            Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(() => RequestAppLock("Rice2k locked because the main window was minimized.")));
        }
    }

    private void SystemEvents_SessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason != SessionSwitchReason.SessionLock)
            return;

        var settings = _appSettingsService.Load();
        if (!settings.AppLockEnabled || !settings.LockOnWindowsSessionLock)
            return;

        Dispatcher.BeginInvoke(
            DispatcherPriority.Send,
            new Action(() =>
            {
                ApplyAppLockSettings(settings);
                if (_appLockCredentialService.IsConfigured())
                    RequestAppLock("Rice2k locked because Windows reported that the current session was locked.");
            }));
    }

    private void InputManager_PreProcessInput(object sender, PreProcessInputEventArgs e)
    {
        if (_appLockActive)
            return;

        if (e.StagingItem.Input is KeyboardEventArgs or MouseEventArgs)
            _lastAppInputUtc = DateTimeOffset.UtcNow;
    }

    private void AppLockTimer_Tick(object? sender, EventArgs e)
    {
        if (_appLockActive)
            return;

        var settings = _appSettingsService.Load();
        ApplyAppLockSettings(settings);
        if (!settings.AppLockEnabled || settings.AppLockInactivityMinutes <= 0 || !_appLockCredentialService.IsConfigured())
            return;

        var idleFor = DateTimeOffset.UtcNow - _lastAppInputUtc;
        if (idleFor < TimeSpan.FromMinutes(settings.AppLockInactivityMinutes))
            return;

        RequestAppLock($"Rice2k locked after {settings.AppLockInactivityMinutes} minute(s) without Rice2k input.");
    }

    private void RequestAppLock(string reason)
    {
        if (_appLockActive)
            return;

        var settings = _appSettingsService.Load();
        ApplyAppLockSettings(settings);
        if (!settings.AppLockEnabled || !_appLockCredentialService.IsConfigured())
            return;

        _appLockActive = true;
        _lastAppInputUtc = DateTimeOffset.UtcNow;
        var presentationStates = new List<WindowPresentationState>();
        var unlocked = false;

        try
        {
            ClearTransientSensitivePreviews();
            _appLockClipboard.ClearNow();
            BlankRice2kWindows(presentationStates);

            var lockWindow = new AppLockWindow(_appLockCredentialService, reason);
            unlocked = lockWindow.ShowDialog() == true && lockWindow.WasUnlocked;
        }
        finally
        {
            RestoreRice2kWindows(presentationStates);
            _appLockActive = false;
            _lastAppInputUtc = DateTimeOffset.UtcNow;
        }

        if (!unlocked || Application.Current.Dispatcher.HasShutdownStarted)
            return;

        try
        {
            ShowInTaskbar = true;
            Show();
            WindowState = WindowState.Normal;
            Activate();
            GlobalStatusText.Text = "● Rice2k unlocked   |   Local / offline";
        }
        catch
        {
            // Restoration is best effort; an application shutdown may already be underway.
        }
    }

    private static void BlankRice2kWindows(List<WindowPresentationState> presentationStates)
    {
        foreach (Window window in Application.Current.Windows.Cast<Window>().ToArray())
        {
            if (window is AppLockWindow || !window.IsVisible)
                continue;

            try
            {
                presentationStates.Add(new WindowPresentationState(
                    window,
                    window.Opacity,
                    window.ShowInTaskbar,
                    window.WindowState));

                window.Opacity = 0;
                window.ShowInTaskbar = false;
                if (window.WindowState == WindowState.Minimized)
                    window.WindowState = WindowState.Normal;
            }
            catch
            {
                // One unusual/closing child window must not prevent the rest of Rice2k from locking.
            }
        }
    }

    private static void RestoreRice2kWindows(IEnumerable<WindowPresentationState> presentationStates)
    {
        foreach (var state in presentationStates.Reverse())
        {
            try
            {
                state.Window.Opacity = state.Opacity;
                state.Window.ShowInTaskbar = state.ShowInTaskbar;
                state.Window.WindowState = state.WindowState == WindowState.Minimized
                    ? WindowState.Normal
                    : state.WindowState;
            }
            catch
            {
                // A window may have closed while Rice2k was locked. Continue restoring the others.
            }
        }
    }

    private void DisposeAppLockHooks()
    {
        _appLockTimer?.Stop();
        _appLockTimer = null;
        StateChanged -= MainWindow_AppLockStateChanged;
        InputManager.Current.PreProcessInput -= InputManager_PreProcessInput;
        if (_systemEventsSubscribed)
        {
            try
            {
                SystemEvents.SessionSwitch -= SystemEvents_SessionSwitch;
            }
            catch
            {
                // Shutdown cleanup only.
            }
            _systemEventsSubscribed = false;
        }
    }
}
