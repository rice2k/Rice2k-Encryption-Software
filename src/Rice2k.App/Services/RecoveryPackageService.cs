using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Rice2k.Encryption.Models;
using Sodium;

namespace Rice2k.Encryption.Services;

public sealed class RecoveryPackageService
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("R2KREC01");
    private const byte Version = 1;
    private const byte KdfArgon2Id = 1;
    private const long OpsLimit = 4;
    private const int MemLimit = 64 * 1024 * 1024;
    private const int SaltSize = 16;
    private const int NonceSize = 24;
    private const int SecretKeySize = 32;
    private const int AuthenticationTagSize = 16;
    private const int MinimumPasswordLength = 12;
    private const int MaximumCipherLength = 64 * 1024;
    private const int MaximumPayloadLength = MaximumCipherLength - AuthenticationTagSize;
    private const long MaximumSupportedOpsLimit = 10;
    private const int MaximumSupportedMemLimit = 256 * 1024 * 1024;

    private sealed record RecoveryPayload(
        Guid Id,
        string Name,
        DateTimeOffset OriginalCreatedUtc,
        DateTimeOffset RecoveryCreatedUtc,
        string SecretKeyBase64);

    public async Task CreateAsync(ManagedKey managedKey, string destinationPath, string recoveryPassword, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(managedKey);
        ValidatePassword(recoveryPassword, creatingPackage: true);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        if (File.Exists(destinationPath))
            throw new IOException("The selected .r2krecovery file already exists. Rice2k will not overwrite it automatically.");

        var directory = Path.GetDirectoryName(Path.GetFullPath(destinationPath));
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            throw new DirectoryNotFoundException("The selected recovery-package folder does not exist.");

        var secret = managedKey.CopySecretKey();
        var passwordBytes = Encoding.UTF8.GetBytes(recoveryPassword);
        byte[]? derivedKey = null;
        byte[]? payloadBytes = null;
        byte[]? cipher = null;
        var tempPath = destinationPath + $".{Guid.NewGuid():N}.partial";

        try
        {
            var payload = new RecoveryPayload(
                managedKey.Id,
                managedKey.Name,
                managedKey.CreatedUtc,
                DateTimeOffset.UtcNow,
                Convert.ToBase64String(secret));
            payloadBytes = JsonSerializer.SerializeToUtf8Bytes(payload);
            if (payloadBytes.Length > MaximumPayloadLength)
            {
                throw new InvalidDataException(
                    "The key name/metadata is too large for the Rice2k recovery-package format. Use a shorter key name before creating recovery material.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            var salt = PasswordHash.ArgonGenerateSalt();
            var nonce = SecretAeadXChaCha20Poly1305.GenerateNonce();
            derivedKey = PasswordHash.ArgonHashBinary(
                passwordBytes,
                salt,
                OpsLimit,
                MemLimit,
                32,
                PasswordHash.ArgonAlgorithm.Argon_2ID13);

            cipher = SecretAeadXChaCha20Poly1305.Encrypt(
                payloadBytes,
                nonce,
                derivedKey,
                BuildAad(OpsLimit, MemLimit, salt));
            if (cipher.Length > MaximumCipherLength)
                throw new InvalidOperationException("Rice2k generated a recovery package outside its supported format limit.");

            await using (var output = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
            using (var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(Magic);
                writer.Write(Version);
                writer.Write(KdfArgon2Id);
                writer.Write(OpsLimit);
                writer.Write(MemLimit);
                writer.Write(salt);
                writer.Write(nonce);
                writer.Write(cipher.Length);
                writer.Write(cipher);
                writer.Flush();
                await output.FlushAsync(cancellationToken);
                output.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(destinationPath))
                throw new IOException("A file appeared at the selected destination before Rice2k could finalize the recovery package.");

            File.Move(tempPath, destinationPath);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
            CryptographicOperations.ZeroMemory(passwordBytes);
            if (derivedKey is not null)
                CryptographicOperations.ZeroMemory(derivedKey);
            if (payloadBytes is not null)
                CryptographicOperations.ZeroMemory(payloadBytes);
            if (cipher is not null)
                CryptographicOperations.ZeroMemory(cipher);
        }
    }

    public async Task<ManagedKey> OpenAsync(string sourcePath, string recoveryPassword, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ValidatePassword(recoveryPassword, creatingPackage: false);

        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("The selected .r2krecovery file could not be found.", sourcePath);

        var passwordBytes = Encoding.UTF8.GetBytes(recoveryPassword);
        byte[]? derivedKey = null;
        byte[]? cipher = null;
        byte[]? plain = null;
        byte[]? secret = null;

        try
        {
            await using var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var reader = new BinaryReader(input, Encoding.UTF8, leaveOpen: true);

            var magic = reader.ReadBytes(Magic.Length);
            if (!magic.SequenceEqual(Magic))
                throw new InvalidDataException("This is not a supported Rice2k recovery package.");

            var version = reader.ReadByte();
            var kdf = reader.ReadByte();
            if (version != Version)
                throw new NotSupportedException($"Rice2k recovery-package version {version} is not supported by this build.");
            if (kdf != KdfArgon2Id)
                throw new NotSupportedException("This recovery package uses an unsupported password derivation method.");

            var opsLimit = reader.ReadInt64();
            var memLimit = reader.ReadInt32();
            if (opsLimit < 3 || opsLimit > MaximumSupportedOpsLimit)
                throw new InvalidDataException("The recovery package contains an unsupported Argon2id work factor.");
            if (memLimit < 8 * 1024 * 1024 || memLimit > MaximumSupportedMemLimit)
                throw new InvalidDataException("The recovery package contains an unsupported Argon2id memory setting.");

            var salt = reader.ReadBytes(SaltSize);
            var nonce = reader.ReadBytes(NonceSize);
            if (salt.Length != SaltSize || nonce.Length != NonceSize)
                throw new InvalidDataException("The recovery package header is truncated.");

            var cipherLength = reader.ReadInt32();
            if (cipherLength < AuthenticationTagSize || cipherLength > MaximumCipherLength)
                throw new InvalidDataException("The encrypted recovery payload length is invalid.");

            cipher = reader.ReadBytes(cipherLength);
            if (cipher.Length != cipherLength || input.Position != input.Length)
                throw new InvalidDataException("The recovery package is truncated or contains unexpected trailing data.");

            cancellationToken.ThrowIfCancellationRequested();
            derivedKey = PasswordHash.ArgonHashBinary(
                passwordBytes,
                salt,
                opsLimit,
                memLimit,
                32,
                PasswordHash.ArgonAlgorithm.Argon_2ID13);

            try
            {
                plain = SecretAeadXChaCha20Poly1305.Decrypt(cipher, nonce, derivedKey, BuildAad(opsLimit, memLimit, salt));
            }
            catch (CryptographicException ex)
            {
                throw new CryptographicException("Rice2k could not unlock this recovery package. The password may be wrong, or the package may have been modified.", ex);
            }

            var payload = JsonSerializer.Deserialize<RecoveryPayload>(plain)
                ?? throw new InvalidDataException("The decrypted recovery package is missing its key data.");

            try
            {
                secret = Convert.FromBase64String(payload.SecretKeyBase64);
            }
            catch (FormatException ex)
            {
                throw new InvalidDataException("The recovery package contains invalid key data.", ex);
            }

            if (secret.Length != SecretKeySize)
                throw new InvalidDataException("The recovery package does not contain a valid 256-bit key.");

            return new ManagedKey(payload.Id, payload.Name, payload.OriginalCreatedUtc, "Recovered .r2krecovery", secret);
        }
        catch (EndOfStreamException ex)
        {
            throw new InvalidDataException("The recovery package appears to be truncated.", ex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
            if (derivedKey is not null)
                CryptographicOperations.ZeroMemory(derivedKey);
            if (cipher is not null)
                CryptographicOperations.ZeroMemory(cipher);
            if (plain is not null)
                CryptographicOperations.ZeroMemory(plain);
            if (secret is not null)
                CryptographicOperations.ZeroMemory(secret);
        }
    }

    private static byte[] BuildAad(long opsLimit, int memLimit, byte[] salt)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(Magic);
        writer.Write(Version);
        writer.Write(KdfArgon2Id);
        writer.Write(opsLimit);
        writer.Write(memLimit);
        writer.Write(salt);
        writer.Flush();
        return stream.ToArray();
    }

    private static void ValidatePassword(string password, bool creatingPackage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        if (creatingPackage && password.Length < MinimumPasswordLength)
            throw new ArgumentException($"Use a recovery password of at least {MinimumPasswordLength} characters.");
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
