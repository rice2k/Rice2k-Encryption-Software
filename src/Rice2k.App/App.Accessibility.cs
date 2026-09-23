using System.ComponentModel;
using System.Windows;
using System.Windows.Media;

namespace Rice2k.Encryption;

public partial class App
{
    private readonly Dictionary<string, Color> _defaultAccessibilityColors = new(StringComparer.Ordinal);
    private bool _accessibilityThemeInitialized;

    private void ApplyStartupAccessibilityTheme()
    {
        if (!_accessibilityThemeInitialized)
        {
            _accessibilityThemeInitialized = true;
            PrepareMutableApplicationBrushes();
            SystemParameters.StaticPropertyChanged += SystemParameters_StaticPropertyChanged;
        }

        ApplyCurrentAccessibilityPalette();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_accessibilityThemeInitialized)
            SystemParameters.StaticPropertyChanged -= SystemParameters_StaticPropertyChanged;
        base.OnExit(e);
    }

    private void SystemParameters_StaticPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!string.Equals(e.PropertyName, nameof(SystemParameters.HighContrast), StringComparison.Ordinal))
            return;

        Dispatcher.BeginInvoke(new Action(ApplyCurrentAccessibilityPalette));
    }

    private void PrepareMutableApplicationBrushes()
    {
        string[] keys =
        [
            "BackgroundBrush",
            "SurfaceBrush",
            "Surface2Brush",
            "BorderBrush",
            "PrimaryBrush",
            "PrimaryHoverBrush",
            "SecondaryBrush",
            "TextBrush",
            "MutedTextBrush",
            "SuccessBrush",
            "WarningBrush",
            "ErrorBrush"
        ];

        foreach (var key in keys)
        {
            if (Resources[key] is not SolidColorBrush existing)
                continue;

            _defaultAccessibilityColors[key] = existing.Color;
            // WPF may freeze resource brushes. Replace them before the main window is
            // created so StaticResource consumers receive a mutable shared instance.
            Resources[key] = new SolidColorBrush(existing.Color);
        }
    }

    private void ApplyCurrentAccessibilityPalette()
    {
        if (SystemParameters.HighContrast)
        {
            SetBrushColor("BackgroundBrush", SystemColors.WindowColor);
            SetBrushColor("SurfaceBrush", SystemColors.WindowColor);
            SetBrushColor("Surface2Brush", SystemColors.ControlColor);
            SetBrushColor("BorderBrush", SystemColors.WindowTextColor);
            SetBrushColor("PrimaryBrush", SystemColors.HighlightColor);
            SetBrushColor("PrimaryHoverBrush", SystemColors.HighlightColor);
            SetBrushColor("SecondaryBrush", SystemColors.HighlightColor);
            SetBrushColor("TextBrush", SystemColors.WindowTextColor);
            SetBrushColor("MutedTextBrush", SystemColors.GrayTextColor);

            // Status must remain understandable without relying on color. Rice2k also
            // uses explicit text/icons such as ✓, ⚠, and ✗ throughout status UI.
            SetBrushColor("SuccessBrush", SystemColors.WindowTextColor);
            SetBrushColor("WarningBrush", SystemColors.WindowTextColor);
            SetBrushColor("ErrorBrush", SystemColors.WindowTextColor);
            return;
        }

        foreach (var pair in _defaultAccessibilityColors)
            SetBrushColor(pair.Key, pair.Value);
    }

    private void SetBrushColor(string key, Color color)
    {
        if (Resources[key] is SolidColorBrush brush && !brush.IsFrozen)
        {
            brush.Color = color;
            return;
        }

        Resources[key] = new SolidColorBrush(color);
    }
}
