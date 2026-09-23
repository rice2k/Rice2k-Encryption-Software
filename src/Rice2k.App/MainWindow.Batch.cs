using System.Windows;

namespace Rice2k.Encryption;

public partial class MainWindow
{
    private void OpenBatchQueue_Click(object sender, RoutedEventArgs e)
    {
        var window = new BatchQueueWindow
        {
            Owner = this
        };

        window.ShowDialog();
    }
}
