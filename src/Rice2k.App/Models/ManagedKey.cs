using System.Security.Cryptography;

namespace Rice2k.Encryption.Models;

public sealed class ManagedKey : IDisposable
{
    private byte[]? _secretKey;

    internal ManagedKey(Guid id, string name, DateTimeOffset createdUtc, string source, byte[] secretKey)
    {
        if (secretKey.Length != 32)
            throw new ArgumentException("Rice2k symmetric keys must be exactly 256 bits.", nameof(secretKey));

        Id = id;
        Name = string.IsNullOrWhiteSpace(name) ? "Unnamed Key" : name.Trim();
        CreatedUtc = createdUtc;
        Source = source;
        _secretKey = secretKey.ToArray();
        Fingerprint = CreateFingerprint(_secretKey);
    }

    public Guid Id { get; }
    public string Name { get; }
    public string Fingerprint { get; }
    public DateTimeOffset CreatedUtc { get; }
    public string CreatedDisplay => CreatedUtc.ToLocalTime().ToString("g");
    public string Source { get; }
    public string KeyType => "256-bit symmetric";

    internal byte[] CopySecretKey()
    {
        if (_secretKey is null)
            throw new ObjectDisposedException(nameof(ManagedKey), "This key is no longer available in memory.");

        return _secretKey.ToArray();
    }

    public void Dispose()
    {
        if (_secretKey is null)
            return;

        CryptographicOperations.ZeroMemory(_secretKey);
        _secretKey = null;
        GC.SuppressFinalize(this);
    }

    private static string CreateFingerprint(byte[] key)
    {
        var hash = SHA256.HashData(key);
        try
        {
            var hex = Convert.ToHexString(hash.AsSpan(0, 10));
            return $"R2K-{hex[..4]}-{hex[4..8]}-{hex[8..12]}-{hex[12..16]}-{hex[16..20]}";
        }
        finally
        {
            CryptographicOperations.ZeroMemory(hash);
        }
    }
}
