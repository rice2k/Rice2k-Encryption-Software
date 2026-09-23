namespace Rice2k.Encryption.Models;

public sealed record VaultOperationProgress(
    string Stage,
    long BytesProcessed,
    long TotalBytes,
    int ItemsProcessed = 0,
    int TotalItems = 0,
    string? CurrentItem = null)
{
    public double Percentage => TotalBytes > 0
        ? Math.Clamp((double)BytesProcessed / TotalBytes * 100.0, 0, 100)
        : TotalItems > 0
            ? Math.Clamp((double)ItemsProcessed / TotalItems * 100.0, 0, 100)
            : 0;

    public bool IsDeterminate => TotalBytes > 0 || TotalItems > 0;
}
