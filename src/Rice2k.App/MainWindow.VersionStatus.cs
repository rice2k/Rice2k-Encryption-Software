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
        var version = GetApplicationVersion();
        GlobalStatusText.Text = $"● Ready   |   Offline/local   |   v{version}";

        // The legacy XAML footer still contains a milestone-era "v0.2 development"
        // literal and has no x:Name. Replace that visible label at runtime so both
        // status surfaces derive from the assembly informational version.
        var sidebarVersion = FindVisualChildren<TextBlock>(this)
            .FirstOrDefault(text => string.Equals(text.Text, "v0.2 development", StringComparison.Ordinal));
        if (sidebarVersion is not null)
            sidebarVersion.Text = $"v{version}";

        // Do not turn source configuration into a release-validation claim. The
        // authenticated-encryption/KDF design is configured, but the supported
        // Windows Release build/test gates are tracked separately until executed.
        var homeText = FindVisualChildren<TextBlock>(HomePage).ToArray();
        var securityBadge = homeText.FirstOrDefault(text => string.Equals(text.Text, "STRONG", StringComparison.Ordinal));
        if (securityBadge is not null)
            securityBadge.Text = "PREVIEW";

        var securitySummary = homeText.FirstOrDefault(text =>
            string.Equals(
                text.Text,
                "✓ Encryption engine configured   ✓ Authenticated encryption   ✓ Argon2id",
                StringComparison.Ordinal));
        if (securitySummary is not null)
            securitySummary.Text = "✓ Authenticated-encryption design configured   ✓ Argon2id   ○ Windows release validation pending";

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
