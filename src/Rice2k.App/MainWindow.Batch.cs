using System.Windows;

namespace Rice2k.Encryption;

public partial class MainWindow
{
    protected override void OnPreviewDrop(DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            var existingPaths = paths
                .Where(path => File.Exists(path) || Directory.Exists(path))
                .ToArray();

            var containsFolder = existingPaths.Any(Directory.Exists);
            var normalFileCount = existingPaths.Count(path =>
                File.Exists(path) &&
                !path.EndsWith(".r2kenc", StringComparison.OrdinalIgnoreCase));

            if (containsFolder || normalFileCount > 1)
            {
                e.Handled = true;
                OpenBatchQueue(existingPaths);
                return;
            }
        }

        base.OnPreviewDrop(e);
    }

    private void OpenBatchQueue_Click(object sender, RoutedEventArgs e) => OpenBatchQueue();

    private void OpenBatchQueue(IEnumerable<string>? initialPaths = null)
    {
        var window = new BatchQueueWindow(initialPaths)
        {
            Owner = this
        };

        window.ShowDialog();
    }
}
