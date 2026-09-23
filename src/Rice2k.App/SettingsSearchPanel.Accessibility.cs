namespace Rice2k.Encryption;

public partial class SettingsSearchPanel
{
    public bool FocusSearchBox()
    {
        if (!SettingsSearchBox.IsVisible || !SettingsSearchBox.IsEnabled)
            return false;

        SettingsSearchBox.Focus();
        SettingsSearchBox.SelectAll();
        return SettingsSearchBox.IsKeyboardFocusWithin;
    }
}
