using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace Rice2k.Encryption;

public partial class MainWindow
{
    private bool _safeExitRequested;
    private bool _safeExitApproved;
    private bool _safeExitWaitStarted;

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);

        var settingsSecurityBusy = IsSettingsSecurityOperationActive();

        // Do not interfere with an application-wide/fatal shutdown that is already
        // underway. The global exception path intentionally exits rather than
        // continuing in an unknown state.
        if (e.Cancel ||
            _safeExitApproved ||
            Application.Current.Dispatcher.HasShutdownStarted ||
            (_operationCts is null && !settingsSecurityBusy))
        {
            return;
        }

        // Preserve the user's normal close intent while security-sensitive work is
        // active. File/integrity work is cancellation-aware; App Lock credential
        // setup/removal uses synchronous Argon2/atomic credential operations on a
        // worker thread and is allowed to reach its consistency boundary before exit.
        e.Cancel = true;
        _safeExitRequested = true;
        GlobalStatusText.Text = settingsSecurityBusy
            ? "● Waiting for security settings to finish before exit…   |   preserving App Lock consistency"
            : "● Cancelling active operation before exit…   |   waiting for safe cleanup";

        if (_operationCts is not null)
        {
            try
            {
                if (!_operationCts.IsCancellationRequested)
                    _operationCts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The operation is already crossing its cleanup boundary. The waiter
                // below will observe the field becoming null and finish the close.
            }
        }

        if (!_safeExitWaitStarted)
        {
            _safeExitWaitStarted = true;
            _ = CompleteSafeExitAfterOperationAsync();
        }
    }

    private async Task CompleteSafeExitAfterOperationAsync()
    {
        try
        {
            while ((_operationCts is not null || IsSettingsSecurityOperationActive()) &&
                   !Dispatcher.HasShutdownStarted)
            {
                await Task.Delay(100);
            }
        }
        finally
        {
            _safeExitWaitStarted = false;
        }

        if (!_safeExitRequested || Dispatcher.HasShutdownStarted)
            return;

        _safeExitRequested = false;
        _safeExitApproved = true;
        Dispatcher.BeginInvoke(new Action(Close));
    }

    private bool IsSettingsSecurityOperationActive()
    {
        if (SettingsPage.Content is not StackPanel root)
            return false;

        return root.Children
            .OfType<SettingsSearchPanel>()
            .Any(panel => panel.HasActiveAppLockOperation);
    }
}
