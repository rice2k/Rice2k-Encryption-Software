using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Rice2k.Encryption.Models;
using Sodium;

namespace Rice2k.Encryption.Services;

public sealed class IdentityService
{
    private static readonly byte[] PrivateMagic = Encoding.ASCII.GetBytes("R2KID001");
    private const byte PrivateVersion = 1;
    private const byte KdfArgon2Id = 1;
    private const long OpsLimit = 4;
    private const int MemLimit = 64 * 1024 * 1024;
    private const int SaltSize = 16;
    private const int NonceSize = 24;
    private const int AuthenticationTagSize = 16;
    private const int MinimumPasswordLength = 12;
    private const int MaximumIdentityNameCharacters = 200;
    private const int MaximumPrivateCipherLength = 128 * 1024;
    private const int MaximumPrivatePayloadLength = MaximumPrivateCipherLength - AuthenticationTagSize;
    private const int MaximumPublicCardLength = 64 * 1024;
    private const long MaximumSupportedOpsLimit = 10;
    private const int MaximumSupportedMemLimit = 256 * 1024 * 1024;

    private sealed record PrivateIdentityPayload(
        Guid Id,
        string Name,
        DateTimeOffset CreatedUtc,
        string Fingerprint,
        string EncryptionPublicKeyBase64,
        string EncryptionPrivateKeyBase64,
        string SigningPublicKeyBase64,
        string SigningPrivateKeyBase64);

    private sealed record PublicIdentityCard(
        string Format,
        int Version,
        Guid Id,
        string Name,
        DateTimeOffset CreatedUtc,
        string Fingerprint,
        string EncryptionPublicKeyBase64,
        string SigningPublicKeyBase64,
        string SelfSignatureBase64);

