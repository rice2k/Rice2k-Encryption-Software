using System.Security.Cryptography;
using System.Text;
using Sodium;

namespace Rice2k.Encryption.Services;

public sealed class TextCryptoService
{
    private const string Prefix = "R2KTXT1";
    private const long OpsLimit = 4;
    private const int MemLimit = 64 * 1024 * 1024;
    private const int SaltSize = 16;
    private const int NonceSize = 24;
    private const int AuthenticationTagSize = 16;
    private const int MinimumEncryptionPasswordLength = 12;
    private const int MaximumTokenCharacters = 16 * 1024 * 1024;
    private const int SaltBase64Characters = 24;
    private const int NonceBase64Characters = 32;
    private const int FixedTokenCharacters = 7 + 3 + SaltBase64Characters + NonceBase64Characters;
    private const int MaximumCiphertextBase64Characters = ((MaximumTokenCharacters - FixedTokenCharacters) / 4) * 4;
    private const int MaximumCiphertextBytes = (MaximumCiphertextBase64Characters / 4) * 3;
    private const int MaximumPlaintextBytes = MaximumCiphertextBytes - AuthenticationTagSize;
    private static readonly byte[] Aad = Encoding.ASCII.GetBytes(Prefix);

    public string Encrypt(string plaintext, string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        if (password.Length < MinimumEncryptionPasswordLength)
            throw new ArgumentException($"Encryption passwords must contain at least {MinimumEncryptionPasswordLength} characters.", nameof(password));

        var text = plaintext ?? string.Empty;
        if (Encoding.UTF8.GetByteCount(text) > MaximumPlaintextBytes)
        {
            throw new ArgumentException(
                "The text is too large for the Rice2k encrypted-text format. Use file encryption for larger content.",
                nameof(plaintext));
        }

        var plainBytes = Encoding.UTF8.GetBytes(text);
        var salt = PasswordHash.ArgonGenerateSalt();
        var nonce = SecretAeadXChaCha20Poly1305.GenerateNonce();
        var passwordBytes = Encoding.UTF8.GetBytes(password);
        byte[]? key = null;
        byte[]? cipher = null;

        try
        {
            key = PasswordHash.ArgonHashBinary(
                passwordBytes,
                salt,
                OpsLimit,
                MemLimit,
                32,
                PasswordHash.ArgonAlgorithm.Argon_2ID13);

            cipher = SecretAeadXChaCha20Poly1305.Encrypt(plainBytes, nonce, key, Aad);
            var token = string.Join('.',
                Prefix,
                Convert.ToBase64String(salt),
                Convert.ToBase64String(nonce),
                Convert.ToBase64String(cipher));

            if (token.Length > MaximumTokenCharacters)
                throw new InvalidOperationException("Rice2k generated an encrypted text token outside its supported format limit.");

            return token;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plainBytes);
            CryptographicOperations.ZeroMemory(passwordBytes);
            if (key is not null)
                CryptographicOperations.ZeroMemory(key);
            if (cipher is not null)
                CryptographicOperations.ZeroMemory(cipher);
        }
    }

    public string Decrypt(string token, string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        if (token.Length > MaximumTokenCharacters)
            throw new FormatException("The encrypted text token is larger than this development build supports.");

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

        if (salt.Length != SaltSize)
            throw new FormatException("The encrypted text token contains an invalid salt.");
        if (nonce.Length != NonceSize)
            throw new FormatException("The encrypted text token contains an invalid nonce.");
        if (cipher.Length < AuthenticationTagSize)
            throw new FormatException("The encrypted text token does not contain valid authenticated ciphertext.");

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
            CryptographicOperations.ZeroMemory(cipher);
        }
    }
}
