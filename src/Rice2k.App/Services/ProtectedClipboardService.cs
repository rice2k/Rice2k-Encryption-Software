using System.Windows;

namespace Rice2k.Encryption.Services;

internal interface IProtectedClipboardAdapter
{
    void SetText(string value);
    bool Clear();
    Task<bool> ClearIfMatchesAsync(string expectedValue);
}

internal sealed class WpfProtectedClipboardAdapter : IProtectedClipboardAdapter
{
    public void SetText(string value) => Clipboard.SetText(value);

    public bool Clear()
    {
        try
        {
            Clipboard.Clear();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> ClearIfMatchesAsync(string expectedValue)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
            return false;

        return await dispatcher.InvokeAsync(() =>
        {
            try
            {
                if (!Clipboard.ContainsText())
                    return false;
                if (!string.Equals(Clipboard.GetText(), expectedValue, StringComparison.Ordinal))
                    return false;

                Clipboard.Clear();
                return true;
            }
            catch
            {
                // Clipboard ownership can change between checks. Best effort only.
                return false;
            }
        });
    }
}

public sealed class ProtectedClipboardService
{
    private readonly AppSettingsService _settingsService;
    private readonly IProtectedClipboardAdapter _clipboard;
    private readonly Func<TimeSpan, Task> _delayAsync;
    private static long _globalGeneration;

    public ProtectedClipboardService(AppSettingsService? settingsService = null)
        : this(
            settingsService ?? new AppSettingsService(),
            new WpfProtectedClipboardAdapter(),
            delay => Task.Delay(delay))
    {
    }

    internal ProtectedClipboardService(
        AppSettingsService settingsService,
        IProtectedClipboardAdapter clipboard,
        Func<TimeSpan, Task> delayAsync)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
        _delayAsync = delayAsync ?? throw new ArgumentNullException(nameof(delayAsync));
    }

    internal Task? LastScheduledClearTask { get; private set; }

    public void CopyText(string value, Action<string>? status = null)
    {
        if (string.IsNullOrEmpty(value))
            return;

        _clipboard.SetText(value);
        var generation = Interlocked.Increment(ref _globalGeneration);
        var seconds = _settingsService.Load().ClipboardAutoClearSeconds;
        if (seconds <= 0)
        {
            status?.Invoke("Copied to clipboard. Auto-clear is disabled in Settings.");
            LastScheduledClearTask = null;
            return;
        }

        status?.Invoke($"Copied to clipboard. Rice2k will clear this value in {seconds} seconds if it is still unchanged.");
        LastScheduledClearTask = ClearLaterAsync(value, seconds, generation, status);
    }

    public bool ClearNow()
    {
        Interlocked.Increment(ref _globalGeneration);
        return _clipboard.Clear();
    }

    private async Task ClearLaterAsync(string expectedValue, int seconds, long generation, Action<string>? status)
    {
        try
        {
            await _delayAsync(TimeSpan.FromSeconds(seconds));
            if (generation != Interlocked.Read(ref _globalGeneration))
                return;

            if (await _clipboard.ClearIfMatchesAsync(expectedValue))
                status?.Invoke("✓ Clipboard auto-cleared.");
        }
        catch
        {
            // Clipboard timers must never terminate the application.
        }
    }
}