    public Rice2kIdentity Generate(string name)
    {
        ValidateIdentityName(name);
        var encryption = PublicKeyBox.GenerateKeyPair();
        var signing = PublicKeyAuth.GenerateKeyPair();
        try
        {
            return new Rice2kIdentity(
                Guid.NewGuid(),
                name,
                DateTimeOffset.UtcNow,
                encryption.PublicKey,
                encryption.PrivateKey,
                signing.PublicKey,
                signing.PrivateKey);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encryption.PrivateKey);
            CryptographicOperations.ZeroMemory(signing.PrivateKey);
        }
    }

    public async Task ExportPrivateAsync(
        Rice2kIdentity identity,
        string destinationPath,
        string password,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ValidateIdentityId(identity.Id);
        ValidateIdentityName(identity.Name);
        ValidateNewPassword(password);
        ValidateNewDestination(destinationPath, ".r2kid");

        var encryptionPrivate = identity.CopyEncryptionPrivateKey();
        var signingPrivate = identity.CopySigningPrivateKey();
        var passwordBytes = Encoding.UTF8.GetBytes(password);
        byte[]? derivedKey = null;
        byte[]? payloadBytes = null;
        byte[]? cipher = null;
        var tempPath = destinationPath + $".{Guid.NewGuid():N}.partial";

        try
        {
            var payload = new PrivateIdentityPayload(
                identity.Id,
                identity.Name,
                identity.CreatedUtc,
                identity.Fingerprint,
                Convert.ToBase64String(identity.EncryptionPublicKey),
                Convert.ToBase64String(encryptionPrivate),
                Convert.ToBase64String(identity.SigningPublicKey),
                Convert.ToBase64String(signingPrivate));
            payloadBytes = JsonSerializer.SerializeToUtf8Bytes(payload);
            if (payloadBytes.Length > MaximumPrivatePayloadLength)
                throw new InvalidDataException("The private identity metadata is too large for the Rice2k identity-package format.");

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
                BuildPrivateAad(OpsLimit, MemLimit, salt));
            if (cipher.Length > MaximumPrivateCipherLength)
                throw new InvalidOperationException("Rice2k generated a private identity package outside its supported format limit.");

            await using (var output = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
            using (var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(PrivateMagic);
                writer.Write(PrivateVersion);
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
                throw new IOException("A file appeared at the selected identity-package destination before Rice2k could finalize it.");
            File.Move(tempPath, destinationPath);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encryptionPrivate);
            CryptographicOperations.ZeroMemory(signingPrivate);
            CryptographicOperations.ZeroMemory(passwordBytes);
            if (derivedKey is not null)
                CryptographicOperations.ZeroMemory(derivedKey);
            if (payloadBytes is not null)
                CryptographicOperations.ZeroMemory(payloadBytes);
            if (cipher is not null)
                CryptographicOperations.ZeroMemory(cipher);
        }
    }

    public async Task<Rice2kIdentity> ImportPrivateAsync(
        string sourcePath,
        string password,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("The selected Rice2k private identity package could not be found.", sourcePath);

        var passwordBytes = Encoding.UTF8.GetBytes(password);
        byte[]? derivedKey = null;
        byte[]? cipher = null;
        byte[]? plain = null;
        byte[]? encryptionPublic = null;
        byte[]? encryptionPrivate = null;
        byte[]? signingPublic = null;
        byte[]? signingPrivate = null;

        try
        {
            await using var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var reader = new BinaryReader(input, Encoding.UTF8, leaveOpen: true);

            var magic = reader.ReadBytes(PrivateMagic.Length);
            if (!magic.SequenceEqual(PrivateMagic))
                throw new InvalidDataException("This is not a supported Rice2k private identity package.");

            var version = reader.ReadByte();
            var kdf = reader.ReadByte();
            if (version != PrivateVersion)
                throw new NotSupportedException($"Rice2k identity-package version {version} is not supported by this build.");
            if (kdf != KdfArgon2Id)
                throw new NotSupportedException("This identity package uses an unsupported password derivation method.");

            var opsLimit = reader.ReadInt64();
            var memLimit = reader.ReadInt32();
            if (opsLimit < 3 || opsLimit > MaximumSupportedOpsLimit)
                throw new InvalidDataException("The identity package contains an unsupported Argon2id work factor.");
            if (memLimit < 8 * 1024 * 1024 || memLimit > MaximumSupportedMemLimit)
                throw new InvalidDataException("The identity package contains an unsupported Argon2id memory setting.");

            var salt = reader.ReadBytes(SaltSize);
            var nonce = reader.ReadBytes(NonceSize);
            if (salt.Length != SaltSize || nonce.Length != NonceSize)
                throw new InvalidDataException("The identity-package header is truncated.");

            var cipherLength = reader.ReadInt32();
            if (cipherLength < AuthenticationTagSize || cipherLength > MaximumPrivateCipherLength)
                throw new InvalidDataException("The encrypted identity payload length is invalid.");
            cipher = reader.ReadBytes(cipherLength);
            if (cipher.Length != cipherLength || input.Position != input.Length)
                throw new InvalidDataException("The identity package is truncated or contains unexpected trailing data.");

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
                plain = SecretAeadXChaCha20Poly1305.Decrypt(
                    cipher,
                    nonce,
                    derivedKey,
                    BuildPrivateAad(opsLimit, memLimit, salt));
            }
            catch (CryptographicException ex)
            {
                throw new CryptographicException("Rice2k could not unlock this private identity. The password may be wrong, or the package may have been modified.", ex);
            }

            var payload = JsonSerializer.Deserialize<PrivateIdentityPayload>(plain)
                ?? throw new InvalidDataException("The decrypted identity package is missing its identity data.");
            ValidateIdentityId(payload.Id);
            ValidateIdentityName(payload.Name);

            encryptionPublic = DecodeKey(payload.EncryptionPublicKeyBase64, 32, "encryption public key");
            encryptionPrivate = DecodeKey(payload.EncryptionPrivateKeyBase64, 32, "encryption private key");
            signingPublic = DecodeKey(payload.SigningPublicKeyBase64, 32, "signing public key");
            signingPrivate = DecodeKey(payload.SigningPrivateKeyBase64, 64, "signing private key");

            ValidatePrivateKeyPairs(encryptionPublic, encryptionPrivate, signingPublic, signingPrivate);
            var fingerprint = Rice2kIdentity.CreateFingerprint(encryptionPublic, signingPublic);
            if (!string.Equals(payload.Fingerprint, fingerprint, StringComparison.Ordinal))
                throw new InvalidDataException("The identity package fingerprint does not match its authenticated public keys.");

            return new Rice2kIdentity(
                payload.Id,
                payload.Name.Trim(),
                payload.CreatedUtc,
                encryptionPublic,
                encryptionPrivate,
                signingPublic,
                signingPrivate);
        }
        catch (EndOfStreamException ex)
        {
            throw new InvalidDataException("The identity package appears to be truncated.", ex);
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
            if (encryptionPublic is not null)
                CryptographicOperations.ZeroMemory(encryptionPublic);
            if (encryptionPrivate is not null)
                CryptographicOperations.ZeroMemory(encryptionPrivate);
            if (signingPublic is not null)
                CryptographicOperations.ZeroMemory(signingPublic);
            if (signingPrivate is not null)
                CryptographicOperations.ZeroMemory(signingPrivate);
        }
    }

    public async Task ExportPublicAsync(
        Rice2kIdentity identity,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ValidateIdentityId(identity.Id);
        ValidateIdentityName(identity.Name);
        ValidateNewDestination(destinationPath, ".r2kpub");

        var privateSigning = identity.CopySigningPrivateKey();
        byte[]? payloadToSign = null;
        byte[]? signature = null;
        byte[]? cardBytes = null;
        var tempPath = destinationPath + $".{Guid.NewGuid():N}.partial";

        try
        {
            payloadToSign = BuildPublicCardPayload(
                identity.Id,
                identity.Name,
                identity.CreatedUtc,
                identity.Fingerprint,
                identity.EncryptionPublicKey,
                identity.SigningPublicKey);
            signature = PublicKeyAuth.SignDetached(payloadToSign, privateSigning);

            var card = new PublicIdentityCard(
                "R2KPUB1",
                1,
                identity.Id,
                identity.Name,
                identity.CreatedUtc,
                identity.Fingerprint,
                Convert.ToBase64String(identity.EncryptionPublicKey),
                Convert.ToBase64String(identity.SigningPublicKey),
                Convert.ToBase64String(signature));
            cardBytes = JsonSerializer.SerializeToUtf8Bytes(card, new JsonSerializerOptions { WriteIndented = true });
            if (cardBytes.Length <= 0 || cardBytes.Length > MaximumPublicCardLength)
                throw new InvalidDataException("The public identity card is too large for the supported Rice2k public-card format.");

            await using (var output = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
            {
                await output.WriteAsync(cardBytes, cancellationToken);
                await output.FlushAsync(cancellationToken);
                output.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(destinationPath))
                throw new IOException("A file appeared at the selected public-identity destination before Rice2k could finalize it.");
            File.Move(tempPath, destinationPath);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(privateSigning);
            if (payloadToSign is not null)
                CryptographicOperations.ZeroMemory(payloadToSign);
            if (signature is not null)
                CryptographicOperations.ZeroMemory(signature);
            if (cardBytes is not null)
                CryptographicOperations.ZeroMemory(cardBytes);
        }
    }

    public async Task<Rice2kPublicIdentity> ImportPublicAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        PublicIdentityCard card;
        try
        {
            var cardBytes = await BoundedFileReader.ReadAllBytesAsync(
                sourcePath,
                MaximumPublicCardLength,
                "public identity card",
                cancellationToken);
            card = JsonSerializer.Deserialize<PublicIdentityCard>(cardBytes)
                ?? throw new InvalidDataException("The public identity card is empty or malformed.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("The public identity card is malformed.", ex);
        }

        if (!string.Equals(card.Format, "R2KPUB1", StringComparison.Ordinal) || card.Version != 1)
            throw new NotSupportedException("This public identity format is not supported by this Rice2k build.");
        ValidateIdentityId(card.Id);
        ValidateIdentityName(card.Name);

        var encryptionPublic = DecodeKey(card.EncryptionPublicKeyBase64, 32, "encryption public key");
        var signingPublic = DecodeKey(card.SigningPublicKeyBase64, 32, "signing public key");
        var signature = DecodeKey(card.SelfSignatureBase64, 64, "self-signature");
        byte[]? payload = null;

        try
        {
            var fingerprint = Rice2kIdentity.CreateFingerprint(encryptionPublic, signingPublic);
            if (!string.Equals(card.Fingerprint, fingerprint, StringComparison.Ordinal))
                throw new InvalidDataException("The public identity fingerprint does not match its public keys.");

            payload = BuildPublicCardPayload(
                card.Id,
                card.Name,
                card.CreatedUtc,
                card.Fingerprint,
                encryptionPublic,
                signingPublic);
            if (!PublicKeyAuth.VerifyDetached(signature, payload, signingPublic))
                throw new CryptographicException("The public identity self-signature is invalid. The card may have been modified.");

            return new Rice2kPublicIdentity(
                card.Id,
                card.Name.Trim(),
                card.CreatedUtc,
                fingerprint,
                encryptionPublic.ToArray(),
                signingPublic.ToArray());
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encryptionPublic);
            CryptographicOperations.ZeroMemory(signingPublic);
            CryptographicOperations.ZeroMemory(signature);
            if (payload is not null)
                CryptographicOperations.ZeroMemory(payload);
        }
    }

    private static void ValidatePrivateKeyPairs(
        byte[] encryptionPublic,
        byte[] encryptionPrivate,
        byte[] signingPublic,
        byte[] signingPrivate)
    {
        var encryptionPair = PublicKeyBox.GenerateKeyPair(encryptionPrivate);
        byte[]? derivedSigningPublic = null;
        try
        {
            if (!CryptographicOperations.FixedTimeEquals(encryptionPair.PublicKey, encryptionPublic))
                throw new InvalidDataException("The private identity contains an inconsistent encryption key pair.");

            derivedSigningPublic = PublicKeyAuth.ExtractEd25519PublicKeyFromEd25519SecretKey(signingPrivate);
            if (!CryptographicOperations.FixedTimeEquals(derivedSigningPublic, signingPublic))
                throw new InvalidDataException("The private identity contains an inconsistent signing key pair.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encryptionPair.PrivateKey);
            CryptographicOperations.ZeroMemory(encryptionPair.PublicKey);
            if (derivedSigningPublic is not null)
                CryptographicOperations.ZeroMemory(derivedSigningPublic);
        }
    }

    private static byte[] BuildPublicCardPayload(
        Guid id,
        string name,
        DateTimeOffset createdUtc,
        string fingerprint,
        byte[] encryptionPublic,
        byte[] signingPublic)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write("R2KPUB1-SIGNED-PAYLOAD");
        writer.Write(id.ToByteArray());
        writer.Write(name.Trim());
        writer.Write(createdUtc.UtcTicks);
        writer.Write(fingerprint);
        writer.Write(encryptionPublic.Length);
        writer.Write(encryptionPublic);
        writer.Write(signingPublic.Length);
        writer.Write(signingPublic);
        writer.Flush();
        return stream.ToArray();
    }

    private static byte[] BuildPrivateAad(long opsLimit, int memLimit, byte[] salt)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(PrivateMagic);
        writer.Write(PrivateVersion);
        writer.Write(KdfArgon2Id);
        writer.Write(opsLimit);
        writer.Write(memLimit);
        writer.Write(salt);
        writer.Flush();
        return stream.ToArray();
    }

    private static byte[] DecodeKey(string value, int expectedLength, string label)
    {
        byte[] decoded;
        try
        {
            decoded = Convert.FromBase64String(value ?? string.Empty);
        }
        catch (FormatException ex)
        {
            throw new InvalidDataException($"The identity contains an invalid {label}.", ex);
        }

        if (decoded.Length != expectedLength)
        {
            CryptographicOperations.ZeroMemory(decoded);
            throw new InvalidDataException($"The identity contains an invalid {label} length.");
        }
        return decoded;
    }

    private static void ValidateIdentityId(Guid id)
    {
        if (id == Guid.Empty)
            throw new InvalidDataException("The Rice2k identity contains an invalid empty identifier.");
    }

    private static void ValidateIdentityName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > MaximumIdentityNameCharacters)
            throw new InvalidDataException($"Rice2k identity names must contain 1 to {MaximumIdentityNameCharacters} characters.");
    }

    private static void ValidateNewPassword(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        if (password.Length < MinimumPasswordLength)
            throw new ArgumentException($"Use a password of at least {MinimumPasswordLength} characters for private identity packages.");
    }

    private static void ValidateNewDestination(string destinationPath, string extension)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        if (File.Exists(destinationPath))
            throw new IOException($"The selected {extension} file already exists. Rice2k will not overwrite it automatically.");

        var directory = Path.GetDirectoryName(Path.GetFullPath(destinationPath));
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            throw new DirectoryNotFoundException("The selected identity-package folder does not exist.");
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
            // Best-effort temporary-file cleanup only.
        }
    }
}
