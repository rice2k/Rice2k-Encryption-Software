using System.Diagnostics;
using System.Security.Cryptography;
using Rice2k.Encryption.Models;
using Rice2k.Encryption.Services;

const string BenchmarkPassword = "Rice2k benchmark password 2026 only";

var fileSizeMb = ReadIntArgument(args, "--file-size-mb", 64, 1, 16 * 1024);
var fileCount = ReadIntArgument(args, "--files", 4, 1, 10_000);
var keep = args.Any(arg => string.Equals(arg, "--keep", StringComparison.OrdinalIgnoreCase));
var totalBytes = checked((long)fileSizeMb * 1024 * 1024 * fileCount);

var root = Path.Combine(Path.GetTempPath(), "Rice2k.VaultBench", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
var sourceDirectory = Path.Combine(root, "source");
var restoreDirectory = Path.Combine(root, "restored");
var vaultPath = Path.Combine(root, "benchmark.r2kvault");
Directory.CreateDirectory(sourceDirectory);
Directory.CreateDirectory(restoreDirectory);

Console.WriteLine("Rice2k Secure Vault benchmark");
Console.WriteLine($"Files: {fileCount:N0}");
Console.WriteLine($"Per file: {FormatBytes((long)fileSizeMb * 1024 * 1024)}");
Console.WriteLine($"Total plaintext: {FormatBytes(totalBytes)}");
Console.WriteLine($"Working directory: {root}");
Console.WriteLine();

try
{
    Console.WriteLine("Generating benchmark data...");
    var generation = Stopwatch.StartNew();
    var sourceFiles = new List<string>(fileCount);
    for (var index = 0; index < fileCount; index++)
    {
        var path = Path.Combine(sourceDirectory, $"file-{index + 1:0000}.bin");
        await WriteRandomFileAsync(path, (long)fileSizeMb * 1024 * 1024);
        sourceFiles.Add(path);
        Console.Write($"\rGenerated {index + 1:N0}/{fileCount:N0}");
    }
    generation.Stop();
    Console.WriteLine($"\nGeneration: {generation.Elapsed}");
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

    Console.WriteLine("Verifying vault...");
    var verifyProgress = new ConsoleVaultProgress();
    var verifyTimer = Stopwatch.StartNew();
    await service.VerifyWithProgressAsync(session, verifyProgress);
    verifyTimer.Stop();
    PrintRate("Full vault verification", totalBytes, verifyTimer.Elapsed);

    var firstEntry = session.Entries.OrderBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase).First();
    var sourceForExtract = sourceFiles[0];
    var restoredPath = Path.Combine(restoreDirectory, Path.GetFileName(sourceForExtract));

    Console.WriteLine("Extracting first file...");
    var extractProgress = new ConsoleVaultProgress();
    var extractTimer = Stopwatch.StartNew();
    await service.ExtractWithProgressAsync(session, firstEntry.Id, restoredPath, extractProgress);
    extractTimer.Stop();
    PrintRate("Single-file extract", firstEntry.Length, extractTimer.Elapsed);

    Console.WriteLine("Checking restored SHA-256...");
    var sourceHash = await HashFileAsync(sourceForExtract);
    var restoredHash = await HashFileAsync(restoredPath);
    if (!CryptographicOperations.FixedTimeEquals(sourceHash, restoredHash))
        throw new InvalidDataException("Benchmark correctness check failed: restored SHA-256 did not match the source file.");

    Console.WriteLine("Correctness: PASS");
    Console.WriteLine($"Vault size: {FormatBytes(new FileInfo(vaultPath).Length)}");
    Console.WriteLine($"Vault sequence: {session.Sequence:N0}");
    Console.WriteLine($"Entries: {session.Entries.Count:N0}");
    Console.WriteLine();
    Console.WriteLine("Benchmark completed successfully.");
}
finally
{
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
