using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Rice2k.Encryption.Models;
using Rice2k.Encryption.Services;

const string BenchmarkPassword = "Rice2k benchmark password 2026 only";

var fileSizeMb = ReadIntArgument(args, "--file-size-mb", 64, 1, 16 * 1024);
var fileCount = ReadIntArgument(args, "--files", 4, 1, 10_000);
var keep = args.Any(arg => string.Equals(arg, "--keep", StringComparison.OrdinalIgnoreCase));
var perFileBytes = checked((long)fileSizeMb * 1024 * 1024);
var totalBytes = checked(perFileBytes * fileCount);
var runId = $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..8]}";

var root = Path.Combine(Path.GetTempPath(), "Rice2k.VaultBench", runId);
var sourceDirectory = Path.Combine(root, "source");
var restoreDirectory = Path.Combine(root, "restored");
var vaultPath = Path.Combine(root, "benchmark.r2kvault");
Directory.CreateDirectory(sourceDirectory);
Directory.CreateDirectory(restoreDirectory);
EnsureDiskSpace(root, totalBytes);

var assembly = typeof(SecureVaultService).Assembly;
var informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
    ?? assembly.GetName().Version?.ToString()
    ?? "unknown";
var startedUtc = DateTimeOffset.UtcNow;

Console.WriteLine("Rice2k Secure Vault benchmark");
Console.WriteLine($"Rice2k version: {informationalVersion}");
Console.WriteLine($"Started UTC: {startedUtc:O}");
Console.WriteLine($"OS: {RuntimeInformation.OSDescription}");
Console.WriteLine($".NET: {RuntimeInformation.FrameworkDescription}");
Console.WriteLine($"Process architecture: {RuntimeInformation.ProcessArchitecture}");
Console.WriteLine($"Processor count: {Environment.ProcessorCount}");
Console.WriteLine($"Files: {fileCount:N0}");
Console.WriteLine($"Per file: {FormatBytes(perFileBytes)}");
Console.WriteLine($"Total plaintext: {FormatBytes(totalBytes)}");
Console.WriteLine($"Working directory: {root}");
Console.WriteLine();

await using var memorySampler = new MemorySampler();
var sourceHashes = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
byte[]? restoredHash = null;

