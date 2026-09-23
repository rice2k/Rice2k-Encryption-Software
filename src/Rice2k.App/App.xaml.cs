using System.Windows;
using System.Windows.Threading;

namespace Rice2k.Encryption;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        ApplyStartupAccessibilityTheme();
        base.OnStartup(e);
    }

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;

        var message = "Rice2k encountered an unexpected application error. To avoid continuing in an unknown state, the application will close. Your original source files are not intentionally removed by Rice2k's encryption workflows.";
        var suggestion = "Keep your original files, restart Rice2k, and try the operation again. If the problem repeats, copy the technical details when reporting the issue.";

        try
        {
            if (Current.MainWindow is Window owner && owner.IsVisible)
            {
                FriendlyErrorDialog.ShowError(
                    owner,
                    message,
                    e.Exception.ToString(),
                    suggestion,
                    "Unexpected Rice2k error");
            }
            else
            {
                MessageBox.Show(message, "Rice2k Encryption Software", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        finally
        {
            Shutdown(-1);
        }
    }
}
