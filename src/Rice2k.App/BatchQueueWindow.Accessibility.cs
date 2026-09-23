using System.Windows.Automation;

namespace Rice2k.Encryption;

public partial class BatchQueueWindow
{
    private void InitializeBatchAccessibility()
    {
        AutomationProperties.SetName(BatchQueueGrid, "Batch encryption file queue");
        AutomationProperties.SetHelpText(BatchQueueGrid, "Files are processed sequentially. A failure on one item does not remove successful encrypted outputs from earlier items.");

        AutomationProperties.SetName(AddFilesButton, "Add files to batch queue");
        AutomationProperties.SetName(AddFolderButton, "Add folder files to batch queue");
        AutomationProperties.SetHelpText(AddFolderButton, "Scans the selected folder and adds accessible files. Reparse-point folders are skipped for safety.");
        AutomationProperties.SetName(RemoveSelectedButton, "Remove selected queue items");
        AutomationProperties.SetName(ClearQueueButton, "Clear batch queue");

        AutomationProperties.SetName(BatchPasswordBox, "Batch encryption password");
        AutomationProperties.SetHelpText(BatchPasswordBox, "This password protects every file in the current batch. Use at least 12 characters.");
        AutomationProperties.SetName(BatchConfirmPasswordBox, "Confirm batch encryption password");
        AutomationProperties.SetName(StartBatchButton, "Start batch encryption");
        AutomationProperties.SetName(PauseBatchButton, "Pause or resume batch encryption");
        AutomationProperties.SetName(CancelBatchButton, "Cancel batch safely");
        AutomationProperties.SetHelpText(CancelBatchButton, "Stops the active operation safely, removes its incomplete temporary output and keeps files that already completed.");

        AutomationProperties.SetName(BatchStatusText, "Batch operation status");
        AutomationProperties.SetLiveSetting(BatchStatusText, AutomationLiveSetting.Polite);
        AutomationProperties.SetName(BatchProgressDetails, "Batch progress details");
        AutomationProperties.SetLiveSetting(BatchProgressDetails, AutomationLiveSetting.Polite);
    }
}
