using System.Security.Cryptography;
using System.Text;

namespace Rice2k.Encryption.Models;

public sealed record Rice2kPublicIdentity(
    Guid Id,
    string Name,
    DateTimeOffset CreatedUtc,
    string Fingerprint,
    byte[] EncryptionPublicKey,
    byte[] SigningPublicKey)
{
    public string CreatedDisplay => CreatedUtc.ToLocalTime().ToString("g");
}

public sealed class Rice2kIdentity : IDisposable
{
    private const int MaximumNameCharacters = 200;
    private byte[]? _encryptionPrivateKey;
    private byte[]? _signingPrivateKey;

    internal Rice2kIdentity(
        Guid id,
        string name,
        DateTimeOffset createdUtc,
        byte[] encryptionPublicKey,
        byte[] encryptionPrivateKey,
        byte[] signingPublicKey,
        byte[] signingPrivateKey)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Rice2k identities require a non-empty identifier.", nameof(id));
        if (encryptionPublicKey.Length != 32 || encryptionPrivateKey.Length != 32)
            throw new ArgumentException("Rice2k encryption identities require a 32-byte public/private key pair.");
        if (signingPublicKey.Length != 32 || signingPrivateKey.Length != 64)
            throw new ArgumentException("Rice2k signing identities require a 32-byte public key and 64-byte private key.");

        var normalizedName = string.IsNullOrWhiteSpace(name) ? "Unnamed Identity" : name.Trim();
        if (normalizedName.Length > MaximumNameCharacters)
            throw new ArgumentException($"Rice2k identity names must be {MaximumNameCharacters} characters or fewer.", nameof(name));

        Id = id;
        Name = normalizedName;
        CreatedUtc = createdUtc;
        EncryptionPublicKey = encryptionPublicKey.ToArray();
        SigningPublicKey = signingPublicKey.ToArray();
        _encryptionPrivateKey = encryptionPrivateKey.ToArray();
        _signingPrivateKey = signingPrivateKey.ToArray();
        Fingerprint = CreateFingerprint(EncryptionPublicKey, SigningPublicKey);
    }

    public Guid Id { get; }
    public string Name { get; }
    public DateTimeOffset CreatedUtc { get; }
    public string CreatedDisplay => CreatedUtc.ToLocalTime().ToString("g");
    public string Fingerprint { get; }
    public byte[] EncryptionPublicKey { get; }
    public byte[] SigningPublicKey { get; }
    public bool IsDisposed => _encryptionPrivateKey is null || _signingPrivateKey is null;

    public Rice2kPublicIdentity ToPublicIdentity() => new(
        Id,
        Name,
        CreatedUtc,
        Fingerprint,
        EncryptionPublicKey.ToArray(),
        SigningPublicKey.ToArray());

    internal byte[] CopyEncryptionPrivateKey()
    {
        if (_encryptionPrivateKey is null)
            throw new ObjectDisposedException(nameof(Rice2kIdentity));
        return _encryptionPrivateKey.ToArray();
    }

    internal byte[] CopySigningPrivateKey()
    {
        if (_signingPrivateKey is null)
            throw new ObjectDisposedException(nameof(Rice2kIdentity));
        return _signingPrivateKey.ToArray();
    }

    public void Dispose()
    {
        if (_encryptionPrivateKey is not null)
        {
            CryptographicOperations.ZeroMemory(_encryptionPrivateKey);
            _encryptionPrivateKey = null;
        }

        if (_signingPrivateKey is not null)
        {
            CryptographicOperations.ZeroMemory(_signingPrivateKey);
            _signingPrivateKey = null;
        }

        CryptographicOperations.ZeroMemory(EncryptionPublicKey);
        CryptographicOperations.ZeroMemory(SigningPublicKey);
        GC.SuppressFinalize(this);
    }

    public static string CreateFingerprint(byte[] encryptionPublicKey, byte[] signingPublicKey)
    {
        if (encryptionPublicKey.Length != 32 || signingPublicKey.Length != 32)
            throw new ArgumentException("Identity fingerprints require 32-byte encryption and signing public keys.");

        var domain = Encoding.ASCII.GetBytes("RICE2K-IDENTITY-FINGERPRINT-V1");
        var input = new byte[domain.Length + encryptionPublicKey.Length + signingPublicKey.Length];
        try
        {
            domain.CopyTo(input, 0);
            encryptionPublicKey.CopyTo(input, domain.Length);
            signingPublicKey.CopyTo(input, domain.Length + encryptionPublicKey.Length);
            var hash = SHA256.HashData(input);
            try
            {
                var hex = Convert.ToHexString(hash.AsSpan(0, 16));
                return $"R2KI-{hex[..4]}-{hex[4..8]}-{hex[8..12]}-{hex[12..16]}-{hex[16..20]}-{hex[20..24]}-{hex[24..28]}-{hex[28..32]}";
            }
            finally
            {
                CryptographicOperations.ZeroMemory(hash);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(input);
        }
    }
}
