using System.ComponentModel;
using System.Reflection;
using System.Windows.Controls;

namespace Rice2k.Encryption;

public partial class MainWindow
{
    private bool _normalizingVersionStatus;

    private void InitializeVersionStatus()
    {
        GlobalStatusText.Text = $"● Ready   |   Offline/local   |   v{GetApplicationVersion()}";

        // MainWindow.xaml.cs still contains a few milestone-era status messages with
        // a hard-coded v0.2-dev suffix. Normalize those user-visible strings until the
        // large legacy file is decomposed during a later refactor.
        var descriptor = DependencyPropertyDescriptor.FromProperty(
            TextBlock.TextProperty,
            typeof(TextBlock));
        descriptor?.AddValueChanged(GlobalStatusText, (_, _) => NormalizeLegacyVersionStatus());
    }

    private void NormalizeLegacyVersionStatus()
    {
        if (_normalizingVersionStatus ||
            string.IsNullOrWhiteSpace(GlobalStatusText.Text) ||
            !GlobalStatusText.Text.Contains("v0.2-dev", StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            _normalizingVersionStatus = true;
            GlobalStatusText.Text = GlobalStatusText.Text.Replace(
                "v0.2-dev",
                $"v{GetApplicationVersion()}",
                StringComparison.Ordinal);
        }
        finally
        {
            _normalizingVersionStatus = false;
        }
    }

    private static string GetApplicationVersion()
    {
        var informational = typeof(MainWindow).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        return string.IsNullOrWhiteSpace(informational)
            ? typeof(MainWindow).Assembly.GetName().Version?.ToString() ?? "development"
            : informational.Split('+', 2)[0];
    }
}
