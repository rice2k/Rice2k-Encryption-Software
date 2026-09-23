namespace Rice2k.Encryption;

public partial class BatchQueueWindow
{
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Closing += Window_Closing;
        InitializePauseUi();
        InitializeBatchAccessibility();
    }
}
