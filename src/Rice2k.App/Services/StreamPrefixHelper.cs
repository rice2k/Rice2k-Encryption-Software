namespace Rice2k.Encryption.Services;

internal static class StreamPrefixHelper
{
    public static bool StartsWith(Stream source, ReadOnlySpan<byte> expectedPrefix)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (expectedPrefix.Length == 0)
            return true;

        var buffer = new byte[expectedPrefix.Length];
        try
        {
            source.ReadExactly(buffer);
            return buffer.AsSpan().SequenceEqual(expectedPrefix);
        }
        catch (EndOfStreamException)
        {
            return false;
        }
    }
}
