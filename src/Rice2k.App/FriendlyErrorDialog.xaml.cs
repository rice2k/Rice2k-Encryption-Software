using System.Windows;

namespace Rice2k.Encryption;

public partial class FriendlyErrorDialog : Window
{
    private FriendlyErrorDialog(
        string message,
        string? technicalDetails,
        string? suggestion,
        string? heading)
    {
        InitializeComponent();

        HeadingText.Text = string.IsNullOrWhiteSpace(heading)
            ? "Rice2k needs your attention"
            : heading;
        MessageText.Text = message;

        if (string.IsNullOrWhiteSpace(suggestion))
        {
            SuggestionPanel.Visibility = Visibility.Collapsed;
        }
        else
        {
            SuggestionText.Text = suggestion;
        }

        if (string.IsNullOrWhiteSpace(technicalDetails))
        {
            TechnicalExpander.Visibility = Visibility.Collapsed;
            CopyDetailsButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            TechnicalDetailsText.Text = technicalDetails;
        }
    }

    public static void ShowError(
        Window owner,
        string message,
        string? technicalDetails = null,
        string? suggestion = null,
        string? heading = null)
    {
        var dialog = new FriendlyErrorDialog(message, technicalDetails, suggestion, heading)
        {
            Owner = owner
        };
        dialog.ShowDialog();
    }

    private void CopyDetails_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(TechnicalDetailsText.Text))
            Clipboard.SetText(TechnicalDetailsText.Text);
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
