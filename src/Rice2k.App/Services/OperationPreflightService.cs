namespace Rice2k.Encryption.Services;

public sealed record PreflightResult(
    long SourceBytes,
    string DestinationDirectory,
    long? AvailableBytes,
    string Summary);

public sealed class OperationPreflightService
{
    private const long SafetyMarginBytes = 32L * 1024 * 1024;

    public PreflightResult Validate(string sourcePath, string destinationPath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            throw new FileNotFoundException("Choose a source file first.", sourcePath);

        if (string.IsNullOrWhiteSpace(destinationPath))
            throw new ArgumentException("Choose where the output file should be saved.");

        var sourceFullPath = Path.GetFullPath(sourcePath);
        var destinationFullPath = Path.GetFullPath(destinationPath);

        if (string.Equals(sourceFullPath, destinationFullPath, StringComparison.OrdinalIgnoreCase))
            throw new IOException("The output cannot be the same file as the source. Choose a different destination.");

        var destinationDirectory = Path.GetDirectoryName(destinationFullPath);
        if (string.IsNullOrWhiteSpace(destinationDirectory) || !Directory.Exists(destinationDirectory))
            throw new DirectoryNotFoundException("The selected output folder does not exist. Choose another location.");

        if (File.Exists(destinationFullPath))
            throw new IOException("A file already exists at the selected output path. Rice2k will not overwrite it automatically. Choose Keep Both or another name.");

        var sourceInfo = new FileInfo(sourceFullPath);

        try
        {
            using var stream = new FileStream(sourceFullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            _ = stream.Length;
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new UnauthorizedAccessException("Rice2k cannot read the selected source file. Check the file permissions or choose another file.", ex);
        }
        catch (IOException ex)
        {
            throw new IOException("Rice2k cannot open the selected source file for reading. The file may be locked by another program.", ex);
        }

        ProbeDestinationWriteAccess(destinationDirectory);

        long? availableBytes = null;
        try
        {
            var root = Path.GetPathRoot(destinationFullPath);
            if (!string.IsNullOrWhiteSpace(root))
            {
                var drive = new DriveInfo(root);
                if (drive.IsReady)
                {
                    availableBytes = drive.AvailableFreeSpace;
                    var requiredBytes = sourceInfo.Length + SafetyMarginBytes;
                    if (availableBytes.Value < requiredBytes)
                    {
                        throw new IOException(
                            $"There is not enough free space in the selected destination. Rice2k needs at least {FormatBytes(requiredBytes)} available before starting.");
                    }
                }
            }
        }
        catch (IOException)
        {
            throw;
        }
        catch
        {
            // Some network or virtual destinations do not expose free-space information.
            // This is not a blocker; the write operation will still fail safely if storage runs out.
        }

        var freeSpaceText = availableBytes is { } bytes
            ? $"{FormatBytes(bytes)} free"
            : "free space unavailable";

        return new PreflightResult(
            sourceInfo.Length,
            destinationDirectory,
            availableBytes,
            $"Preflight passed — source readable, destination writable, output name available, {freeSpaceText}.");
    }

    private static void ProbeDestinationWriteAccess(string destinationDirectory)
    {
        var probePath = Path.Combine(
            destinationDirectory,
            $".rice2k-write-test-{Guid.NewGuid():N}.tmp");

        try
        {
            using (var stream = new FileStream(
                       probePath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 1,
                       FileOptions.WriteThrough))
            {
                stream.WriteByte(0x52);
                stream.Flush(flushToDisk: true);
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new UnauthorizedAccessException(
                "Rice2k cannot write to the selected output folder. Choose another folder or adjust its permissions before starting.",
                ex);
        }
        catch (IOException ex)
        {
            throw new IOException(
                "Rice2k could not create a temporary safety-check file in the selected output folder. The folder may be read-only, unavailable, or locked.",
                ex);
        }
        finally
        {
            try
            {
                if (File.Exists(probePath))
                    File.Delete(probePath);
            }
            catch
            {
                // The probe is intentionally tiny and uniquely named. Failure to remove it is
                // non-fatal to preflight but should be extremely uncommon.
            }
        }
    }

    private static string FormatBytes(long bytes)
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
}
