using System.Security.Cryptography;

namespace Rice2k.Encryption.Models;

public sealed record VaultEntryInfo(
    Guid Id,
    string Path,
    long Length,
    DateTimeOffset ModifiedUtc)
{
    public string Name => System.IO.Path.GetFileName(Path.Replace('/', System.IO.Path.DirectorySeparatorChar));
    public string SizeDisplay => FormatBytes(Length);
    public string ModifiedDisplay => ModifiedUtc.ToLocalTime().ToString("g");

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

public sealed class SecureVaultSession : IDisposable
{
    private byte[]? _contentKey;
    private IReadOnlyList<VaultEntryInfo> _entries;

    internal SecureVaultSession(
        string vaultPath,
        Guid vaultId,
        DateTimeOffset createdUtc,
        DateTimeOffset updatedUtc,
        long sequence,
        byte[] contentKey,
        IReadOnlyList<VaultEntryInfo> entries)
    {
        VaultPath = vaultPath;
        VaultId = vaultId;
        CreatedUtc = createdUtc;
        UpdatedUtc = updatedUtc;
        Sequence = sequence;
        _contentKey = contentKey.ToArray();
        _entries = entries.ToArray();
    }

    public string VaultPath { get; }
    public Guid VaultId { get; }
    public DateTimeOffset CreatedUtc { get; }
    public DateTimeOffset UpdatedUtc { get; private set; }
    public long Sequence { get; private set; }
    public IReadOnlyList<VaultEntryInfo> Entries => _entries;
    public bool IsLocked => _contentKey is null;

    internal byte[] CopyContentKey()
    {
        if (_contentKey is null)
            throw new ObjectDisposedException(nameof(SecureVaultSession), "The vault is locked. Unlock it again before accessing protected data.");
        return _contentKey.ToArray();
    }

    internal void UpdateState(DateTimeOffset updatedUtc, long sequence, IReadOnlyList<VaultEntryInfo> entries)
    {
        if (_contentKey is null)
            throw new ObjectDisposedException(nameof(SecureVaultSession));
        UpdatedUtc = updatedUtc;
        Sequence = sequence;
        _entries = entries.ToArray();
    }

    public void Dispose()
    {
        if (_contentKey is null)
            return;

        CryptographicOperations.ZeroMemory(_contentKey);
        _contentKey = null;
        _entries = Array.Empty<VaultEntryInfo>();
        GC.SuppressFinalize(this);
    }
}
