using System.Windows;

namespace Rice2k.Encryption;

public partial class BatchQueueWindow
{
    private void InitializePauseUi()
    {
        PauseBatchButton.IsEnabled = CancelBatchButton.IsEnabled;
        CancelBatchButton.IsEnabledChanged += (_, _) =>
        {
            PauseBatchButton.IsEnabled = CancelBatchButton.IsEnabled;
            if (!CancelBatchButton.IsEnabled)
            {
                _fileCrypto.Resume();
                PauseBatchButton.Content = "Pause";
            }
        };
    }

    private void PauseBatch_Click(object sender, RoutedEventArgs e)
    {
        if (_batchCts is null)
            return;

        if (_fileCrypto.IsPaused)
        {
            _fileCrypto.Resume();
            PauseBatchButton.Content = "Pause";
            BatchStatusText.Text = "Resuming batch…";
            BatchProgressDetails.Text = "Rice2k is continuing from the next safe chunk boundary.";
        }
        else
        {
            _fileCrypto.Pause();
            PauseBatchButton.Content = "Resume";
            BatchStatusText.Text = "Paused";
            BatchProgressDetails.Text = "The active file will remain safely paused at the next chunk boundary. Cancel is still available.";
        }
    }
}
