using System.Windows;

namespace Rice2k.Encryption.Services;

public sealed class ProtectedClipboardService
{
    private readonly AppSettingsService _settingsService;
    private static long _globalGeneration;

    public ProtectedClipboardService(AppSettingsService? settingsService = null)
    {
        _settingsService = settingsService ?? new AppSettingsService();
    }

    public void CopyText(string value, Action<string>? status = null)
    {
        if (string.IsNullOrEmpty(value))
            return;

        Clipboard.SetText(value);
        var generation = Interlocked.Increment(ref _globalGeneration);
        var seconds = _settingsService.Load().ClipboardAutoClearSeconds;
        if (seconds <= 0)
        {
            status?.Invoke("Copied to clipboard. Auto-clear is disabled in Settings.");
            return;
        }

        status?.Invoke($"Copied to clipboard. Rice2k will clear this value in {seconds} seconds if it is still unchanged.");
        _ = ClearLaterAsync(value, seconds, generation, status);
    }

    public bool ClearNow()
    {
        Interlocked.Increment(ref _globalGeneration);
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

    private static async Task ClearLaterAsync(string expectedValue, int seconds, long generation, Action<string>? status)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(seconds));
            if (generation != Interlocked.Read(ref _globalGeneration))
                return;

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null)
                return;

            await dispatcher.InvokeAsync(() =>
            {
                try
                {
                    if (!Clipboard.ContainsText())
                        return;
                    if (!string.Equals(Clipboard.GetText(), expectedValue, StringComparison.Ordinal))
                        return;
                    Clipboard.Clear();
                    status?.Invoke("✓ Clipboard auto-cleared.");
                }
                catch
                {
                    // Clipboard ownership can change between checks. Best effort only.
                }
            });
        }
        catch
        {
            // Clipboard timers must never terminate the application.
        }
    }
}
