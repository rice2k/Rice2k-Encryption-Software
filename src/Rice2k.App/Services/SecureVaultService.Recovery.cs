using System.Security.Cryptography;
using Rice2k.Encryption.Models;

namespace Rice2k.Encryption.Services;

public sealed partial class SecureVaultService
{
    public sealed record VaultRecoveryBackupInfo(
        string BackupPath,
        long Sequence,
        DateTimeOffset UpdatedUtc,
        int EntryCount,
        long FileSize);

    public async Task<VaultRecoveryBackupInfo> VerifyRecoveryBackupAsync(
        SecureVaultSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        var backupPath = RecoveryBackupPath(session.VaultPath);
        if (!File.Exists(backupPath))
            throw new FileNotFoundException("No preserved Rice2k vault recovery backup was found.", backupPath);

        var key = session.CopyContentKey();
        try
        {
            var state = await ReadStateWithKeyAsync(backupPath, key, verifyContents: true, cancellationToken);
            if (state.Manifest.VaultId != session.VaultId)
                throw new InvalidDataException("The preserved backup belongs to a different vault and will not be used for this session.");

            return new VaultRecoveryBackupInfo(
                backupPath,
                state.Manifest.Sequence,
                state.Manifest.UpdatedUtc,
                state.Manifest.Entries.Count,
                new FileInfo(backupPath).Length);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    public async Task<string> RestoreRecoveryBackupAsync(
        SecureVaultSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        var vaultPath = session.VaultPath;
        var backupPath = RecoveryBackupPath(vaultPath);
        if (!File.Exists(backupPath))
            throw new FileNotFoundException("No preserved Rice2k vault recovery backup was found.", backupPath);

        var key = session.CopyContentKey();
        var archivedCurrentPath = CreateRecoveryArchivePath(vaultPath);
        try
        {
            var backupState = await ReadStateWithKeyAsync(backupPath, key, verifyContents: true, cancellationToken);
            if (backupState.Manifest.VaultId != session.VaultId)
                throw new InvalidDataException("The preserved backup belongs to a different vault and cannot replace this vault.");

            // Confirm the active vault has not changed since this session was opened.
            var currentState = await ReadStateWithKeyAsync(vaultPath, key, verifyContents: false, cancellationToken);
            EnsureSessionMatches(session, currentState.Manifest);

            cancellationToken.ThrowIfCancellationRequested();
            File.Replace(backupPath, vaultPath, archivedCurrentPath, ignoreMetadataErrors: true);

            try
            {
                var restored = await ReadStateWithKeyAsync(vaultPath, key, verifyContents: true, cancellationToken);
                if (restored.Manifest.VaultId != session.VaultId || restored.Manifest.Sequence != backupState.Manifest.Sequence)
                    throw new InvalidDataException("The restored vault did not match the verified recovery backup.");

                session.UpdateState(restored.Manifest.UpdatedUtc, restored.Manifest.Sequence, restored.Manifest.Entries);
                return archivedCurrentPath;
            }
            catch
            {
                TryRollbackRecoveryRestore(vaultPath, archivedCurrentPath, backupPath);
                throw;
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    public async Task PreserveRecoveryBackupAsync(
        SecureVaultSession session,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        var backupPath = RecoveryBackupPath(session.VaultPath);
        if (!File.Exists(backupPath))
            throw new FileNotFoundException("No preserved Rice2k vault recovery backup was found.", backupPath);

        var destinationFullPath = Path.GetFullPath(destinationPath);
        if (File.Exists(destinationFullPath))
            throw new IOException("A file already exists at the selected backup destination. Rice2k will not overwrite it automatically.");
        var directory = Path.GetDirectoryName(destinationFullPath);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            throw new DirectoryNotFoundException("The selected backup destination folder does not exist.");

        // Never move a recovery candidate aside without first proving that it authenticates
        // with this unlocked vault's key and belongs to the same vault ID.
        _ = await VerifyRecoveryBackupAsync(session, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        File.Move(backupPath, destinationFullPath);
    }

    private static string CreateRecoveryArchivePath(string vaultPath)
    {
        var basePath = $"{vaultPath}.pre-recovery-{DateTime.UtcNow:yyyyMMdd-HHmmss}.backup";
        if (!File.Exists(basePath))
            return basePath;

        for (var index = 2; index < 10000; index++)
        {
            var candidate = $"{basePath}.{index}";
            if (!File.Exists(candidate))
                return candidate;
        }

        throw new IOException("Rice2k could not create a unique archive name for the current vault before recovery.");
    }

    private static void TryRollbackRecoveryRestore(string vaultPath, string archivedCurrentPath, string backupPath)
    {
        try
        {
            if (!File.Exists(archivedCurrentPath))
                return;

            if (File.Exists(vaultPath))
                File.Replace(archivedCurrentPath, vaultPath, backupPath, ignoreMetadataErrors: true);
            else
                File.Move(archivedCurrentPath, vaultPath);
        }
        catch
        {
            // Preserve all remaining files for manual recovery rather than deleting evidence.
        }
    }
}
