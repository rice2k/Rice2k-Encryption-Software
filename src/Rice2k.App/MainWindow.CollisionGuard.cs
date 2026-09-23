using System.Windows;
using System.Windows.Input;

namespace Rice2k.Encryption;

public partial class MainWindow
{
    private bool _collisionGuardsInitialized;

    private void InitializeCollisionGuards()
    {
        if (_collisionGuardsInitialized)
            return;

        _collisionGuardsInitialized = true;
        EncryptStartButton.PreviewMouseLeftButtonDown += EncryptStartButton_PreviewMouseLeftButtonDown;
        DecryptStartButton.PreviewMouseLeftButtonDown += DecryptStartButton_PreviewMouseLeftButtonDown;
    }

    private void EncryptStartButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!ResolveLateDestinationCollision(
                EncryptDestinationBox.Text,
                path =>
                {
                    EncryptDestinationBox.Text = path;
                    PopulateEncryptReview();
                    RunEncryptPreflight(showDialog: false);
                },
                () => BrowseEncryptDestination_Click(EncryptStartButton, new RoutedEventArgs())))
        {
            e.Handled = true;
        }
    }

    private void DecryptStartButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!ResolveLateDestinationCollision(
                DecryptDestinationBox.Text,
                path =>
                {
                    DecryptDestinationBox.Text = path;
                    PopulateDecryptReview();
                    RunDecryptPreflight(showDialog: false);
                },
                () => BrowseDecryptDestination_Click(DecryptStartButton, new RoutedEventArgs())))
        {
            e.Handled = true;
        }
    }

    private bool ResolveLateDestinationCollision(
        string destinationPath,
        Action<string> usePath,
        Action chooseDifferentLocation)
    {
        if (string.IsNullOrWhiteSpace(destinationPath) || !File.Exists(destinationPath))
            return true;

        var keepBothPath = CreateNonCollidingPath(destinationPath);
        var result = MessageBox.Show(
            this,
            $"Another file now exists at the selected output location.\n\nYes — Keep both\nRice2k will save as:\n{Path.GetFileName(keepBothPath)}\n\nNo — Choose a different location\nCancel — Return to the review screen\n\nRice2k will not overwrite the existing file automatically.",
            "Output file already exists",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            usePath(keepBothPath);
            GlobalStatusText.Text = $"● Keep Both selected   |   Output: {Path.GetFileName(keepBothPath)}";
            return true;
        }

        if (result == MessageBoxResult.No)
            chooseDifferentLocation();

        return false;
    }
}
