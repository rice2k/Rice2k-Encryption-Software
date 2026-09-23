using System.Security.Cryptography;
using System.Text;
using Rice2k.Encryption.Models;
using Sodium;

namespace Rice2k.Encryption.Services;

public sealed partial class SecureVaultService
{
    private sealed record PendingVaultAddition(string SourcePath, VaultEntryInfo Entry);

    public Task AddFileAsync(
        SecureVaultSession session,
        string sourcePath,
        string vaultPath,
        CancellationToken cancellationToken = default) =>
        AddFilesAsync(session, [(sourcePath, vaultPath)], cancellationToken);

    public async Task AddFilesAsync(
        SecureVaultSession session,
        IEnumerable<(string SourcePath, string VaultPath)> files,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(files);

        var requested = files.ToArray();
        if (requested.Length == 0)
            return;

        var key = session.CopyContentKey();
        try
        {
            // A mutation is never based on a vault whose current encrypted file data
            // has not first authenticated successfully.
            var state = await ReadStateWithKeyAsync(session.VaultPath, key, verifyContents: true, cancellationToken);
            EnsureSessionMatches(session, state.Manifest);

            var occupiedPaths = new HashSet<string>(state.Manifest.Entries.Select(entry => entry.Path), StringComparer.OrdinalIgnoreCase);
            var additions = new List<PendingVaultAddition>(requested.Length);
            foreach (var item in requested)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!File.Exists(item.SourcePath))
                    throw new FileNotFoundException("A file selected for the vault could not be found.", item.SourcePath);

                var normalized = NormalizeVaultPath(item.VaultPath);
                if (!occupiedPaths.Add(normalized))
                    throw new IOException($"The vault already contains an entry at '{normalized}'. Choose another vault path.");

                var info = new FileInfo(item.SourcePath);
                additions.Add(new PendingVaultAddition(
                    Path.GetFullPath(item.SourcePath),
                    new VaultEntryInfo(Guid.NewGuid(), normalized, info.Length, info.LastWriteTimeUtc)));
            }

            var updatedUtc = DateTimeOffset.UtcNow;
            var newEntries = state.Manifest.Entries.Concat(additions.Select(item => item.Entry)).ToList();
            var newManifest = state.Manifest with
            {
                UpdatedUtc = updatedUtc,
                Sequence = checked(state.Manifest.Sequence + 1),
                Entries = newEntries
            };

