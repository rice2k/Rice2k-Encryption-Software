using System.Security.Cryptography;
using System.Text;
using Sodium;

namespace Rice2k.Encryption.Services;

public sealed class TextCryptoService
{
    private const string Prefix = "R2KTXT1";
    private const long OpsLimit = 4;
    private const int MemLimit = 64 * 1024 * 1024;
    private static readonly byte[] Aad = Encoding.ASCII.GetBytes(Prefix);

    public string Encrypt(string plaintext, string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        var salt = PasswordHash.ArgonGenerateSalt();
        var nonce = SecretAeadXChaCha20Poly1305.GenerateNonce();
        var passwordBytes = Encoding.UTF8.GetBytes(password);
        byte[]? key = null;

        try
        {
            key = PasswordHash.ArgonHashBinary(
                passwordBytes,
                salt,
                OpsLimit,
                MemLimit,
                32,
                PasswordHash.ArgonAlgorithm.Argon_2ID13);

            var plainBytes = Encoding.UTF8.GetBytes(plaintext ?? string.Empty);
            try
            {
                var cipher = SecretAeadXChaCha20Poly1305.Encrypt(plainBytes, nonce, key, Aad);
                return string.Join('.',
                    Prefix,
                    Convert.ToBase64String(salt),
                    Convert.ToBase64String(nonce),
                    Convert.ToBase64String(cipher));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plainBytes);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
            if (key is not null)
                CryptographicOperations.ZeroMemory(key);
        }
    }

    public string Decrypt(string token, string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        var parts = token.Trim().Split('.', 4);
        if (parts.Length != 4 || !string.Equals(parts[0], Prefix, StringComparison.Ordinal))
            throw new FormatException("This is not a supported Rice2k encrypted text token.");

        byte[] salt;
        byte[] nonce;
        byte[] cipher;

        try
        {
            salt = Convert.FromBase64String(parts[1]);
            nonce = Convert.FromBase64String(parts[2]);
            cipher = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException ex)
        {
            throw new FormatException("The encrypted text token is damaged or incorrectly formatted.", ex);
        }

        var passwordBytes = Encoding.UTF8.GetBytes(password);
        byte[]? key = null;

        try
        {
            key = PasswordHash.ArgonHashBinary(
                passwordBytes,
                salt,
                OpsLimit,
                MemLimit,
                32,
                PasswordHash.ArgonAlgorithm.Argon_2ID13);

            var plain = SecretAeadXChaCha20Poly1305.Decrypt(cipher, nonce, key, Aad);
            try
            {
                return Encoding.UTF8.GetString(plain);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plain);
            }
        }
        catch (CryptographicException ex)
        {
            throw new CryptographicException("Decryption failed. The password may be wrong, or the encrypted text may have been modified.", ex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
            if (key is not null)
                CryptographicOperations.ZeroMemory(key);
        }
    }
}
