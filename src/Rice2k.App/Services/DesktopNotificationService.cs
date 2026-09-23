namespace Rice2k.Encryption.Services;

public sealed class DesktopNotificationService : IDisposable
{
    private System.Windows.Forms.NotifyIcon? _notifyIcon;
    private bool _disposed;

    public void ShowCompletion(string title, string message)
    {
        if (_disposed || string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(message))
            return;

        try
        {
            _notifyIcon ??= CreateNotifyIcon();
            _notifyIcon.BalloonTipTitle = title.Length <= 63 ? title : title[..63];
            _notifyIcon.BalloonTipText = message.Length <= 255 ? message : message[..255];
            _notifyIcon.BalloonTipIcon = System.Windows.Forms.ToolTipIcon.Info;
            _notifyIcon.ShowBalloonTip(4000);
        }
        catch
        {
            // Notifications are optional convenience UI. Failure must never affect
            // encryption/decryption, vault state, or other security workflows.
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        try
        {
            if (_notifyIcon is not null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
            }
        }
        catch
        {
            // Best-effort notification cleanup only.
        }
        finally
        {
            _notifyIcon = null;
        }
    }

    private static System.Windows.Forms.NotifyIcon CreateNotifyIcon()
    {
        return new System.Windows.Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Shield,
            Text = "Rice2k Encryption Software",
            Visible = true
        };
    }
}
