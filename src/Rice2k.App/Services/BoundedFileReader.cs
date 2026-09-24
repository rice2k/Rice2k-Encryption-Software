namespace Rice2k.Encryption.Services;

internal static class BoundedFileReader
{
    public static byte[] ReadAllBytes(string path, int maximumLength, string label)
    {
        ValidateArguments(path, maximumLength);
        if (!File.Exists(path))
            throw new FileNotFoundException($"The {label} could not be found.", path);

        using var input = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            4096,
            FileOptions.SequentialScan);

        var length = ValidateLength(input, maximumLength, label);
        var bytes = new byte[checked((int)length)];
        try
        {
            input.ReadExactly(bytes);
            EnsureLengthStable(input, length, label);
            return bytes;
        }
        catch (EndOfStreamException ex)
        {
            throw new InvalidDataException($"The {label} changed or was truncated while Rice2k was reading it.", ex);
        }
    }

    public static async Task<byte[]> ReadAllBytesAsync(
        string path,
        int maximumLength,
        string label,
        CancellationToken cancellationToken = default)
    {
        ValidateArguments(path, maximumLength);
        if (!File.Exists(path))
            throw new FileNotFoundException($"The {label} could not be found.", path);

        await using var input = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        var length = ValidateLength(input, maximumLength, label);
        var bytes = new byte[checked((int)length)];
        try
        {
            await input.ReadExactlyAsync(bytes.AsMemory(), cancellationToken);
            EnsureLengthStable(input, length, label);
            return bytes;
        }
        catch (EndOfStreamException ex)
        {
            throw new InvalidDataException($"The {label} changed or was truncated while Rice2k was reading it.", ex);
        }
    }

    private static void ValidateArguments(string path, int maximumLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (maximumLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumLength));
    }

    private static long ValidateLength(FileStream input, int maximumLength, string label)
    {
        var length = input.Length;
        if (length <= 0 || length > maximumLength)
            throw new InvalidDataException($"The {label} has an invalid size.");
        return length;
    }

    private static void EnsureLengthStable(FileStream input, long expectedLength, string label)
    {
        if (input.Length != expectedLength)
            throw new InvalidDataException($"The {label} changed while Rice2k was reading it.");
    }
}
