using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Rice2k.Encryption.Services;

namespace Rice2k.Encryption;

public partial class MainWindow
{
    private readonly ProtectedClipboardService _mainProtectedClipboard = new();
    private Rice2kAppSettings _privacySettings = new();
    private Button? _privacyQuickButton;
    private DispatcherTimer? _privacyScrubTimer;
    private bool _privacyUiInitialized;
    private bool _privacyWasEnabled;

    private void InitializePrivacyUi()
    {
        if (_privacyUiInitialized)
            return;

        _privacyUiInitialized = true;
        AddHandler(Button.ClickEvent, new RoutedEventHandler(MainWindow_PrivacyAwareButtonClick), handledEventsToo: true);

        _privacyScrubTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _privacyScrubTimer.Tick += (_, _) =>
        {
            if (_privacySettings.PrivacyModeEnabled && _privacySettings.HideActivityInPrivacyMode)
                ActivityList.Items.Clear();
        };
        _privacyScrubTimer.Start();

        AddPrivacyQuickToggle();
        ApplyPrivacySettings(_appSettingsService.Load());
        Closed += (_, _) => _privacyScrubTimer?.Stop();
    }

    private void AddPrivacyQuickToggle()
    {
        var settingsButton = FindVisualChildren<Button>(this)
            .FirstOrDefault(button => string.Equals(button.Tag?.ToString(), "Settings", StringComparison.Ordinal));
        if (settingsButton?.Parent is not Panel parent)
            return;

        _privacyQuickButton = new Button
        {
            Tag = "Rice2kPrivacyToggle",
            ToolTip = "Quickly hide session activity and clear transient sensitive previews",
            Margin = new Thickness(0, 4, 0, 0)
        };
        if (TryFindResource("NavButtonStyle") is Style navStyle)
            _privacyQuickButton.Style = navStyle;
        _privacyQuickButton.Click += PrivacyQuickToggle_Click;

        var index = parent.Children.IndexOf(settingsButton);
        parent.Children.Insert(index >= 0 ? index : parent.Children.Count, _privacyQuickButton);
        RefreshPrivacyQuickToggle();
    }

    private void PrivacyQuickToggle_Click(object sender, RoutedEventArgs e)
    {
        var updated = _privacySettings with { PrivacyModeEnabled = !_privacySettings.PrivacyModeEnabled };
        var saved = _appSettingsService.TrySave(updated);
        ApplyPrivacySettings(updated);
        SyncPrivacySettingsPanel(updated);

        if (!saved)
        {
            GlobalStatusText.Text += updated.PrivacyModeEnabled
                ? "   |   ⚠ preference not saved; Privacy Mode is session-only"
                : "   |   ⚠ preference not saved; previous saved setting may return next launch";
        }
    }

    private void ApplyPrivacySettings(Rice2kAppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var enablingNow = settings.PrivacyModeEnabled && !_privacyWasEnabled;
        _privacySettings = settings;
        _privacyWasEnabled = settings.PrivacyModeEnabled;

        if (settings.PrivacyModeEnabled && settings.HideActivityInPrivacyMode)
        {
            ActivityList.Items.Clear();
            ActivityList.Visibility = Visibility.Collapsed;
        }
        else
        {
            ActivityList.Visibility = Visibility.Visible;
        }

        bool? clipboardCleared = null;
        bool? diskHistoryCleared = null;
        if (enablingNow && settings.ClearSensitivePreviewsWhenPrivacyModeStarts)
            clipboardCleared = ClearTransientSensitivePreviews();

        if (enablingNow && settings.ClearDiskHistoryWhenPrivacyModeStarts)
            diskHistoryCleared = _privacyHistoryService.TryClearAll();

        RefreshPrivacyQuickToggle();
        ApplyAppLockSettings(settings);
        if (_privacyUiInitialized)
        {
            if (settings.PrivacyModeEnabled && clipboardCleared == false && diskHistoryCleared == false)
            {
                GlobalStatusText.Text = "⚠ Privacy Mode ON, but Rice2k could not clear the Windows clipboard or all stored history. Review Stored History and clear the clipboard manually.";
            }
            else if (settings.PrivacyModeEnabled && clipboardCleared == false)
            {
                GlobalStatusText.Text = "⚠ Privacy Mode ON, but Windows clipboard clearing failed. Clear the clipboard manually if it may contain sensitive data.";
            }
            else if (settings.PrivacyModeEnabled && diskHistoryCleared == false)
            {
                GlobalStatusText.Text = "⚠ Privacy Mode ON, but stored Rice2k history could not be fully cleared. Review Stored History in Settings.";
            }
            else
            {
                GlobalStatusText.Text = settings.PrivacyModeEnabled
                    ? "● Privacy Mode ON   |   Local / offline"
                    : "● Privacy Mode OFF   |   Local / offline";
            }
        }
    }

    private void SyncPrivacySettingsPanel(Rice2kAppSettings settings)
    {
        if (SettingsPage.Content is not StackPanel root)
            return;
        foreach (var panel in root.Children.OfType<SettingsSearchPanel>())
            panel.RefreshPrivacySettings(settings);
    }

    private bool ClearTransientSensitivePreviews()
    {
        TextInputBox.Clear();
        TextOutputBox.Clear();
        GeneratedPasswordBox.Clear();
        ActivityList.Items.Clear();

        // Newer Rice2k features add password fields in partial classes and child
        // windows. Clear every live PasswordBox instead of maintaining a fragile
        // hand-written list so App Lock / Privacy Mode cannot miss newly-added
        // key-package, identity, recovery, vault, or other transient passwords.
        foreach (Window window in Application.Current.Windows.Cast<Window>().ToArray())
        {
            try
            {
                foreach (var passwordBox in FindVisualChildren<PasswordBox>(window).ToArray())
                    passwordBox.Clear();
            }
            catch
            {
                // A child window may be opening/closing while the scrub runs. Keep
                // clearing the remaining Rice2k windows rather than aborting all cleanup.
            }
        }

        return _mainProtectedClipboard.ClearNow();
    }

    private void RefreshPrivacyQuickToggle()
    {
        if (_privacyQuickButton is null)
            return;
        _privacyQuickButton.Content = _privacySettings.PrivacyModeEnabled
            ? "◉  Privacy Mode: ON"
            : "○  Privacy Mode: OFF";
    }

    private void MainWindow_PrivacyAwareButtonClick(object sender, RoutedEventArgs e)
    {
        if (e.Source is not Button button)
            return;

        var label = button.Content?.ToString() ?? string.Empty;
        string? copiedValue = label switch
        {
            "Copy Output" => TextOutputBox.Text,
            "Copy Checksum" => IntegrityResultBox.Text,
            "Copy" when !string.IsNullOrEmpty(GeneratedPasswordBox.Text) => GeneratedPasswordBox.Text,
            _ => null
        };

        if (string.IsNullOrEmpty(copiedValue))
            return;

        try
        {
            _mainProtectedClipboard.CopyText(copiedValue, message => GlobalStatusText.Text = $"● {message}");
        }
        catch
        {
            // The source command already reports ordinary clipboard-access failures.
        }
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is null)
            yield break;

        var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, index);
            if (child is T match)
                yield return match;
            foreach (var descendant in FindVisualChildren<T>(child))
                yield return descendant;
        }
    }
}
