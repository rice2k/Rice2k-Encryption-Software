using System.Windows;

namespace Rice2k.Encryption;

public partial class MainWindow
{
    protected override void OnPreviewDrop(DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            var normalFiles = paths
                .Where(File.Exists)
                .Where(path => !path.EndsWith(".r2kenc", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (normalFiles.Length > 1)
            {
                e.Handled = true;
                OpenBatchQueue(normalFiles);
                return;
            }
        }

        base.OnPreviewDrop(e);
    }

    private void OpenBatchQueue_Click(object sender, RoutedEventArgs e) => OpenBatchQueue();

    private void OpenBatchQueue(IEnumerable<string>? initialFiles = null)
    {
        var window = new BatchQueueWindow(initialFiles)
        {
            Owner = this
        };

        window.ShowDialog();
    }
}
