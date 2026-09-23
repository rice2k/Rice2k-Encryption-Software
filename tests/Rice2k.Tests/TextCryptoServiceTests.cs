using System.Security.Cryptography;
using Rice2k.Encryption.Services;

namespace Rice2k.Tests;

public sealed class TextCryptoServiceTests
{
    private const string Password = "correct horse battery staple 2026";
    private const int MaximumSupportedPlaintextBytes = 12_582_845;
    private readonly TextCryptoService _service = new();

    [Fact]
    public void EncryptDecrypt_RoundTrip_RestoresOriginalText()
    {
        const string plaintext = "Rice2k test text 🔐\nSecond line with Unicode: café 日本語";

        var token = _service.Encrypt(plaintext, Password);
        var restored = _service.Decrypt(token, Password);

        Assert.Equal(plaintext, restored);
        Assert.StartsWith("R2KTXT1.", token, StringComparison.Ordinal);
        Assert.NotEqual(plaintext, token);
    }

    [Fact]
    public void Encrypt_SamePlaintextAndPassword_ProducesDifferentTokens()
    {
        const string plaintext = "The same input should receive fresh salt and nonce values.";

        var first = _service.Encrypt(plaintext, Password);
        var second = _service.Encrypt(plaintext, Password);

        Assert.NotEqual(first, second);
        Assert.Equal(plaintext, _service.Decrypt(first, Password));
        Assert.Equal(plaintext, _service.Decrypt(second, Password));
    }

    [Fact]
    public void Encrypt_OversizedPlaintext_IsRejectedBeforeEncryption()
    {
        var oversized = new string('A', MaximumSupportedPlaintextBytes + 1);

        var error = Assert.Throws<ArgumentException>(() =>
            _service.Encrypt(oversized, Password));

        Assert.Contains("too large", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Decrypt_WrongPassword_FailsClosed()
    {
        var token = _service.Encrypt("private data", Password);

        Assert.Throws<CryptographicException>(() =>
            _service.Decrypt(token, "this is definitely the wrong password"));
    }

    [Fact]
    public void Decrypt_ModifiedCiphertext_FailsAuthentication()
    {
        var token = _service.Encrypt("authenticated text", Password);
        var parts = token.Split('.', 4);
        var cipher = Convert.FromBase64String(parts[3]);
        cipher[^1] ^= 0x01;
        parts[3] = Convert.ToBase64String(cipher);
        var modified = string.Join('.', parts);

        Assert.Throws<CryptographicException>(() =>
            _service.Decrypt(modified, Password));
    }

    [Fact]
    public void Decrypt_InvalidSaltLength_IsRejectedBeforeKdf()
    {
        var token = _service.Encrypt("hello", Password);
        var parts = token.Split('.', 4);
        parts[1] = Convert.ToBase64String(new byte[8]);
        var malformed = string.Join('.', parts);

        var error = Assert.Throws<FormatException>(() =>
            _service.Decrypt(malformed, Password));

        Assert.Contains("salt", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Encrypt_ShortPassword_IsRejected()
    {
        var error = Assert.Throws<ArgumentException>(() =>
            _service.Encrypt("data", "short"));

        Assert.Contains("12", error.Message, StringComparison.Ordinal);
    }
}
