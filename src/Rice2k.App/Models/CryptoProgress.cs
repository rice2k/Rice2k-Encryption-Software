namespace Rice2k.Encryption.Models;

public sealed record CryptoProgress(
    long BytesProcessed,
    long TotalBytes,
    string Stage,
    TimeSpan Elapsed,
    double BytesPerSecond)
{
    public double Percentage => TotalBytes <= 0
        ? 0
        : Math.Clamp((double)BytesProcessed / TotalBytes * 100.0, 0, 100);

    public TimeSpan? EstimatedRemaining
    {
        get
        {
            if (BytesPerSecond <= 0 || TotalBytes <= BytesProcessed)
                return null;

            return TimeSpan.FromSeconds((TotalBytes - BytesProcessed) / BytesPerSecond);
        }
    }
}
