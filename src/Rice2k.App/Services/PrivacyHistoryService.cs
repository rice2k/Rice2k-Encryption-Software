using System.Text.Json;

namespace Rice2k.Encryption.Services;

public sealed record RecentFileEntry(
    string Path,
    string Purpose,
    DateTimeOffset LastUsedUtc);

public sealed record RedactedActivityEntry(
    string Action,
    DateTimeOffset TimestampUtc);

public sealed class PrivacyHistoryService
{
    private const int MaxRecentFiles = 20;
    private const int MaxActivityEntries = 500;
    private const long MaxHistoryFileBytes = 2 * 1024 * 1024;

    private readonly string _directory;
    private readonly string _recentPath;
    private readonly string _activityPath;

    public PrivacyHistoryService(string? directory = null)
    {
        _directory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Rice2k Encryption Software");
        _recentPath = Path.Combine(_directory, "recent-files.json");
        _activityPath = Path.Combine(_directory, "activity-redacted.json");
    }

    public IReadOnlyList<RecentFileEntry> LoadRecentFiles() =>
        LoadBoundedList<RecentFileEntry>(_recentPath, MaxRecentFiles)
            .Where(IsValidRecentEntry)
            .OrderByDescending(entry => entry.LastUsedUtc)
            .Take(MaxRecentFiles)
            .ToArray();

    public IReadOnlyList<RedactedActivityEntry> LoadActivity() =>
        LoadBoundedList<RedactedActivityEntry>(_activityPath, MaxActivityEntries)
            .Where(IsValidActivityEntry)
            .OrderByDescending(entry => entry.TimestampUtc)
            .Take(MaxActivityEntries)
            .ToArray();

    public bool TryRememberRecentFile(string path, string purpose)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            return false;

        var normalizedPath = Path.GetFullPath(path);
        purpose = NormalizePurpose(purpose);

        try
        {
            var entries = LoadRecentFiles()
                .Where(entry => !string.Equals(entry.Path, normalizedPath, StringComparison.OrdinalIgnoreCase))
                .Prepend(new RecentFileEntry(normalizedPath, purpose, DateTimeOffset.UtcNow))
                .Take(MaxRecentFiles)
                .ToArray();

            return TryWriteList(_recentPath, entries);
        }
        catch
        {
            return false;
        }
    }

    public bool TryAppendRedactedActivity(string activityDisplayText)
    {
        var action = RedactActivityDisplayText(activityDisplayText);
        if (string.IsNullOrWhiteSpace(action))
            return false;

        try
        {
            var entries = LoadActivity()
                .Prepend(new RedactedActivityEntry(action, DateTimeOffset.UtcNow))
                .Take(MaxActivityEntries)
                .ToArray();
            return TryWriteList(_activityPath, entries);
        }
        catch
        {
            return false;
        }
    }

    public bool TryClearRecentFiles() => TryDelete(_recentPath);
    public bool TryClearActivity() => TryDelete(_activityPath);

    public bool TryClearAll()
    {
        var recent = TryClearRecentFiles();
        var activity = TryClearActivity();
        return recent && activity;
    }

    public static string RedactActivityDisplayText(string? displayText)
    {
        if (string.IsNullOrWhiteSpace(displayText))
            return string.Empty;

        var withoutDetail = displayText;
        var detailSeparator = withoutDetail.IndexOf("   —   ", StringComparison.Ordinal);
        if (detailSeparator >= 0)
            withoutDetail = withoutDetail[..detailSeparator];

        var fields = withoutDetail.Split("   ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var action = fields.Length == 0 ? withoutDetail.Trim() : fields[^1].Trim();

        if (action.Length > 160)
            action = action[..160];

        return action;
    }

    private IReadOnlyList<T> LoadBoundedList<T>(string path, int maximumEntries)
    {
        try
        {
            if (!File.Exists(path))
                return Array.Empty<T>();

            var info = new FileInfo(path);
            if (info.Length <= 0 || info.Length > MaxHistoryFileBytes)
                return Array.Empty<T>();

            using var stream = File.OpenRead(path);
            var entries = JsonSerializer.Deserialize<List<T>>(stream) ?? new List<T>();
            return entries.Take(maximumEntries).ToArray();
        }
        catch
        {
            // History is optional metadata and must never block Rice2k from operating.
            return Array.Empty<T>();
        }
    }

    private bool TryWriteList<T>(string path, IReadOnlyCollection<T> entries)
    {
        string? tempPath = null;
        try
        {
            Directory.CreateDirectory(_directory);
            tempPath = Path.Combine(_directory, $"history.{Guid.NewGuid():N}.tmp");
            using (var stream = File.Create(tempPath))
            {
                JsonSerializer.Serialize(stream, entries, new JsonSerializerOptions { WriteIndented = true });
                stream.Flush(flushToDisk: true);
            }
            File.Move(tempPath, path, overwrite: true);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(tempPath))
            {
                try
                {
                    if (File.Exists(tempPath))
                        File.Delete(tempPath);
                }
                catch
                {
                    // Best effort cleanup only.
                }
            }
        }
    }

    private static bool TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsValidRecentEntry(RecentFileEntry entry) =>
        !string.IsNullOrWhiteSpace(entry.Path) &&
        entry.Path.Length <= 4096 &&
        !string.IsNullOrWhiteSpace(entry.Purpose) &&
        entry.Purpose.Length <= 64;

    private static bool IsValidActivityEntry(RedactedActivityEntry entry) =>
        !string.IsNullOrWhiteSpace(entry.Action) &&
        entry.Action.Length <= 160;

    private static string NormalizePurpose(string purpose)
    {
        purpose = string.IsNullOrWhiteSpace(purpose) ? "Recent file" : purpose.Trim();
        return purpose.Length <= 64 ? purpose : purpose[..64];
    }
}
