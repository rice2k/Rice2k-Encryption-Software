using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Rice2k.Encryption.Models;
using Sodium;

namespace Rice2k.Encryption.Services;

public sealed class FileSignatureService
{
    private const string Format = "R2KSIG1";
    private const int Version = 1;
    private const int MaximumSignatureFileLength = 128 * 1024;

    private sealed record SignatureDocument(
        string Format,
        int Version,
        DateTimeOffset SignedUtc,
        Guid SignerIdentityId,
        string SignerName,
        string SignerFingerprint,
        string EncryptionPublicKeyBase64,
        string SigningPublicKeyBase64,
        string OriginalFileName,
        long FileLength,
        string HashAlgorithm,
        string FileHashBase64,
        string SignatureBase64);

    public async Task SignAsync(
        string sourcePath,
        string destinationPath,
        Rice2kIdentity identity,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("The file to sign could not be found.", sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        if (File.Exists(destinationPath))
            throw new IOException("The selected .r2ksig file already exists. Rice2k will not overwrite it automatically.");

        var destinationDirectory = Path.GetDirectoryName(Path.GetFullPath(destinationPath));
        if (string.IsNullOrWhiteSpace(destinationDirectory) || !Directory.Exists(destinationDirectory))
            throw new DirectoryNotFoundException("The selected signature folder does not exist.");

        var sourceInfo = new FileInfo(sourcePath);
        var signedUtc = DateTimeOffset.UtcNow;
        var signingPrivate = identity.CopySigningPrivateKey();
        byte[]? hash = null;
        byte[]? payload = null;
        byte[]? signature = null;
        var tempPath = destinationPath + $".{Guid.NewGuid():N}.partial";

        try
        {
            hash = await ComputeSha512Async(sourcePath, cancellationToken);
            payload = BuildSignedPayload(
                signedUtc,
                identity.Id,
                identity.Name,
                identity.Fingerprint,
                identity.EncryptionPublicKey,
                identity.SigningPublicKey,
                sourceInfo.Name,
                sourceInfo.Length,
                hash);
            signature = PublicKeyAuth.SignDetached(payload, signingPrivate);

            var document = new SignatureDocument(
                Format,
                Version,
                signedUtc,
                identity.Id,
                identity.Name,
                identity.Fingerprint,
                Convert.ToBase64String(identity.EncryptionPublicKey),
                Convert.ToBase64String(identity.SigningPublicKey),
                sourceInfo.Name,
                sourceInfo.Length,
                "SHA-512",
                Convert.ToBase64String(hash),
                Convert.ToBase64String(signature));

            var json = JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(tempPath, json, Encoding.UTF8, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(destinationPath))
                throw new IOException("A file appeared at the signature destination before Rice2k could finalize the signature.");
            File.Move(tempPath, destinationPath);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(signingPrivate);
            if (hash is not null)
                CryptographicOperations.ZeroMemory(hash);
            if (payload is not null)
                CryptographicOperations.ZeroMemory(payload);
            if (signature is not null)
                CryptographicOperations.ZeroMemory(signature);
        }
    }

    public async Task<SignatureVerificationResult> VerifyAsync(
        string sourcePath,
        string signaturePath,
        Rice2kPublicIdentity? expectedIdentity = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("The file to verify could not be found.", sourcePath);
        if (!File.Exists(signaturePath))
            throw new FileNotFoundException("The .r2ksig file could not be found.", signaturePath);

        var sigInfo = new FileInfo(signaturePath);
        if (sigInfo.Length <= 0 || sigInfo.Length > MaximumSignatureFileLength)
            throw new InvalidDataException("The Rice2k signature file has an invalid size.");

        SignatureDocument document;
        try
        {
            var json = await File.ReadAllTextAsync(signaturePath, cancellationToken);
            document = JsonSerializer.Deserialize<SignatureDocument>(json)
                ?? throw new InvalidDataException("The Rice2k signature file is empty or malformed.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("The Rice2k signature file is malformed.", ex);
        }

        ValidateDocumentBasics(document);

        var encryptionPublic = Decode(document.EncryptionPublicKeyBase64, 32, "encryption public key");
        var signingPublic = Decode(document.SigningPublicKeyBase64, 32, "signing public key");
        var expectedHash = Decode(document.FileHashBase64, 64, "SHA-512 file hash");
        var signature = Decode(document.SignatureBase64, 64, "Ed25519 signature");
        byte[]? signedPayload = null;
        byte[]? actualHash = null;

        try
        {
            var computedFingerprint = Rice2kIdentity.CreateFingerprint(encryptionPublic, signingPublic);
            if (!string.Equals(document.SignerFingerprint, computedFingerprint, StringComparison.Ordinal))
                throw new InvalidDataException("The signature fingerprint does not match its embedded public keys.");

            signedPayload = BuildSignedPayload(
                document.SignedUtc,
                document.SignerIdentityId,
                document.SignerName,
                document.SignerFingerprint,
                encryptionPublic,
                signingPublic,
                document.OriginalFileName,
                document.FileLength,
                expectedHash);

            var cryptographicSignatureValid = PublicKeyAuth.VerifyDetached(signature, signedPayload, signingPublic);
            if (!cryptographicSignatureValid)
                throw new CryptographicException("The detached signature is invalid. The .r2ksig file may have been modified or may not belong to this signing key.");

            actualHash = await ComputeSha512Async(sourcePath, cancellationToken);
            var currentInfo = new FileInfo(sourcePath);
            var contentMatches = currentInfo.Length == document.FileLength &&
                                 CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);

            bool? matchesExpectedIdentity = null;
            if (expectedIdentity is not null)
            {
                matchesExpectedIdentity =
                    expectedIdentity.Id == document.SignerIdentityId &&
                    string.Equals(expectedIdentity.Fingerprint, document.SignerFingerprint, StringComparison.Ordinal) &&
                    CryptographicOperations.FixedTimeEquals(expectedIdentity.EncryptionPublicKey, encryptionPublic) &&
                    CryptographicOperations.FixedTimeEquals(expectedIdentity.SigningPublicKey, signingPublic);
            }

            return new SignatureVerificationResult(
                true,
                contentMatches,
                matchesExpectedIdentity,
                document.SignerName,
                document.SignerFingerprint,
                document.SignerIdentityId,
                document.SignedUtc,
                document.OriginalFileName,
                currentInfo.Name,
                string.Equals(currentInfo.Name, document.OriginalFileName, StringComparison.Ordinal),
                document.FileLength,
                currentInfo.Length,
                document.HashAlgorithm);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encryptionPublic);
            CryptographicOperations.ZeroMemory(signingPublic);
            CryptographicOperations.ZeroMemory(expectedHash);
            CryptographicOperations.ZeroMemory(signature);
            if (signedPayload is not null)
                CryptographicOperations.ZeroMemory(signedPayload);
            if (actualHash is not null)
                CryptographicOperations.ZeroMemory(actualHash);
        }
    }