            var finalState = await RewriteVaultAsync(session, state, newManifest, additions, key, cancellationToken);
            session.UpdateState(finalState.Manifest.UpdatedUtc, finalState.Manifest.Sequence, finalState.Manifest.Entries);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    public async Task RenameEntryAsync(
        SecureVaultSession session,
        Guid entryId,
        string newVaultPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        var normalized = NormalizeVaultPath(newVaultPath);
        var key = session.CopyContentKey();
        try
        {
            var state = await ReadStateWithKeyAsync(session.VaultPath, key, verifyContents: true, cancellationToken);
            EnsureSessionMatches(session, state.Manifest);

            var existing = state.Manifest.Entries.SingleOrDefault(entry => entry.Id == entryId)
                ?? throw new FileNotFoundException("The selected vault entry no longer exists.");
            if (state.Manifest.Entries.Any(entry => entry.Id != entryId && string.Equals(entry.Path, normalized, StringComparison.OrdinalIgnoreCase)))
                throw new IOException($"The vault already contains an entry at '{normalized}'.");

            if (string.Equals(existing.Path, normalized, StringComparison.Ordinal))
                return;

            var entries = state.Manifest.Entries
                .Select(entry => entry.Id == entryId ? entry with { Path = normalized } : entry)
                .ToList();
            var newManifest = state.Manifest with
            {
                UpdatedUtc = DateTimeOffset.UtcNow,
                Sequence = checked(state.Manifest.Sequence + 1),
                Entries = entries
            };

            var finalState = await RewriteVaultAsync(session, state, newManifest, [], key, cancellationToken);
            session.UpdateState(finalState.Manifest.UpdatedUtc, finalState.Manifest.Sequence, finalState.Manifest.Entries);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    public async Task RemoveEntryAsync(
        SecureVaultSession session,
        Guid entryId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        var key = session.CopyContentKey();
        try
        {
            var state = await ReadStateWithKeyAsync(session.VaultPath, key, verifyContents: true, cancellationToken);
            EnsureSessionMatches(session, state.Manifest);
            if (!state.Manifest.Entries.Any(entry => entry.Id == entryId))
                throw new FileNotFoundException("The selected vault entry no longer exists.");

            var entries = state.Manifest.Entries.Where(entry => entry.Id != entryId).ToList();
            var newManifest = state.Manifest with
            {
                UpdatedUtc = DateTimeOffset.UtcNow,
                Sequence = checked(state.Manifest.Sequence + 1),
                Entries = entries
            };

            var finalState = await RewriteVaultAsync(session, state, newManifest, [], key, cancellationToken);
            session.UpdateState(finalState.Manifest.UpdatedUtc, finalState.Manifest.Sequence, finalState.Manifest.Entries);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private async Task<UnlockedVaultState> RewriteVaultAsync(
        SecureVaultSession session,
        UnlockedVaultState currentState,
        VaultManifest newManifest,
        IReadOnlyList<PendingVaultAddition> additions,
        byte[] key,
        CancellationToken cancellationToken)
    {
        var vaultPath = session.VaultPath;
        var pendingPath = CreateUniqueSiblingPath(vaultPath, ".pending");
        var backupPath = RecoveryBackupPath(vaultPath);
        if (File.Exists(backupPath))
        {
            throw new IOException(
                $"Rice2k found a preserved recovery backup at '{Path.GetFileName(backupPath)}'. The vault will not be modified until that backup is reviewed or moved.");
        }

        byte[]? manifestCipher = null;
        try
        {
            var manifestNonce = SecretAeadXChaCha20Poly1305.GenerateNonce();
            manifestCipher = EncryptManifest(newManifest, manifestNonce, key, currentState.Header.HeaderAuthenticationData);

            await using (var oldVault = new FileStream(vaultPath, FileMode.Open, FileAccess.Read, FileShare.Read, DefaultChunkSize, FileOptions.Asynchronous | FileOptions.RandomAccess))
            await using (var output = new FileStream(pendingPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, DefaultChunkSize, FileOptions.Asynchronous | FileOptions.SequentialScan))
            using (var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true))
            {
                WriteHeader(
                    writer,
                    currentState.Header.OpsLimit,
                    currentState.Header.MemLimit,
                    currentState.Header.ChunkSize,
                    currentState.Header.Salt,
                    currentState.Header.VaultId,
                    manifestNonce,
                    manifestCipher);
                writer.Flush();

                var retainedIds = new HashSet<Guid>(newManifest.Entries.Select(entry => entry.Id));
                foreach (var entry in currentState.Manifest.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!retainedIds.Contains(entry.Id))
                        continue;
                    var record = currentState.Records[entry.Id];
                    await CopyRangeAsync(oldVault, output, record.RecordOffset, record.TotalLength, cancellationToken);
                }

                foreach (var addition in additions)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await WriteEntryRecordAsync(output, writer, currentState.Header, addition, key, cancellationToken);
                }

                await output.FlushAsync(cancellationToken);
                output.Flush(flushToDisk: true);
            }

            // The pending vault must fully authenticate before the current vault is touched.
            _ = await ReadStateWithKeyAsync(pendingPath, key, verifyContents: true, cancellationToken);

            // Detect a concurrent change that occurred while the pending vault was being built.
            var latest = await ReadStateWithKeyAsync(vaultPath, key, verifyContents: false, cancellationToken);
            EnsureSessionMatches(session, latest.Manifest);

            cancellationToken.ThrowIfCancellationRequested();
            File.Replace(pendingPath, vaultPath, backupPath, ignoreMetadataErrors: true);

            try
            {
                var finalState = await ReadStateWithKeyAsync(vaultPath, key, verifyContents: true, cancellationToken);
                if (finalState.Manifest.Sequence != newManifest.Sequence)
                    throw new InvalidDataException("The finalized vault does not contain the expected authenticated sequence number.");

                TryDelete(backupPath);
                return finalState;
            }
            catch
            {
                // Preserve or restore the last known-good state if final verification fails.
                TryRestoreBackup(vaultPath, backupPath);
                throw;
            }
        }
        catch
        {
            TryDelete(pendingPath);
            throw;
        }
        finally
        {
            if (manifestCipher is not null)
                CryptographicOperations.ZeroMemory(manifestCipher);
        }
    }

    private static async Task WriteEntryRecordAsync(
        Stream output,
        BinaryWriter writer,
        VaultHeader header,
        PendingVaultAddition addition,
        byte[] key,
        CancellationToken cancellationToken)
    {
        var sourceInfo = new FileInfo(addition.SourcePath);
        if (sourceInfo.Length != addition.Entry.Length)
            throw new IOException($"'{sourceInfo.Name}' changed size after it was selected. Add it again so Rice2k can protect the current version.");

        var chunkCount = sourceInfo.Length == 0 ? 0 : (sourceInfo.Length + header.ChunkSize - 1) / header.ChunkSize;
        var perChunkOverhead = sizeof(long) + sizeof(int) + NonceSize + AuthenticationTagSize;
        var payloadLength = checked(sizeof(long) + sourceInfo.Length + checked(chunkCount * perChunkOverhead));

        writer.Write(addition.Entry.Id.ToByteArray());
        writer.Write(payloadLength);
        writer.Write(chunkCount);
        writer.Flush();

        await using var input = new FileStream(addition.SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, header.ChunkSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var buffer = new byte[header.ChunkSize];
        long totalRead = 0;
        long chunkIndex = 0;
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (read == 0)
                    break;

                var plain = buffer.AsSpan(0, read).ToArray();
                try
                {
                    var nonce = SecretAeadXChaCha20Poly1305.GenerateNonce();
                    var cipher = SecretAeadXChaCha20Poly1305.Encrypt(
                        plain,
                        nonce,
                        key,
                        BuildChunkAad(header.HeaderAuthenticationData, addition.Entry.Id, chunkIndex));

                    writer.Write(chunkIndex);
                    writer.Write(cipher.Length);
                    writer.Write(nonce);
                    writer.Write(cipher);
                    writer.Flush();
                    totalRead += read;
                    chunkIndex++;
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(plain);
                }
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);
        }

        if (totalRead != sourceInfo.Length || chunkIndex != chunkCount)
            throw new IOException($"'{sourceInfo.Name}' changed while Rice2k was reading it. The pending vault will be discarded.");
    }

    private static async Task CopyRangeAsync(
        FileStream source,
        FileStream destination,
        long sourceOffset,
        long length,
        CancellationToken cancellationToken)
    {
        source.Position = sourceOffset;
        var buffer = new byte[1024 * 1024];
        try
        {
            long remaining = length;
            while (remaining > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var request = (int)Math.Min(buffer.Length, remaining);
                var read = await source.ReadAsync(buffer.AsMemory(0, request), cancellationToken);
                if (read <= 0)
                    throw new InvalidDataException("The source vault ended while Rice2k was copying an authenticated entry record.");
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                remaining -= read;
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);
        }
    }

    private static void TryRestoreBackup(string vaultPath, string backupPath)
    {
        try
        {
            if (!File.Exists(backupPath))
                return;

            if (File.Exists(vaultPath))
                File.Replace(backupPath, vaultPath, null, ignoreMetadataErrors: true);
            else
                File.Move(backupPath, vaultPath);
        }
        catch
        {
            // Deliberately leave the .backup file in place for manual/recovery-center review.
        }
    }
}
