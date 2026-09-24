namespace Rice2k.Encryption.Services;

internal static class ChunkReadHelper
{
    public static async ValueTask<int> ReadFullChunkAsync(
        Stream input,
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = await input.ReadAsync(buffer[totalRead..], cancellationToken);
            if (read == 0)
                break;

            totalRead += read;
        }

        return totalRead;
    }
}
