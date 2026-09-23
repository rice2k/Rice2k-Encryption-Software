using System.Collections.Specialized;
using Rice2k.Encryption.Services;

namespace Rice2k.Encryption;

public partial class MainWindow
{
    private readonly DesktopNotificationService _desktopNotifications = new();
    private INotifyCollectionChanged? _notificationActivityNotifier;
    private bool _desktopNotificationsInitialized;

    private void InitializeDesktopNotificationsUi()
    {
        if (_desktopNotificationsInitialized)
            return;

        _desktopNotificationsInitialized = true;
        _notificationActivityNotifier = ActivityList.Items as INotifyCollectionChanged;
        if (_notificationActivityNotifier is not null)
            _notificationActivityNotifier.CollectionChanged += NotificationActivity_CollectionChanged;

        Closed += (_, _) =>
        {
            if (_notificationActivityNotifier is not null)
                _notificationActivityNotifier.CollectionChanged -= NotificationActivity_CollectionChanged;
            _desktopNotifications.Dispose();
        };
    }

    private void NotificationActivity_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is null)
            return;

        var settings = _appSettingsService.Load();
        if (!settings.DesktopNotificationsEnabled || settings.PrivacyModeEnabled)
            return;

        foreach (var displayText in e.NewItems.OfType<string>())
        {
            var action = PrivacyHistoryService.RedactActivityDisplayText(displayText);
            switch (action)
            {
                case "Encryption complete":
                    _desktopNotifications.ShowCompletion(
                        "Rice2k encryption complete",
                        "Your encrypted output finished successfully. Open Rice2k to review verification details.");
                    break;

                case "Decryption complete":
                    _desktopNotifications.ShowCompletion(
                        "Rice2k decryption complete",
                        "Your authenticated restore finished successfully. Open Rice2k to review the output details.");
                    break;

                case "Recovery test passed":
                case "Recovery test complete":
                    _desktopNotifications.ShowCompletion(
                        "Rice2k recovery test complete",
                        "The recovery test completed successfully. No secret recovery material is included in this notification.");
                    break;
            }
        }
    }
}
