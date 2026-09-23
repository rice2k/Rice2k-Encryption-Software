namespace Rice2k.Encryption.Models;

public sealed record SignatureVerificationResult(
    bool CryptographicSignatureValid,
    bool FileContentMatches,
    bool? MatchesExpectedIdentity,
    string SignerName,
    string SignerFingerprint,
    Guid SignerIdentityId,
    DateTimeOffset SignedUtc,
    string OriginalFileName,
    string CurrentFileName,
    bool CurrentNameMatchesOriginal,
    long SignedFileLength,
    long CurrentFileLength,
    string HashAlgorithm)
{
    public bool IsValid => CryptographicSignatureValid && FileContentMatches && MatchesExpectedIdentity != false;
}
