using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace Rice2k.Encryption;

public partial class RecipientEncryptionWindow
{
    private bool _contactsUiInitialized;

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        InitializeContactsUi();
    }

    private void InitializeContactsUi()
    {
        if (_contactsUiInitialized)
            return;
        _contactsUiInitialized = true;

        if (RecipientsList.Parent is not StackPanel parent)
            return;

        var actions = parent.Children.OfType<WrapPanel>().FirstOrDefault();
        if (actions is null)
            return;

        var button = new Button
        {
            Content = "Saved Contacts…",
            ToolTip = "Choose reusable validated Rice2k public identity contacts",
            MinWidth = 125
        };
        if (TryFindResource("SecondaryButtonStyle") is Style secondary)
            button.Style = secondary;
        AutomationProperties.SetName(button, "Choose saved public identity contacts");
        AutomationProperties.SetHelpText(button, "Opens the local public identity contact book and adds selected contacts as encryption recipients.");
        button.Click += OpenSavedContacts_Click;
        actions.Children.Insert(1, button);
    }

    private void OpenSavedContacts_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
            return;

        var contacts = new PublicIdentityContactsWindow
        {
            Owner = this
        };
        if (contacts.ShowDialog() != true)
            return;

        var added = 0;
        foreach (var contact in contacts.SelectedContacts)
        {
            if (_recipients.Any(existing => existing.Id == contact.Id || existing.Fingerprint == contact.Fingerprint))
                continue;
            _recipients.Add(contact);
            added++;
        }

        EncryptStatusText.Text = $"{_recipients.Count:N0} recipient(s) selected";
        EncryptProgressDetailText.Text = added > 0
            ? $"Added {added:N0} saved contact(s). Compare fingerprints independently when recipient identity matters."
            : "Those contacts were already selected.";
    }
}
