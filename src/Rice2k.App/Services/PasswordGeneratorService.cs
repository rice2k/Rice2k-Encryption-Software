using System.Security.Cryptography;

namespace Rice2k.Encryption.Services;

public sealed class PasswordGeneratorService
{
    private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Lower = "abcdefghijkmnopqrstuvwxyz";
    private const string Digits = "23456789";
    private const string Symbols = "!@#$%^&*-_=+?";
    private static readonly string All = Upper + Lower + Digits + Symbols;

    public string GeneratePassword(int length = 24)
    {
        if (length < 16 || length > 128)
            throw new ArgumentOutOfRangeException(nameof(length), "Password length must be between 16 and 128 characters.");

        var chars = new char[length];
        chars[0] = Pick(Upper);
        chars[1] = Pick(Lower);
        chars[2] = Pick(Digits);
        chars[3] = Pick(Symbols);

        for (var i = 4; i < chars.Length; i++)
            chars[i] = Pick(All);

        // Fisher-Yates shuffle using the operating system CSPRNG.
        for (var i = chars.Length - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }

        return new string(chars);
    }

    private static char Pick(string alphabet) =>
        alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
}
