using System.ComponentModel;
using System.Windows;

namespace Rice2k.Encryption;

public partial class BatchQueueWindow
{
    private void PauseBatch_Click(object sender, RoutedEventArgs e)
    {
        if (_batchCts is null)
            return;

        if (_fileCrypto.IsPaused)
        {
            _fileCrypto.Resume();
            PauseBatchButton.Content = "Pause";
            BatchStatusText.Text = "Batch resumed";
            BatchProgressDetails.Text = "Continuing from the next safe encryption chunk boundary.";
        }
        else
        {
            _fileCrypto.Pause();
            PauseBatchButton.Content = "Resume";
            BatchStatusText.Text = "Batch paused safely";
            BatchProgressDetails.Text = "The active file is paused at a safe chunk boundary. Completed files remain intact.";
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_batchCts is null)
        {
            base.OnClosing(e);
            return;
        }

        var result = MessageBox.Show(
            this,
            "A batch is still running. Stop it and close after Rice2k cleans up the active temporary file?\n\nCompleted encrypted files will be kept.",
            "Batch still running",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        e.Cancel = true;

        if (result == MessageBoxResult.Yes)
        {
            _closeWhenFinished = true;
            BatchStatusText.Text = "Cancelling safely before closing…";
            _fileCrypto.Resume();
            _batchCts.Cancel();
        }

        base.OnClosing(e);
    }
}