    private static void ValidateDocumentBasics(SignatureDocument document)
    {
        if (!string.Equals(document.Format, Format, StringComparison.Ordinal) || document.Version != Version)
            throw new NotSupportedException("This Rice2k signature format is not supported by this build.");
        if (!string.Equals(document.HashAlgorithm, "SHA-512", StringComparison.Ordinal))
            throw new NotSupportedException("This Rice2k signature uses an unsupported file-hash algorithm.");
        if (string.IsNullOrWhiteSpace(document.SignerName) || document.SignerName.Length > 200)
            throw new InvalidDataException("The signature contains an invalid signer label.");
        if (string.IsNullOrWhiteSpace(document.SignerFingerprint) || document.SignerFingerprint.Length > 100)
            throw new InvalidDataException("The signature contains an invalid signer fingerprint.");
        if (string.IsNullOrWhiteSpace(document.OriginalFileName) || document.OriginalFileName.Length > 1024)
            throw new InvalidDataException("The signature contains an invalid original filename.");
        if (document.FileLength < 0)
            throw new InvalidDataException("The signature contains an invalid file length.");
    }

    private static byte[] BuildSignedPayload(
        DateTimeOffset signedUtc,
        Guid identityId,
        string signerName,
        string signerFingerprint,
        byte[] encryptionPublic,
        byte[] signingPublic,
        string originalFileName,
        long fileLength,
        byte[] fileHash)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write("R2KSIG1-DETACHED-FILE-SIGNATURE");
        writer.Write(Version);
        writer.Write(signedUtc.UtcDateTime.Ticks);
        writer.Write(identityId.ToByteArray());
        writer.Write(signerName.Trim());
        writer.Write(signerFingerprint);
        writer.Write(encryptionPublic.Length);
        writer.Write(encryptionPublic);
        writer.Write(signingPublic.Length);
        writer.Write(signingPublic);
        writer.Write(originalFileName);
        writer.Write(fileLength);
        writer.Write("SHA-512");
        writer.Write(fileHash.Length);
        writer.Write(fileHash);
        writer.Flush();
        return stream.ToArray();
    }

    private static async Task<byte[]> ComputeSha512Async(string path, CancellationToken cancellationToken)
    {
        await using var input = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var sha = SHA512.Create();
        return await sha.ComputeHashAsync(input, cancellationToken);
    }

    private static byte[] Decode(string value, int expectedLength, string label)
    {
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(value ?? string.Empty);
        }
        catch (FormatException ex)
        {
            throw new InvalidDataException($"The signature contains an invalid {label}.", ex);
        }

        if (bytes.Length != expectedLength)
        {
            CryptographicOperations.ZeroMemory(bytes);
            throw new InvalidDataException($"The signature contains an invalid {label} length.");
        }
        return bytes;
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
