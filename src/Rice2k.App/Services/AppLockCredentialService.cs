using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Sodium;

namespace Rice2k.Encryption.Services;

public sealed class AppLockCredentialService
{
    private const string Format = "R2KAPPLOCK1";
    private const int Version = 1;
    private const long OpsLimit = 4;
    private const int MemLimit = 64 * 1024 * 1024;
    private const long MaximumSupportedOpsLimit = 10;
    private const int MaximumSupportedMemLimit = 256 * 1024 * 1024;
    private const int MinimumPasswordLength = 12;
    private const int VerifierLength = 32;
    private static readonly byte[] VerifierDomain = Encoding.ASCII.GetBytes("RICE2K-APP-LOCK-VERIFIER-V1");

    private readonly string _credentialPath;

    private sealed record StoredCredential(
        string Format,
        int Version,
        long OpsLimit,
        int MemLimit,
        string SaltBase64,
        string VerifierBase64,
        DateTimeOffset UpdatedUtc);

    public AppLockCredentialService(string? credentialPath = null)
    {
        _credentialPath = credentialPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Rice2k Encryption Software",
            "app-lock.json");
    }

    public bool IsConfigured()
    {
        try
        {
            _ = LoadValidated();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void SetPassword(string password)
    {
        ValidateNewPassword(password);
        var passwordBytes = Encoding.UTF8.GetBytes(password);
        byte[]? derived = null;
        byte[]? verifier = null;
        var salt = PasswordHash.ArgonGenerateSalt();

        try
        {
            derived = PasswordHash.ArgonHashBinary(
                passwordBytes,
                salt,
                OpsLimit,
                MemLimit,
                32,
                PasswordHash.ArgonAlgorithm.Argon_2ID13);
            verifier = HMACSHA256.HashData(derived, VerifierDomain);

            var stored = new StoredCredential(
                Format,
                Version,
                OpsLimit,
                MemLimit,
                Convert.ToBase64String(salt),
                Convert.ToBase64String(verifier),
                DateTimeOffset.UtcNow);

            var directory = Path.GetDirectoryName(_credentialPath)
                ?? throw new DirectoryNotFoundException("Rice2k could not determine the app-lock settings folder.");
            Directory.CreateDirectory(directory);
            var tempPath = _credentialPath + $".{Guid.NewGuid():N}.tmp";
            try
            {
                File.WriteAllText(
                    tempPath,
                    JsonSerializer.Serialize(stored, new JsonSerializerOptions { WriteIndented = true }));
                File.Move(tempPath, _credentialPath, overwrite: true);
            }
            finally
            {
                TryDelete(tempPath);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
            CryptographicOperations.ZeroMemory(salt);
            if (derived is not null)
                CryptographicOperations.ZeroMemory(derived);
            if (verifier is not null)
                CryptographicOperations.ZeroMemory(verifier);
        }
    }

    public bool Verify(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        var stored = LoadValidated();
        var passwordBytes = Encoding.UTF8.GetBytes(password);
        var salt = DecodeExact(stored.SaltBase64, 16, "salt");
        var expected = DecodeExact(stored.VerifierBase64, VerifierLength, "verifier");
        byte[]? derived = null;
        byte[]? actual = null;

        try
        {
            derived = PasswordHash.ArgonHashBinary(
                passwordBytes,
                salt,
                stored.OpsLimit,
                stored.MemLimit,
                32,
                PasswordHash.ArgonAlgorithm.Argon_2ID13);
            actual = HMACSHA256.HashData(derived, VerifierDomain);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
            CryptographicOperations.ZeroMemory(salt);
            CryptographicOperations.ZeroMemory(expected);
            if (derived is not null)
                CryptographicOperations.ZeroMemory(derived);
            if (actual is not null)
                CryptographicOperations.ZeroMemory(actual);
        }
    }

    public void Remove(string currentPassword)
    {
        if (!Verify(currentPassword))
            throw new CryptographicException("The current app-lock password is incorrect.");
        if (File.Exists(_credentialPath))
            File.Delete(_credentialPath);
    }

    public string CredentialPath => _credentialPath;

    private StoredCredential LoadValidated()
    {
        if (!File.Exists(_credentialPath))
            throw new FileNotFoundException("No Rice2k app-lock credential is configured.", _credentialPath);

        StoredCredential stored;
        try
        {
            var info = new FileInfo(_credentialPath);
            if (info.Length <= 0 || info.Length > 32 * 1024)
                throw new InvalidDataException("The Rice2k app-lock credential file has an invalid size.");

            stored = JsonSerializer.Deserialize<StoredCredential>(File.ReadAllText(_credentialPath))
                ?? throw new InvalidDataException("The Rice2k app-lock credential file is empty or malformed.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("The Rice2k app-lock credential file is malformed.", ex);
        }

        if (!string.Equals(stored.Format, Format, StringComparison.Ordinal) || stored.Version != Version)
            throw new NotSupportedException("This Rice2k app-lock credential format is not supported by this build.");
        if (stored.OpsLimit <= 0 || stored.OpsLimit > MaximumSupportedOpsLimit)
            throw new InvalidDataException("The app-lock credential contains an unsupported Argon2 operation limit.");
        if (stored.MemLimit < 8 * 1024 * 1024 || stored.MemLimit > MaximumSupportedMemLimit)
            throw new InvalidDataException("The app-lock credential contains an unsupported Argon2 memory limit.");

        _ = DecodeExact(stored.SaltBase64, 16, "salt");
        _ = DecodeExact(stored.VerifierBase64, VerifierLength, "verifier");
        return stored;
    }

    private static byte[] DecodeExact(string encoded, int expectedLength, string label)
    {
        byte[] value;
        try
        {
            value = Convert.FromBase64String(encoded ?? string.Empty);
        }
        catch (FormatException ex)
        {
            throw new InvalidDataException($"The app-lock credential contains an invalid {label}.", ex);
        }

        if (value.Length != expectedLength)
        {
            CryptographicOperations.ZeroMemory(value);
            throw new InvalidDataException($"The app-lock credential contains an invalid {label} length.");
        }
        return value;
    }

    private static void ValidateNewPassword(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        if (password.Length < MinimumPasswordLength)
            throw new ArgumentException($"Use an app-lock password of at least {MinimumPasswordLength} characters.");
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }
}
