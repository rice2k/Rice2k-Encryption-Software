using System.Windows;

namespace Rice2k.Encryption;

public partial class MainWindow
{
    /// <summary>
    /// Stabilization override for the original Window_Drop handler in MainWindow.xaml.cs.
    /// Handling the routed event here prevents the older milestone-era handler from
    /// showing stale "batch support is coming later" messaging.
    /// </summary>
    protected override void OnDrop(DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] droppedPaths)
        {
            base.OnDrop(e);
            return;
        }

        var paths = droppedPaths
            .Where(path => File.Exists(path) || Directory.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (paths.Length == 0)
        {
            e.Handled = true;
            ShowFriendlyError("Drop a file or folder onto Rice2k.");
            return;
        }

        var containsFolder = paths.Any(Directory.Exists);
        var encryptedFiles = paths
            .Where(File.Exists)
            .Where(path => path.EndsWith(".r2kenc", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var normalFiles = paths
            .Where(File.Exists)
            .Where(path => !path.EndsWith(".r2kenc", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        // A single encrypted container belongs in the guided Decrypt workflow.
        if (!containsFolder && paths.Length == 1 && encryptedFiles.Length == 1)
        {
            SetDecryptSource(encryptedFiles[0]);
            ShowDecryptStep(1);
            NavigateTo("Decrypt");
            e.Handled = true;
            return;
        }

        // One ordinary file belongs in the guided Encrypt workflow.
        if (!containsFolder && paths.Length == 1 && normalFiles.Length == 1)
        {
            SetEncryptSource(normalFiles[0]);
            ShowEncryptStep(1);
            NavigateTo("Encrypt");
            e.Handled = true;
            return;
        }

        // Batch Queue currently encrypts normal files/folders. Existing .r2kenc
        // inputs are deliberately skipped by BatchQueueWindow with an explanation.
        if (normalFiles.Length > 0 || containsFolder)
        {
            var queue = new BatchQueueWindow(paths)
            {
                Owner = this
            };
            queue.ShowDialog();
            GlobalStatusText.Text = "● Batch Queue closed   |   Ready";
            e.Handled = true;
            return;
        }

        // Multiple encrypted containers are not silently treated as an encryption batch.
        e.Handled = true;
        ShowFriendlyError(
            "Batch Queue currently protects normal files and folders. To restore encrypted .r2kenc files, drop one encrypted file at a time into the Decrypt workflow.");
    }
}
