using Rice2k.Encryption.Models;

namespace Rice2k.Encryption.Services;

public sealed class PublicIdentityContactStore
{
    private readonly IdentityService _identityService = new();
    private readonly string _contactsDirectory;

    public PublicIdentityContactStore()
    {
        _contactsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Rice2k Encryption Software",
            "Public Identity Contacts");
    }

    public async Task<Rice2kPublicIdentity> ImportAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        var identity = await _identityService.ImportPublicAsync(sourcePath, cancellationToken);
        Directory.CreateDirectory(_contactsDirectory);

        var destinationPath = Path.Combine(_contactsDirectory, $"{identity.Id:N}.r2kpub");
        if (File.Exists(destinationPath))
        {
            var existing = await _identityService.ImportPublicAsync(destinationPath, cancellationToken);
            if (string.Equals(existing.Fingerprint, identity.Fingerprint, StringComparison.Ordinal))
                return identity;

            throw new InvalidDataException(
                "A saved public identity already uses this identity ID but has different public keys. Rice2k will not replace it automatically.");
        }

        var tempPath = destinationPath + $".{Guid.NewGuid():N}.partial";
        try
        {
            await using (var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var output = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous))
            {
                await source.CopyToAsync(output, cancellationToken);
                await output.FlushAsync(cancellationToken);
                output.Flush(flushToDisk: true);
            }

            // Re-parse and verify the exact stored bytes before finalizing the contact.
            var stored = await _identityService.ImportPublicAsync(tempPath, cancellationToken);
            if (stored.Id != identity.Id || !string.Equals(stored.Fingerprint, identity.Fingerprint, StringComparison.Ordinal))
                throw new InvalidDataException("The saved public identity copy did not match the imported contact.");

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(tempPath, destinationPath);
            return identity;
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    public async Task<IReadOnlyList<Rice2kPublicIdentity>> LoadAllAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_contactsDirectory))
            return Array.Empty<Rice2kPublicIdentity>();

        var results = new List<Rice2kPublicIdentity>();
        foreach (var path in Directory.EnumerateFiles(_contactsDirectory, "*.r2kpub", SearchOption.TopDirectoryOnly).Take(1000))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                results.Add(await _identityService.ImportPublicAsync(path, cancellationToken));
            }
            catch
            {
                // Invalid/tampered contacts are omitted rather than silently trusted.
                // The original file remains on disk for manual review.
            }
        }

        return results
            .OrderBy(identity => identity.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(identity => identity.Fingerprint, StringComparer.Ordinal)
            .ToArray();
    }

    public async Task RemoveAsync(
        Rice2kPublicIdentity identity,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        var path = Path.Combine(_contactsDirectory, $"{identity.Id:N}.r2kpub");
        if (!File.Exists(path))
            return;

        var stored = await _identityService.ImportPublicAsync(path, cancellationToken);
        if (!string.Equals(stored.Fingerprint, identity.Fingerprint, StringComparison.Ordinal))
            throw new InvalidDataException("The saved contact changed and no longer matches the selected fingerprint. Rice2k will not delete it automatically.");

        cancellationToken.ThrowIfCancellationRequested();
        File.Delete(path);
    }

    public string ContactsDirectory => _contactsDirectory;

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
