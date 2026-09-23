using System.Windows;
using System.Windows.Controls;

namespace Rice2k.Encryption;

public partial class MainWindow
{
    private Button? _encryptPauseButton;
    private Button? _decryptPauseButton;
    private bool _pauseUiInitialized;

    private void InitializePauseButtons()
    {
        if (_pauseUiInitialized)
            return;

        _pauseUiInitialized = true;
        _encryptPauseButton = CreatePauseButton();
        _decryptPauseButton = CreatePauseButton();

        _encryptPauseButton.Click += PauseSingleOperation_Click;
        _decryptPauseButton.Click += PauseSingleOperation_Click;

        if (EncryptCancelButton.Parent is Panel encryptPanel)
            encryptPanel.Children.Insert(Math.Max(0, encryptPanel.Children.IndexOf(EncryptCancelButton)), _encryptPauseButton);

        if (DecryptCancelButton.Parent is Panel decryptPanel)
            decryptPanel.Children.Insert(Math.Max(0, decryptPanel.Children.IndexOf(DecryptCancelButton)), _decryptPauseButton);

        SyncPauseButton(_encryptPauseButton, EncryptCancelButton.IsEnabled);
        SyncPauseButton(_decryptPauseButton, DecryptCancelButton.IsEnabled);

        EncryptCancelButton.IsEnabledChanged += (_, _) =>
            SyncPauseButton(_encryptPauseButton, EncryptCancelButton.IsEnabled);
        DecryptCancelButton.IsEnabledChanged += (_, _) =>
            SyncPauseButton(_decryptPauseButton, DecryptCancelButton.IsEnabled);
    }

    private Button CreatePauseButton()
    {
        var button = new Button
        {
            Content = "Pause",
            MinWidth = 100,
            IsEnabled = false,
            Margin = new Thickness(0, 0, 8, 0)
        };

        if (TryFindResource("SecondaryButtonStyle") is Style secondaryStyle)
            button.Style = secondaryStyle;

        return button;
    }

    private void PauseSingleOperation_Click(object sender, RoutedEventArgs e)
    {
        if (_operationCts is null || sender is not Button button)
            return;

        if (_fileCrypto.IsPaused)
        {
            _fileCrypto.Resume();
            button.Content = "Pause";

            if (ReferenceEquals(button, _encryptPauseButton))
            {
                EncryptStatusText.Text = "Resuming encryption…";
                EncryptProgressDetails.Text = "Continuing from the next safe chunk boundary.";
            }
            else
            {
                DecryptStatusText.Text = "Resuming decryption…";
                DecryptProgressDetails.Text = "Continuing from the next safe chunk boundary.";
            }

            GlobalStatusText.Text = "● Resuming operation…";
        }
        else
        {
            _fileCrypto.Pause();
            button.Content = "Resume";

            if (ReferenceEquals(button, _encryptPauseButton))
            {
                EncryptStatusText.Text = "Paused";
                EncryptProgressDetails.Text = "Encryption is paused at the next safe chunk boundary. Cancel remains available.";
            }
            else
            {
                DecryptStatusText.Text = "Paused";
                DecryptProgressDetails.Text = "Decryption is paused at the next safe chunk boundary. Cancel remains available.";
            }

            GlobalStatusText.Text = "● Paused   |   Cancel remains available";
        }
    }

    private void SyncPauseButton(Button? button, bool operationRunning)
    {
        if (button is null)
            return;

        button.IsEnabled = operationRunning;
        if (!operationRunning)
        {
            _fileCrypto.Resume();
            button.Content = "Pause";
        }
    }
}