try
{
    Console.WriteLine("Generating benchmark data...");
    var generation = Stopwatch.StartNew();
    var sourceFiles = new List<string>(fileCount);
    for (var index = 0; index < fileCount; index++)
    {
        var path = Path.Combine(sourceDirectory, $"file-{index + 1:0000}.bin");
        await WriteRandomFileAsync(path, perFileBytes);
        sourceFiles.Add(path);
        Console.Write($"\rGenerated {index + 1:N0}/{fileCount:N0}");
    }
    generation.Stop();
    Console.WriteLine($"\nGeneration: {generation.Elapsed}");
    Console.WriteLine();

    Console.WriteLine("Hashing source set before vault operations...");
    var baselineHashTimer = Stopwatch.StartNew();
    foreach (var path in sourceFiles)
        sourceHashes.Add(path, await HashFileAsync(path));
    baselineHashTimer.Stop();
    PrintRate("Source-set SHA-256 baseline", totalBytes, baselineHashTimer.Elapsed);
    Console.WriteLine();

    var service = new SecureVaultService();

    var createTimer = Stopwatch.StartNew();
    using var session = await service.CreateAsync(vaultPath, BenchmarkPassword);
    createTimer.Stop();
    Console.WriteLine($"Create empty vault: {createTimer.Elapsed}");

    var planned = sourceFiles
        .Select(path => (SourcePath: path, VaultPath: $"Benchmark/{Path.GetFileName(path)}"))
        .ToArray();

    Console.WriteLine("Adding files to vault...");
    var addProgress = new ConsoleVaultProgress();
    var addTimer = Stopwatch.StartNew();
    await service.AddFilesWithProgressAsync(session, planned, addProgress);
    addTimer.Stop();
    PrintRate("Add + pending/final verification", totalBytes, addTimer.Elapsed);

    if (session.Entries.Count != fileCount)
        throw new InvalidDataException($"Vault manifest entry-count mismatch: expected {fileCount:N0}, found {session.Entries.Count:N0}.");
    if (session.Entries.Sum(entry => entry.Length) != totalBytes)
        throw new InvalidDataException("Vault manifest total plaintext length does not match the generated source set.");

    Console.WriteLine("Verifying vault...");
    var verifyProgress = new ConsoleVaultProgress();
    var verifyTimer = Stopwatch.StartNew();
    await service.VerifyWithProgressAsync(session, verifyProgress);
    verifyTimer.Stop();
    PrintRate("Full vault verification", totalBytes, verifyTimer.Elapsed);

    var firstEntry = session.Entries.OrderBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase).First();
    var firstFileName = firstEntry.Path.Split('/').Last();
    var sourceForExtract = sourceFiles.Single(path =>
        string.Equals(Path.GetFileName(path), firstFileName, StringComparison.OrdinalIgnoreCase));
    var restoredPath = Path.Combine(restoreDirectory, firstFileName);

    Console.WriteLine("Extracting first file...");
    var extractProgress = new ConsoleVaultProgress();
    var extractTimer = Stopwatch.StartNew();
    await service.ExtractWithProgressAsync(session, firstEntry.Id, restoredPath, extractProgress);
    extractTimer.Stop();
    PrintRate("Single-file extract", firstEntry.Length, extractTimer.Elapsed);

    Console.WriteLine("Checking restored SHA-256...");
    restoredHash = await HashFileAsync(restoredPath);
    if (!CryptographicOperations.FixedTimeEquals(sourceHashes[sourceForExtract], restoredHash))
        throw new InvalidDataException("Benchmark correctness check failed: restored SHA-256 did not match the original source baseline.");

    Console.WriteLine("Re-hashing source set to prove vault operations did not modify original files...");
    var preservationTimer = Stopwatch.StartNew();
    foreach (var path in sourceFiles)
    {
        var currentHash = await HashFileAsync(path);
        try
        {
            if (new FileInfo(path).Length != perFileBytes)
                throw new InvalidDataException($"Source preservation check failed: '{Path.GetFileName(path)}' changed length.");
            if (!CryptographicOperations.FixedTimeEquals(sourceHashes[path], currentHash))
                throw new InvalidDataException($"Source preservation check failed: '{Path.GetFileName(path)}' changed SHA-256.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(currentHash);
        }
    }
    preservationTimer.Stop();
    PrintRate("Final source-set SHA-256", totalBytes, preservationTimer.Elapsed);

    var vaultLength = new FileInfo(vaultPath).Length;
    if (vaultLength <= 0)
        throw new InvalidDataException("Vault benchmark completed without a non-empty vault container.");

    using var process = Process.GetCurrentProcess();
    process.Refresh();

    Console.WriteLine("Source preservation: PASS");
    Console.WriteLine("Correctness: PASS");
    Console.WriteLine($"Vault size: {FormatBytes(vaultLength)}");
    Console.WriteLine($"Vault sequence: {session.Sequence:N0}");
    Console.WriteLine($"Entries: {session.Entries.Count:N0}");
    Console.WriteLine($"Peak managed memory observed: {FormatBytes(memorySampler.MaxManagedBytes)}");
    Console.WriteLine($"Process peak working set: {FormatBytes(process.PeakWorkingSet64)}");
    Console.WriteLine($"Completed UTC: {DateTimeOffset.UtcNow:O}");
    Console.WriteLine();
    Console.WriteLine("Benchmark completed successfully.");
}
finally
{
    foreach (var hash in sourceHashes.Values)
        CryptographicOperations.ZeroMemory(hash);
    if (restoredHash is not null)
        CryptographicOperations.ZeroMemory(restoredHash);

    if (keep)
    {
        Console.WriteLine($"Keeping benchmark files at: {root}");
    }
    else
    {
        try
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Cleanup warning: {ex.Message}");
            Console.WriteLine($"Benchmark files may remain at: {root}");
        }
    }
}

static int ReadIntArgument(string[] arguments, string name, int defaultValue, int minimum, int maximum)
{
    var index = Array.FindIndex(arguments, arg => string.Equals(arg, name, StringComparison.OrdinalIgnoreCase));
    if (index < 0)
        return defaultValue;
    if (index + 1 >= arguments.Length || !int.TryParse(arguments[index + 1], out var value))
        throw new ArgumentException($"{name} requires an integer value.");
    if (value < minimum || value > maximum)
        throw new ArgumentOutOfRangeException(name, $"{name} must be between {minimum:N0} and {maximum:N0}.");
    return value;
}

