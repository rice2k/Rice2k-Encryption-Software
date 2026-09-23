using System.ComponentModel;
using System.Windows;

namespace Rice2k.Encryption;

public partial class MainWindow
{
    private bool _safeExitRequested;
    private bool _safeExitApproved;
    private bool _safeExitWaitStarted;

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);

        // Do not interfere with an application-wide/fatal shutdown that is already
        // underway. The global exception path intentionally exits rather than
        // continuing in an unknown state.
        if (e.Cancel ||
            _safeExitApproved ||
            Application.Current.Dispatcher.HasShutdownStarted ||
            _operationCts is null)
        {
            return;
        }

        // Normal user close while file crypto is active: preserve the user's close
        // intent, request the same cancellation used by the UI, and keep the WPF
        // process alive until the operation's finally block has released its CTS.
        // This gives the crypto service a chance to remove incomplete temporary
        // output before the application exits.
        e.Cancel = true;
        _safeExitRequested = true;
        GlobalStatusText.Text = "● Cancelling active operation before exit…   |   waiting for safe cleanup";

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
            while (_operationCts is not null && !Dispatcher.HasShutdownStarted)
                await Task.Delay(100);
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
}
