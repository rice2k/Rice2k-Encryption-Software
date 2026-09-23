using System.Windows;

namespace Rice2k.Encryption;

public partial class App
{
    private void ApplyStartupAccessibilityTheme()
    {
        if (!SystemParameters.HighContrast)
            return;

        Resources["BackgroundBrush"] = SystemColors.WindowBrush;
        Resources["SurfaceBrush"] = SystemColors.WindowBrush;
        Resources["Surface2Brush"] = SystemColors.ControlBrush;
        Resources["BorderBrush"] = SystemColors.WindowTextBrush;
        Resources["PrimaryBrush"] = SystemColors.HighlightBrush;
        Resources["PrimaryHoverBrush"] = SystemColors.HighlightBrush;
        Resources["SecondaryBrush"] = SystemColors.HighlightBrush;
        Resources["TextBrush"] = SystemColors.WindowTextBrush;
        Resources["MutedTextBrush"] = SystemColors.GrayTextBrush;

        // In high-contrast mode status meaning must remain readable without relying on color.
        // Existing status strings already use text/icons such as ✓, ⚠, and ✗, so these
        // semantic brushes intentionally collapse to the system foreground color.
        Resources["SuccessBrush"] = SystemColors.WindowTextBrush;
        Resources["WarningBrush"] = SystemColors.WindowTextBrush;
        Resources["ErrorBrush"] = SystemColors.WindowTextBrush;
    }
}
