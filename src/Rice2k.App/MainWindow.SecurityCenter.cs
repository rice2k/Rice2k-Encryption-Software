using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;

namespace Rice2k.Encryption;

public partial class MainWindow
{
    private bool _securityCenterUiInitialized;

    private void InitializeSecurityCenterUi()
    {
        if (_securityCenterUiInitialized)
            return;
        _securityCenterUiInitialized = true;

        var settingsButton = FindVisualChildren<Button>(this)
            .FirstOrDefault(button => string.Equals(button.Tag?.ToString(), "Settings", StringComparison.Ordinal));
        if (settingsButton?.Parent is not Panel parent)
            return;

        var button = new Button
        {
            Content = "🛡  Security Center",
            Tag = "Rice2kSecurityCenter",
            ToolTip = "Review Rice2k privacy, App Lock, vault auto-lock, recovery, and validation status",
            Margin = new Thickness(0, 2, 0, 2)
        };
        if (TryFindResource("NavButtonStyle") is Style navStyle)
            button.Style = navStyle;
        AutomationProperties.SetName(button, "Open Security Center");
        AutomationProperties.SetHelpText(button, "Review safe local security and privacy status without displaying passwords or secret keys.");
        button.Click += OpenSecurityCenter_Click;

        var index = parent.Children.IndexOf(settingsButton);
        parent.Children.Insert(index >= 0 ? index : parent.Children.Count, button);
    }

    private void OpenSecurityCenter_Click(object sender, RoutedEventArgs e) => OpenSecurityCenter();

    private void OpenSecurityCenter()
    {
        if (_appLockActive)
            return;

        var window = new SecurityCenterWindow
        {
            Owner = this
        };
        window.ShowDialog();

        if (window.LockRequested)
        {
            RequestAppLock("Rice2k was locked from Security Center.");
            return;
        }

        if (window.OpenSettingsRequested)
            NavigateToAndFocus("Settings", focusSettingsSearch: true);
    }
}
