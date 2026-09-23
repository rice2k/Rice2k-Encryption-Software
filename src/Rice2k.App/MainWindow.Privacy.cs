using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Rice2k.Encryption.Services;

namespace Rice2k.Encryption;

public partial class MainWindow
{
    private Rice2kAppSettings _privacySettings = new();
    private Button? _privacyQuickButton;
    private DispatcherTimer? _privacyScrubTimer;
    private bool _privacyUiInitialized;
    private bool _privacyWasEnabled;
    private long _clipboardGeneration;

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
        _appSettingsService.TrySave(updated);
        ApplyPrivacySettings(updated);
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

        if (enablingNow && settings.ClearSensitivePreviewsWhenPrivacyModeStarts)
            ClearTransientSensitivePreviews();

        RefreshPrivacyQuickToggle();
        if (_privacyUiInitialized)
        {
            GlobalStatusText.Text = settings.PrivacyModeEnabled
                ? "● Privacy Mode ON   |   Local / offline"
                : "● Privacy Mode OFF   |   Local / offline";
        }
    }

    private void ClearTransientSensitivePreviews()
    {
        TextInputBox.Clear();
        TextOutputBox.Clear();
        TextPasswordBox.Clear();
        GeneratedPasswordBox.Clear();
        EncryptPasswordBox.Clear();
        EncryptConfirmPasswordBox.Clear();
        DecryptPasswordBox.Clear();

        ActivityList.Items.Clear();
        Interlocked.Increment(ref _clipboardGeneration);
        TryClearClipboardNow();
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
        if (e.OriginalSource is not Button button || _privacySettings.ClipboardAutoClearSeconds <= 0)
            return;

        var label = button.Content?.ToString() ?? string.Empty;
        string? copiedValue = label switch
        {
            "Copy Output" => TextOutputBox.Text,
            "Copy Checksum" => IntegrityResultBox.Text,
            "Copy" when button.Parent is Panel && !string.IsNullOrEmpty(GeneratedPasswordBox.Text) => GeneratedPasswordBox.Text,
            _ => null
        };

        if (string.IsNullOrEmpty(copiedValue))
            return;

        ScheduleClipboardClear(copiedValue, _privacySettings.ClipboardAutoClearSeconds);
    }

    private void ScheduleClipboardClear(string expectedValue, int seconds)
    {
        var generation = Interlocked.Increment(ref _clipboardGeneration);
        _ = ClearClipboardLaterAsync(expectedValue, seconds, generation);
    }

    private async Task ClearClipboardLaterAsync(string expectedValue, int seconds, long generation)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(seconds));
            if (generation != Interlocked.Read(ref _clipboardGeneration))
                return;

            await Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    if (!Clipboard.ContainsText())
                        return;
                    var current = Clipboard.GetText();
                    if (!string.Equals(current, expectedValue, StringComparison.Ordinal))
                        return;

                    Clipboard.Clear();
                    GlobalStatusText.Text = "● Clipboard auto-cleared   |   Local / offline";
                }
                catch
                {
                    // Clipboard access is best effort because another process can own it temporarily.
                }
            });
        }
        catch
        {
            // The privacy timer must never crash the application.
        }
    }

    private void TryClearClipboardNow()
    {
        try
        {
            Clipboard.Clear();
        }
        catch
        {
            // Best effort only; Windows clipboard ownership can be transient.
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
