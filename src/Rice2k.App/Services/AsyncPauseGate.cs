namespace Rice2k.Encryption.Services;

internal sealed class AsyncPauseGate
{
    private TaskCompletionSource<bool>? _resumeSignal;

    public bool IsPaused => Volatile.Read(ref _resumeSignal) is not null;

    public void Pause()
    {
        var signal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Interlocked.CompareExchange(ref _resumeSignal, signal, null);
    }

    public void Resume()
    {
        var signal = Interlocked.Exchange(ref _resumeSignal, null);
        signal?.TrySetResult(true);
    }

    public async ValueTask WaitIfPausedAsync(CancellationToken cancellationToken)
    {
        while (Volatile.Read(ref _resumeSignal) is { } signal)
            await signal.Task.WaitAsync(cancellationToken);
    }
}
