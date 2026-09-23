using System.Windows;
using Rice2k.Encryption.Services;

namespace Rice2k.Encryption;

public partial class WelcomeTourWindow : Window
{
    private readonly AppSettingsService _settingsService;
    private int _pageIndex;

    private sealed record TourPage(string Title, string Headline, string Body, string Tip);

    private static readonly TourPage[] Pages =
    [
        new(
            "Welcome to Rice2k",
            "Protect files without learning cryptography first",
            "Rice2k is designed around guided workflows. Choose a file, choose how to protect it, review the destination, and Rice2k handles the cryptographic details using recommended settings.",
            "You can drag a normal file onto the main window to open Encrypt automatically."),
        new(
            "Your originals stay safe",
            "Rice2k creates a new encrypted file",
            "File encryption writes to a separate temporary output, verifies the encrypted data when enabled, and finalizes a new .r2kenc file. Existing destination files are not overwritten automatically.",
            "Keep your original until you have tested that you can decrypt the new encrypted file successfully."),
        new(
            "Passwords matter",
            "A lost password can mean lost access",
            "Rice2k uses Argon2id to derive encryption keys from passwords, but it cannot recover a password you forget. Use a long unique password or let Rice2k generate one, then store it somewhere you trust.",
            "Recovery Center can create and test separate .r2krecovery backups for Rice2k key packages, but recovery packages cannot bypass an unknown file, key-package, vault, identity, or recovery password."),
        new(
            "Files, folders, and batches",
            "Use the Batch Queue for larger jobs",
            "Drop several files or a folder onto Rice2k to open the Batch Queue. Each file is processed independently with visible progress, and one failed item does not erase the files that completed successfully.",
            "Folder intake skips junction/reparse directories so linked folders cannot create recursive scan loops."),
        new(
            "Ready to begin",
            "Status is always visible",
            "During long operations Rice2k shows the current stage, percentage, bytes processed, speed, elapsed time, and an estimated time remaining. Safe cancellation removes incomplete temporary output while preserving the original source.",
            "This is still a pre-1.0 development build. Keep independent backups of important data and test recovery before relying on it.")
    ];

    public WelcomeTourWindow(AppSettingsService settingsService)
    {
        _settingsService = settingsService;
        InitializeComponent();
        RenderPage();
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (_pageIndex <= 0)
            return;

        _pageIndex--;
        RenderPage();
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_pageIndex >= Pages.Length - 1)
        {
            CompleteTour();
            return;
        }

        _pageIndex++;
        RenderPage();
    }

    private void Skip_Click(object sender, RoutedEventArgs e) => CompleteTour();

    private void CompleteTour()
    {
        var current = _settingsService.Load();
        var saved = _settingsService.TrySave(current with { FirstRunTourCompleted = true });
        if (!saved)
        {
            MessageBox.Show(
                this,
                "Rice2k could not save the first-run completion setting. You can continue using Rice2k, but this welcome tour may appear again the next time the application starts.",
                "Welcome setting was not saved",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        DialogResult = true;
    }

    private void RenderPage()
    {
        var page = Pages[_pageIndex];
        TourTitleText.Text = page.Title;
        TourHeadlineText.Text = page.Headline;
        TourBodyText.Text = page.Body;
        TourTipText.Text = page.Tip;
        TourProgressText.Text = $"{_pageIndex + 1} of {Pages.Length}";
        BackButton.IsEnabled = _pageIndex > 0;
        NextButton.Content = _pageIndex == Pages.Length - 1 ? "Start using Rice2k" : "Next →";
    }
}
