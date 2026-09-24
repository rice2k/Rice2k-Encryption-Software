using System.Diagnostics;
using System.Security.Cryptography;
using Rice2k.Encryption.Models;
using Rice2k.Encryption.Services;

const string BenchmarkPassword = "Rice2k large-file benchmark password 2026";

var fileSizeMb = ReadIntArgument(args, "--file-size-mb", 256, 1, 32 * 1024);
var keep = args.Any(arg => string.Equals(arg, "--keep", StringComparison.OrdinalIgnoreCase));
var fileBytes = checked((long)fileSizeMb * 1024 * 1024);

var root = Path.Combine(Path.GetTempPath(), "Rice2k.FileBench", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
var sourcePath = Path.Combine(root, "source.bin");
var encryptedPath = Path.Combine(root, "source.bin.r2kenc");
var restoredPath = Path.Combine(root, "restored.bin");
Directory.CreateDirectory(root);
EnsureDiskSpace(root, fileBytes);

Console.WriteLine("Rice2k large-file streaming validation");
Console.WriteLine($"Plaintext size: {FormatBytes(fileBytes)}");
Console.WriteLine($"Working directory: {root}");
Console.WriteLine("This harness streams data and hashes; it does not intentionally load the whole test file into memory.");
Console.WriteLine();

await using var memorySampler = new MemorySampler();
byte[]? sourceHash = null;
byte[]? restoredHash = null;

try
{
    Console.WriteLine("Generating random source data...");
    var generationTimer = Stopwatch.StartNew();
    await WriteRandomFileAsync(sourcePath, fileBytes);
    generationTimer.Stop();
    PrintRate("Source generation", fileBytes, generationTimer.Elapsed);

    Console.WriteLine("Hashing source...");
    var sourceHashTimer = Stopwatch.StartNew();
    sourceHash = await HashFileAsync(sourcePath);
    sourceHashTimer.Stop();
    PrintRate("Source SHA-256", fileBytes, sourceHashTimer.Elapsed);
    Console.WriteLine($"Source SHA-256: {Convert.ToHexString(sourceHash)}");
    Console.WriteLine();

    var service = new FileEncryptionService();
    var progress = new ConsoleCryptoProgress();

    Console.WriteLine("Encrypting without inline verification so encryption throughput is measured separately...");
    var encryptTimer = Stopwatch.StartNew();
    await service.EncryptFileAsync(
        sourcePath,
        encryptedPath,
        BenchmarkPassword,
        progress,
        verifyAfterEncrypt: false);
    encryptTimer.Stop();
    PrintRate("Encryption", fileBytes, encryptTimer.Elapsed);
    Console.WriteLine($"Encrypted container: {FormatBytes(new FileInfo(encryptedPath).Length)}");
    Console.WriteLine();

    Console.WriteLine("Authenticating the full encrypted container...");
    var verifyTimer = Stopwatch.StartNew();
    await service.VerifyEncryptedFileAsync(encryptedPath, BenchmarkPassword);
    verifyTimer.Stop();
    PrintRate("Full encrypted-file verification", fileBytes, verifyTimer.Elapsed);
    Console.WriteLine();

    Console.WriteLine("Decrypting...");
    progress.Reset();
    var decryptTimer = Stopwatch.StartNew();
    await service.DecryptFileAsync(
        encryptedPath,
        restoredPath,
        BenchmarkPassword,
        progress);
    decryptTimer.Stop();
    PrintRate("Decryption", fileBytes, decryptTimer.Elapsed);
    Console.WriteLine();

    Console.WriteLine("Hashing restored data...");
    var restoredHashTimer = Stopwatch.StartNew();
    restoredHash = await HashFileAsync(restoredPath);
    restoredHashTimer.Stop();
    PrintRate("Restored SHA-256", fileBytes, restoredHashTimer.Elapsed);

    if (new FileInfo(sourcePath).Length != fileBytes)
        throw new InvalidDataException("Source length changed during the benchmark.");
    if (new FileInfo(restoredPath).Length != fileBytes)
        throw new InvalidDataException("Restored length does not match the original source length.");
    if (!CryptographicOperations.FixedTimeEquals(sourceHash, restoredHash))
        throw new InvalidDataException("Large-file correctness check failed: restored SHA-256 did not match the source file.");

    using var process = Process.GetCurrentProcess();
    process.Refresh();

    Console.WriteLine($"Restored SHA-256: {Convert.ToHexString(restoredHash)}");
    Console.WriteLine("Correctness: PASS");
    Console.WriteLine($"Peak managed memory observed: {FormatBytes(memorySampler.MaxManagedBytes)}");
    Console.WriteLine($"Process peak working set: {FormatBytes(process.PeakWorkingSet64)}");
    Console.WriteLine();
    Console.WriteLine("Large-file streaming validation completed successfully.");
}
finally
{
    if (sourceHash is not null)
        CryptographicOperations.ZeroMemory(sourceHash);
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
        var required = checked((plaintextBytes * 3) + (512L * 1024 * 1024));
        if (drive.AvailableFreeSpace < required)
        {
            throw new IOException(
                $"Large-file validation needs about {FormatBytes(required)} free on {root}, but only {FormatBytes(drive.AvailableFreeSpace)} is available.");
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

            if (written == length || written % (256L * 1024 * 1024) == 0)
                Console.Write($"\r  Generated {FormatBytes(written)} / {FormatBytes(length)}            ");
        }

        await output.FlushAsync();
        output.Flush(flushToDisk: true);
        Console.WriteLine();
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

sealed class ConsoleCryptoProgress : IProgress<CryptoProgress>
{
    private string? _stage;
    private int _bucket = -1;

    public void Reset()
    {
        _stage = null;
        _bucket = -1;
    }

    public void Report(CryptoProgress value)
    {
        if (!string.Equals(_stage, value.Stage, StringComparison.Ordinal))
        {
            if (_stage is not null)
                Console.WriteLine();
            _stage = value.Stage;
            _bucket = -1;
            Console.WriteLine(value.Stage);
        }

        var bucket = (int)(value.Percentage / 10);
        if (bucket == _bucket && value.Percentage < 100)
            return;
        _bucket = bucket;

        Console.Write($"\r  {value.Percentage,5:0}% • {FormatBytes(value.BytesProcessed)} / {FormatBytes(value.TotalBytes)} • {FormatBytes((long)value.BytesPerSecond)}/s          ");
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
