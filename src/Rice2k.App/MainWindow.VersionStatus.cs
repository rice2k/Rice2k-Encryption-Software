using System.ComponentModel;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows.Controls;

namespace Rice2k.Encryption;

public partial class MainWindow
{
    private static readonly Regex LegacyDevelopmentVersionPattern = new(
        @"\bv\d+(?:\.\d+){1,3}-dev\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private bool _normalizingVersionStatus;

    private void InitializeVersionStatus()
    {
        GlobalStatusText.Text = $"● Ready   |   Offline/local   |   v{GetApplicationVersion()}";

        // Older MainWindow partials still contain milestone-era status messages such
        // as v0.2-dev and v0.3-dev. Normalize any legacy *-dev version token until
        // those larger files are decomposed and the literals can be removed directly.
        var descriptor = DependencyPropertyDescriptor.FromProperty(
            TextBlock.TextProperty,
            typeof(TextBlock));
        descriptor?.AddValueChanged(GlobalStatusText, (_, _) => NormalizeLegacyVersionStatus());
    }

    private void NormalizeLegacyVersionStatus()
    {
        if (_normalizingVersionStatus || string.IsNullOrWhiteSpace(GlobalStatusText.Text))
            return;

        if (!LegacyDevelopmentVersionPattern.IsMatch(GlobalStatusText.Text))
            return;

        try
        {
            _normalizingVersionStatus = true;
            GlobalStatusText.Text = LegacyDevelopmentVersionPattern.Replace(
                GlobalStatusText.Text,
                $"v{GetApplicationVersion()}");
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