static void EnsureDiskSpace(string workingDirectory, long plaintextBytes)
{
    var root = Path.GetPathRoot(Path.GetFullPath(workingDirectory));
    if (string.IsNullOrWhiteSpace(root))
        return;

    try
    {
        var drive = new DriveInfo(root);
        var required = checked((plaintextBytes * 3) + (1024L * 1024 * 1024));
        if (drive.AvailableFreeSpace < required)
        {
            throw new IOException(
                $"Vault benchmark needs about {FormatBytes(required)} free on {root}, but only {FormatBytes(drive.AvailableFreeSpace)} is available.");
        }
    }
    catch (ArgumentException)
    {
        // Some unusual/mounted paths do not map cleanly to DriveInfo. The normal
        // filesystem operations will still fail safely if the volume fills.
    }
}

static async Task WriteRandomFileAsync(string path, long length)
{
    const int blockSize = 1024 * 1024;
    var buffer = new byte[blockSize];
    try
    {
        await using var output = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            blockSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        long written = 0;
        while (written < length)
        {
            var count = (int)Math.Min(buffer.Length, length - written);
            RandomNumberGenerator.Fill(buffer.AsSpan(0, count));
            await output.WriteAsync(buffer.AsMemory(0, count));
            written += count;
        }
        await output.FlushAsync();
        output.Flush(flushToDisk: true);
    }
    finally
    {
        CryptographicOperations.ZeroMemory(buffer);
    }
}

static async Task<byte[]> HashFileAsync(string path)
{
    await using var input = new FileStream(
        path,
        FileMode.Open,
        FileAccess.Read,
        FileShare.Read,
        1024 * 1024,
        FileOptions.Asynchronous | FileOptions.SequentialScan);
    using var sha = SHA256.Create();
    return await sha.ComputeHashAsync(input);
}

static void PrintRate(string label, long bytes, TimeSpan elapsed)
{
    var seconds = Math.Max(elapsed.TotalSeconds, 0.001);
    var rate = bytes / seconds;
    Console.WriteLine($"{label}: {elapsed} • {FormatBytes((long)rate)}/s");
}

static string FormatBytes(long bytes)
{
    string[] units = ["B", "KB", "MB", "GB", "TB"];
    double value = Math.Max(0, bytes);
    var unit = 0;
    while (value >= 1024 && unit < units.Length - 1)
    {
        value /= 1024;
        unit++;
    }
    return $"{value:0.##} {units[unit]}";
}

sealed class ConsoleVaultProgress : IProgress<VaultOperationProgress>
{
    private string? _stage;
    private int _bucket = -1;

    public void Report(VaultOperationProgress value)
    {
        if (!string.Equals(_stage, value.Stage, StringComparison.Ordinal))
        {
            if (_stage is not null)
                Console.WriteLine();
            _stage = value.Stage;
            _bucket = -1;
            Console.WriteLine(value.Stage);
        }

        if (!value.IsDeterminate)
        {
            if (!string.IsNullOrWhiteSpace(value.CurrentItem))
                Console.Write($"\r  {value.CurrentItem}                              ");
            return;
        }

        var bucket = (int)(value.Percentage / 10);
        if (bucket == _bucket && value.Percentage < 100)
            return;
        _bucket = bucket;

        var item = string.IsNullOrWhiteSpace(value.CurrentItem) ? string.Empty : $" • {value.CurrentItem}";
        Console.Write($"\r  {value.Percentage,5:0}%{item}                              ");
        if (value.Percentage >= 100)
            Console.WriteLine();
    }
}

sealed class MemorySampler : IAsyncDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _samplingTask;
    private long _maxManagedBytes;

    public MemorySampler()
    {
        _maxManagedBytes = GC.GetTotalMemory(forceFullCollection: false);
        _samplingTask = SampleAsync(_cts.Token);
    }

    public long MaxManagedBytes => Interlocked.Read(ref _maxManagedBytes);

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try
        {
            await _samplingTask;
        }
        catch (OperationCanceledException)
        {
        }
        _cts.Dispose();
    }

    private async Task SampleAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var current = GC.GetTotalMemory(forceFullCollection: false);
            long observed;
            do
            {
                observed = Interlocked.Read(ref _maxManagedBytes);
                if (current <= observed)
                    break;
            }
            while (Interlocked.CompareExchange(ref _maxManagedBytes, current, observed) != observed);

            await Task.Delay(100, cancellationToken);
        }
    }
}
