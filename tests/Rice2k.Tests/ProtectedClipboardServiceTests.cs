using Rice2k.Encryption.Services;

namespace Rice2k.Tests;

public sealed class ProtectedClipboardServiceTests
{
    [Fact]
    public async Task UnchangedValue_AutoClearRemovesOnlyTheExpectedValue()
    {
        using var temp = new TempDirectory();
        var settings = CreateSettings(temp, clipboardSeconds: 15);
        var clipboard = new FakeClipboardAdapter();
        var service = new ProtectedClipboardService(settings, clipboard, _ => Task.CompletedTask);
        var statuses = new List<string>();

        service.CopyText("sensitive-value", statuses.Add);
        var scheduled = Assert.IsType<Task>(service.LastScheduledClearTask);
        await scheduled;

        Assert.Null(clipboard.CurrentText);
        Assert.Equal(1, clipboard.ConditionalClearAttempts);
        Assert.Equal(1, clipboard.SuccessfulConditionalClears);
        Assert.Contains(statuses, value => value.Contains("auto-cleared", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task OlderTimer_CannotClearNewerRice2kCopy()
    {
        using var temp = new TempDirectory();
        var settings = CreateSettings(temp, clipboardSeconds: 15);
        var clipboard = new FakeClipboardAdapter();
        var delays = new ControlledDelayQueue();
        var service = new ProtectedClipboardService(settings, clipboard, delays.DelayAsync);

        service.CopyText("first-secret");
        var firstTimer = Assert.IsType<Task>(service.LastScheduledClearTask);
        service.CopyText("second-secret");
        var secondTimer = Assert.IsType<Task>(service.LastScheduledClearTask);

        delays.Release(0);
        await firstTimer;

        Assert.Equal("second-secret", clipboard.CurrentText);
        Assert.Equal(0, clipboard.ConditionalClearAttempts);

        delays.Release(1);
        await secondTimer;

        Assert.Null(clipboard.CurrentText);
        Assert.Equal(1, clipboard.SuccessfulConditionalClears);
    }

    [Fact]
    public async Task ExternalClipboardChange_IsNeverClearedByRice2kTimer()
    {
        using var temp = new TempDirectory();
        var settings = CreateSettings(temp, clipboardSeconds: 15);
        var clipboard = new FakeClipboardAdapter();
        var delays = new ControlledDelayQueue();
        var service = new ProtectedClipboardService(settings, clipboard, delays.DelayAsync);

        service.CopyText("rice2k-secret");
        var timer = Assert.IsType<Task>(service.LastScheduledClearTask);
        clipboard.ReplaceExternally("user-copied-something-else");

        delays.Release(0);
        await timer;

        Assert.Equal("user-copied-something-else", clipboard.CurrentText);
        Assert.Equal(1, clipboard.ConditionalClearAttempts);
        Assert.Equal(0, clipboard.SuccessfulConditionalClears);
    }

    [Fact]
    public async Task ClearNow_InvalidatesOlderTimerBeforeLaterClipboardContentAppears()
    {
        using var temp = new TempDirectory();
        var settings = CreateSettings(temp, clipboardSeconds: 15);
        var clipboard = new FakeClipboardAdapter();
        var delays = new ControlledDelayQueue();
        var service = new ProtectedClipboardService(settings, clipboard, delays.DelayAsync);

        service.CopyText("secret-before-manual-clear");
        var timer = Assert.IsType<Task>(service.LastScheduledClearTask);

        Assert.True(service.ClearNow());
        clipboard.ReplaceExternally("clipboard-after-manual-clear");
        delays.Release(0);
        await timer;

        Assert.Equal("clipboard-after-manual-clear", clipboard.CurrentText);
        Assert.Equal(0, clipboard.ConditionalClearAttempts);
        Assert.Equal(1, clipboard.DirectClearCalls);
    }

    [Fact]
    public void AutoClearDisabled_DoesNotScheduleTimer()
    {
        using var temp = new TempDirectory();
        var settings = CreateSettings(temp, clipboardSeconds: 0);
        var clipboard = new FakeClipboardAdapter();
        var service = new ProtectedClipboardService(settings, clipboard, _ => Task.CompletedTask);
        string? status = null;

        service.CopyText("keep-on-clipboard", value => status = value);

        Assert.Equal("keep-on-clipboard", clipboard.CurrentText);
        Assert.Null(service.LastScheduledClearTask);
        Assert.Contains("disabled", status ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    private static AppSettingsService CreateSettings(TempDirectory temp, int clipboardSeconds)
    {
        var settings = new AppSettingsService(temp.DirectoryPath);
        Assert.True(settings.TrySave(new Rice2kAppSettings(ClipboardAutoClearSeconds: clipboardSeconds)));
        return settings;
    }

    private sealed class ControlledDelayQueue
    {
        private readonly List<TaskCompletionSource<bool>> _delays = [];

        public Task DelayAsync(TimeSpan _)
        {
            var source = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _delays.Add(source);
            return source.Task;
        }

        public void Release(int index)
        {
            Assert.True(index >= 0 && index < _delays.Count);
            _delays[index].TrySetResult(true);
        }
    }

    private sealed class FakeClipboardAdapter : IProtectedClipboardAdapter
    {
        public string? CurrentText { get; private set; }
        public int DirectClearCalls { get; private set; }
        public int ConditionalClearAttempts { get; private set; }
        public int SuccessfulConditionalClears { get; private set; }

        public void SetText(string value) => CurrentText = value;

        public bool Clear()
        {
            DirectClearCalls++;
            CurrentText = null;
            return true;
        }

        public Task<bool> ClearIfMatchesAsync(string expectedValue)
        {
            ConditionalClearAttempts++;
            if (!string.Equals(CurrentText, expectedValue, StringComparison.Ordinal))
                return Task.FromResult(false);

            CurrentText = null;
            SuccessfulConditionalClears++;
            return Task.FromResult(true);
        }

        public void ReplaceExternally(string value) => CurrentText = value;
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            DirectoryPath = Path.Combine(Path.GetTempPath(), "Rice2k.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DirectoryPath);
        }

        public string DirectoryPath { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(DirectoryPath))
                    Directory.Delete(DirectoryPath, recursive: true);
            }
            catch
            {
                // Test cleanup is best effort.
            }
        }
    }
}
