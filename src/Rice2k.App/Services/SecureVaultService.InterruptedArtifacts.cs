using System.Security.Cryptography;
using Rice2k.Encryption.Models;

namespace Rice2k.Encryption.Services;

public sealed partial class SecureVaultService
{
    public sealed record VaultRecoveryArtifact(
        string Path,
        string Kind,
        long FileSize,
        DateTimeOffset LastWriteUtc);

    public sealed record VaultPendingVerification(
        string Path,
        long Sequence,
        int EntryCount,
        DateTimeOffset UpdatedUtc,
        long FileSize);

    public static IReadOnlyList<VaultRecoveryArtifact> FindRecoveryArtifacts(string vaultPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vaultPath);
        var fullPath = Path.GetFullPath(vaultPath);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new DirectoryNotFoundException("The vault folder could not be determined.");
        var filename = Path.GetFileName(fullPath);
        var results = new List<VaultRecoveryArtifact>();

        var backup = RecoveryBackupPath(fullPath);
        if (File.Exists(backup))
        {
            var info = new FileInfo(backup);
            results.Add(new VaultRecoveryArtifact(
                info.FullName,
                "Recovery backup",
                info.Length,
                info.LastWriteTimeUtc));
        }

        foreach (var pending in Directory.EnumerateFiles(directory, $".{filename}.*.pending", SearchOption.TopDirectoryOnly))
        {
            var info = new FileInfo(pending);
            results.Add(new VaultRecoveryArtifact(
                info.FullName,
                "Interrupted pending save",
                info.Length,
                info.LastWriteTimeUtc));
        }

        return results
            .OrderByDescending(item => item.LastWriteUtc)
            .ToArray();
    }

    public async Task<VaultPendingVerification> VerifyInterruptedPendingAsync(
        SecureVaultSession session,
        string pendingPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ValidatePendingArtifactPath(session.VaultPath, pendingPath);
        if (!File.Exists(pendingPath))
            throw new FileNotFoundException("The interrupted pending vault file could not be found.", pendingPath);

        var key = session.CopyContentKey();
        try
        {
            var state = await ReadStateWithKeyAsync(pendingPath, key, verifyContents: true, cancellationToken);
            if (state.Manifest.VaultId != session.VaultId)
                throw new InvalidDataException("This pending file belongs to a different Rice2k vault.");
            if (state.Manifest.Sequence < session.Sequence)
                throw new InvalidDataException("This pending file is older than the currently unlocked vault state.");

            return new VaultPendingVerification(
                Path.GetFullPath(pendingPath),
                state.Manifest.Sequence,
                state.Manifest.Entries.Count,
                state.Manifest.UpdatedUtc,
                new FileInfo(pendingPath).Length);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    public async Task PreserveInterruptedPendingAsync(
        SecureVaultSession session,
        string pendingPath,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        var verified = await VerifyInterruptedPendingAsync(session, pendingPath, cancellationToken);
        var destination = Path.GetFullPath(destinationPath);
        if (File.Exists(destination))
            throw new IOException("A file already exists at the selected destination. Rice2k will not overwrite it automatically.");
        var directory = Path.GetDirectoryName(destination);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            throw new DirectoryNotFoundException("The selected destination folder does not exist.");

        cancellationToken.ThrowIfCancellationRequested();
        File.Move(verified.Path, destination);
    }

    private static void ValidatePendingArtifactPath(string vaultPath, string pendingPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pendingPath);
        var fullVault = Path.GetFullPath(vaultPath);
        var fullPending = Path.GetFullPath(pendingPath);
        var directory = Path.GetDirectoryName(fullVault)
            ?? throw new DirectoryNotFoundException("The vault folder could not be determined.");
        var expectedPrefix = $".{Path.GetFileName(fullVault)}.";

        if (!string.Equals(Path.GetDirectoryName(fullPending), directory, StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(fullPending).StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase) ||
            !fullPending.EndsWith(".pending", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The selected file is not a pending-save artifact for this vault.");
        }
    }
}
