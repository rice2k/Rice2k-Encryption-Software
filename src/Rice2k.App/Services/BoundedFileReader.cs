namespace Rice2k.Encryption.Services;

internal static class BoundedFileReader
{
    public static async Task<byte[]> ReadAllBytesAsync(
        string path,
        int maximumLength,
        string label,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (maximumLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumLength));
        if (!File.Exists(path))
            throw new FileNotFoundException($"The {label} could not be found.", path);

        await using var input = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        var length = input.Length;
        if (length <= 0 || length > maximumLength)
            throw new InvalidDataException($"The {label} has an invalid size.");

        var bytes = new byte[checked((int)length)];
        try
        {
            await input.ReadExactlyAsync(bytes.AsMemory(), cancellationToken);
            if (input.Length != length)
                throw new InvalidDataException($"The {label} changed while Rice2k was reading it.");
            return bytes;
        }
        catch (EndOfStreamException ex)
        {
            throw new InvalidDataException($"The {label} changed or was truncated while Rice2k was reading it.", ex);
        }
    }
}
