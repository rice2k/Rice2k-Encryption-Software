using System.Windows;
using System.Windows.Controls;
using Rice2k.Encryption.Services;

namespace Rice2k.Encryption;

public partial class SettingsSearchPanel
{
    private readonly ProtectedClipboardService _settingsProtectedClipboard = new();
    private bool _clipboardClearHardeningInitialized;

    private void InitializeClipboardClearHardening()
    {
        if (_clipboardClearHardeningInitialized)
            return;

        var clearButton = FindSettingsLogicalChildren<Button>(this)
            .FirstOrDefault(button => string.Equals(button.Content?.ToString(), "Clear Clipboard Now", StringComparison.Ordinal));
        if (clearButton is null)
            return;

        _clipboardClearHardeningInitialized = true;
        clearButton.Click -= ClearClipboardNow_Click;
        clearButton.Click += ClearClipboardNowProtected_Click;
    }

    private void ClearClipboardNowProtected_Click(object sender, RoutedEventArgs e)
    {
        PrivacyStatusText.Text = _settingsProtectedClipboard.ClearNow()
            ? "✓ Windows clipboard cleared now. Older Rice2k auto-clear timers were invalidated."
            : "⚠ Windows clipboard could not be cleared. Try again or clear it from Windows/another application.";
    }

    private static IEnumerable<T> FindSettingsLogicalChildren<T>(DependencyObject root) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is T match)
                yield return match;
            if (child is not DependencyObject dependencyChild)
                continue;
            foreach (var descendant in FindSettingsLogicalChildren<T>(dependencyChild))
                yield return descendant;
        }
    }
}
