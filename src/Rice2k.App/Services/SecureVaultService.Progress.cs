using System.Security.Cryptography;
using System.Text;
using Rice2k.Encryption.Models;
using Sodium;

namespace Rice2k.Encryption.Services;

public sealed partial class SecureVaultService
{
    public Task AddFileWithProgressAsync(
        SecureVaultSession session,
        string sourcePath,
        string vaultPath,
        IProgress<VaultOperationProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        AddFilesWithProgressAsync(session, [(sourcePath, vaultPath)], progress, cancellationToken);

    public async Task AddFilesWithProgressAsync(
        SecureVaultSession session,
        IEnumerable<(string SourcePath, string VaultPath)> files,
        IProgress<VaultOperationProgress>? progress = null,
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
            var state = await ReadAndVerifyStateWithProgressAsync(
                session.VaultPath,
                key,
                "Checking current vault",
                progress,
                cancellationToken);
            EnsureSessionMatches(session, state.Manifest);

            var occupiedPaths = new HashSet<string>(
                state.Manifest.Entries.Select(entry => entry.Path),
                StringComparer.OrdinalIgnoreCase);
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

            var finalState = await RewriteVaultWithProgressAsync(
                session,
                state,
                newManifest,
                additions,
                key,
                progress,
                cancellationToken);

            session.UpdateState(finalState.Manifest.UpdatedUtc, finalState.Manifest.Sequence, finalState.Manifest.Entries);
            ReportVaultProgress(progress, "Complete", 1, 1, requested.Length, requested.Length, null);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    public async Task RenameEntryWithProgressAsync(
        SecureVaultSession session,
        Guid entryId,
        string newVaultPath,
        IProgress<VaultOperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        var normalized = NormalizeVaultPath(newVaultPath);
        var key = session.CopyContentKey();

        try
        {
            var state = await ReadAndVerifyStateWithProgressAsync(
                session.VaultPath,
                key,
                "Checking current vault",
                progress,
                cancellationToken);
            EnsureSessionMatches(session, state.Manifest);

            var existing = state.Manifest.Entries.SingleOrDefault(entry => entry.Id == entryId)
                ?? throw new FileNotFoundException("The selected vault entry no longer exists.");
            if (state.Manifest.Entries.Any(entry =>
                    entry.Id != entryId &&
                    string.Equals(entry.Path, normalized, StringComparison.OrdinalIgnoreCase)))
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

            var finalState = await RewriteVaultWithProgressAsync(
                session,
                state,
                newManifest,
                [],
                key,
                progress,
                cancellationToken);
            session.UpdateState(finalState.Manifest.UpdatedUtc, finalState.Manifest.Sequence, finalState.Manifest.Entries);
            ReportVaultProgress(progress, "Complete", 1, 1, 1, 1, normalized);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    public async Task RemoveEntryWithProgressAsync(
        SecureVaultSession session,
        Guid entryId,
        IProgress<VaultOperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        var key = session.CopyContentKey();

        try
        {
            var state = await ReadAndVerifyStateWithProgressAsync(
                session.VaultPath,
                key,
                "Checking current vault",
                progress,
                cancellationToken);
            EnsureSessionMatches(session, state.Manifest);

            var removed = state.Manifest.Entries.SingleOrDefault(entry => entry.Id == entryId)
                ?? throw new FileNotFoundException("The selected vault entry no longer exists.");
            var entries = state.Manifest.Entries.Where(entry => entry.Id != entryId).ToList();
            var newManifest = state.Manifest with
            {
                UpdatedUtc = DateTimeOffset.UtcNow,
                Sequence = checked(state.Manifest.Sequence + 1),
                Entries = entries
            };

            var finalState = await RewriteVaultWithProgressAsync(
                session,
                state,
                newManifest,
                [],
                key,
                progress,
                cancellationToken);
            session.UpdateState(finalState.Manifest.UpdatedUtc, finalState.Manifest.Sequence, finalState.Manifest.Entries);
            ReportVaultProgress(progress, "Complete", 1, 1, 1, 1, removed.Path);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    public async Task VerifyWithProgressAsync(
        SecureVaultSession session,
        IProgress<VaultOperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        var key = session.CopyContentKey();
        try
        {
            var state = await ReadAndVerifyStateWithProgressAsync(
                session.VaultPath,
                key,
                "Verifying vault",
                progress,
                cancellationToken);
            EnsureSessionMatches(session, state.Manifest);
            ReportVaultProgress(progress, "Verification complete", 1, 1, state.Manifest.Entries.Count, state.Manifest.Entries.Count, null);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    public async Task ExtractWithProgressAsync(
        SecureVaultSession session,
        Guid entryId,
        string destinationPath,
        IProgress<VaultOperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        if (File.Exists(destinationPath))
            throw new IOException("A file already exists at the selected restore path. Rice2k will not overwrite it automatically.");

        var destinationDirectory = Path.GetDirectoryName(Path.GetFullPath(destinationPath));
        if (string.IsNullOrWhiteSpace(destinationDirectory) || !Directory.Exists(destinationDirectory))
            throw new DirectoryNotFoundException("The selected restore folder does not exist.");

        var key = session.CopyContentKey();
        var tempPath = CreateUniqueSiblingPath(Path.GetFullPath(destinationPath), ".partial");
        try
        {
            ReportVaultProgress(progress, "Reading vault manifest", 0, 0, 0, 0, null);
            var state = await ReadStateWithKeyAsync(session.VaultPath, key, verifyContents: false, cancellationToken);
            EnsureSessionMatches(session, state.Manifest);
            var entry = state.Manifest.Entries.SingleOrDefault(item => item.Id == entryId)
                ?? throw new FileNotFoundException("The selected vault entry no longer exists.");
            if (!state.Records.TryGetValue(entryId, out var record))
                throw new InvalidDataException("The vault manifest references file data that is missing.");

            await using (var output = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                DefaultChunkSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var progressOutput = new VaultProgressWriteStream(
                output,
                processed => ReportVaultProgress(progress, "Restoring file", processed, entry.Length, 0, 1, entry.Path),
                leaveOpen: true))
            {
                await ReadEntryRecordAsync(
                    session.VaultPath,
                    state.Header,
                    record,
                    entry,
                    key,
                    progressOutput,
                    cancellationToken);
                await output.FlushAsync(cancellationToken);
                output.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(destinationPath))
                throw new IOException("A file appeared at the selected restore path before Rice2k could finalize the restored file.");
            File.Move(tempPath, destinationPath);

            var completed = entry.Length == 0 ? 1 : entry.Length;
            ReportVaultProgress(progress, "Restore complete", completed, completed, 1, 1, entry.Path);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private async Task<UnlockedVaultState> ReadAndVerifyStateWithProgressAsync(
        string vaultPath,
        byte[] key,
        string stage,
        IProgress<VaultOperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        var state = await ReadStateWithKeyAsync(vaultPath, key, verifyContents: false, cancellationToken);
        var totalBytes = state.Manifest.Entries.Sum(entry => entry.Length);
        long completedBytes = 0;
        var itemIndex = 0;

        if (state.Manifest.Entries.Count == 0)
        {
            ReportVaultProgress(progress, stage, 1, 1, 0, 0, null);
            return state;
        }

        foreach (var entry in state.Manifest.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var baseBytes = completedBytes;
            await using var sink = new VaultProgressWriteStream(
                Stream.Null,
                entryProcessed => ReportVaultProgress(
                    progress,
                    stage,
                    baseBytes + entryProcessed,
                    totalBytes,
                    itemIndex,
                    state.Manifest.Entries.Count,
                    entry.Path),
                leaveOpen: true);

            await ReadEntryRecordAsync(
                vaultPath,
                state.Header,
                state.Records[entry.Id],
                entry,
                key,
                sink,
                cancellationToken);

            completedBytes += entry.Length;
            itemIndex++;
            ReportVaultProgress(
                progress,
                stage,
                completedBytes,
                totalBytes,
                itemIndex,
                state.Manifest.Entries.Count,
                entry.Path);
        }

        return state;
    }

    private async Task<UnlockedVaultState> RewriteVaultWithProgressAsync(
        SecureVaultSession session,
        UnlockedVaultState currentState,
        VaultManifest newManifest,
        IReadOnlyList<PendingVaultAddition> additions,
        byte[] key,
        IProgress<VaultOperationProgress>? progress,
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

            var retainedIds = new HashSet<Guid>(newManifest.Entries.Select(entry => entry.Id));
            var retainedEntries = currentState.Manifest.Entries.Where(entry => retainedIds.Contains(entry.Id)).ToArray();
            var totalCopyBytes = retainedEntries.Sum(entry => currentState.Records[entry.Id].TotalLength);
            var totalAdditionBytes = additions.Sum(item => item.Entry.Length);
            long copiedBytes = 0;
            long protectedBytes = 0;

            await using (var oldVault = new FileStream(
                vaultPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                DefaultChunkSize,
                FileOptions.Asynchronous | FileOptions.RandomAccess))
            await using (var output = new FileStream(
                pendingPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                DefaultChunkSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
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

                var retainedIndex = 0;
                foreach (var entry in retainedEntries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var record = currentState.Records[entry.Id];
                    var baseCopied = copiedBytes;
                    await CopyRangeWithProgressAsync(
                        oldVault,
                        output,
                        record.RecordOffset,
                        record.TotalLength,
                        current => ReportVaultProgress(
                            progress,
                            "Copying existing protected data",
                            baseCopied + current,
                            totalCopyBytes,
                            retainedIndex,
                            retainedEntries.Length,
                            entry.Path),
                        cancellationToken);
                    copiedBytes += record.TotalLength;
                    retainedIndex++;
                    ReportVaultProgress(
                        progress,
                        "Copying existing protected data",
                        copiedBytes,
                        totalCopyBytes,
                        retainedIndex,
                        retainedEntries.Length,
                        entry.Path);
                }

                var additionIndex = 0;
                foreach (var addition in additions)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var baseProtected = protectedBytes;
                    await WriteEntryRecordWithProgressAsync(
                        writer,
                        currentState.Header,
                        addition,
                        key,
                        current => ReportVaultProgress(
                            progress,
                            "Encrypting new vault data",
                            baseProtected + current,
                            totalAdditionBytes,
                            additionIndex,
                            additions.Count,
                            addition.Entry.Path),
                        cancellationToken);
                    protectedBytes += addition.Entry.Length;
                    additionIndex++;
                    ReportVaultProgress(
                        progress,
                        "Encrypting new vault data",
                        protectedBytes,
                        totalAdditionBytes,
                        additionIndex,
                        additions.Count,
                        addition.Entry.Path);
                }

                await output.FlushAsync(cancellationToken);
                output.Flush(flushToDisk: true);
            }

            _ = await ReadAndVerifyStateWithProgressAsync(
                pendingPath,
                key,
                "Verifying pending vault",
                progress,
                cancellationToken);
            HitVaultMutationCheckpoint(VaultMutationCheckpoint.PendingVaultVerified);

            var latest = await ReadStateWithKeyAsync(vaultPath, key, verifyContents: false, cancellationToken);
            EnsureSessionMatches(session, latest.Manifest);

            cancellationToken.ThrowIfCancellationRequested();
            ReportVaultProgress(progress, "Finalizing vault", 0, 1, 0, 1, Path.GetFileName(vaultPath));
            File.Replace(pendingPath, vaultPath, backupPath, ignoreMetadataErrors: true);

            try
            {
                HitVaultMutationCheckpoint(VaultMutationCheckpoint.ActiveVaultReplaced);
                var finalState = await ReadAndVerifyStateWithProgressAsync(
                    vaultPath,
                    key,
                    "Verifying finalized vault",
                    progress,
                    cancellationToken);
                if (finalState.Manifest.Sequence != newManifest.Sequence)
                    throw new InvalidDataException("The finalized vault does not contain the expected authenticated sequence number.");

                HitVaultMutationCheckpoint(VaultMutationCheckpoint.FinalVaultVerified);
                TryDelete(backupPath);
                ReportVaultProgress(progress, "Finalizing vault", 1, 1, 1, 1, Path.GetFileName(vaultPath));
                return finalState;
            }
            catch
            {
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

    private static async Task WriteEntryRecordWithProgressAsync(
        BinaryWriter writer,
        VaultHeader header,
        PendingVaultAddition addition,
        byte[] key,
        Action<long> report,
        CancellationToken cancellationToken)
    {
        var sourceInfo = new FileInfo(addition.SourcePath);
        if (sourceInfo.Length != addition.Entry.Length)
            throw new IOException($"'{sourceInfo.Name}' changed size after it was selected. Add it again so Rice2k can protect the current version.");

        var chunkCount = sourceInfo.Length == 0
            ? 0
            : (sourceInfo.Length + header.ChunkSize - 1) / header.ChunkSize;
        var perChunkOverhead = sizeof(long) + sizeof(int) + NonceSize + AuthenticationTagSize;
        var payloadLength = checked(sizeof(long) + sourceInfo.Length + checked(chunkCount * perChunkOverhead));

        writer.Write(addition.Entry.Id.ToByteArray());
        writer.Write(payloadLength);
        writer.Write(chunkCount);
        writer.Flush();

        await using var input = new FileStream(
            addition.SourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            header.ChunkSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
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
                byte[]? cipher = null;
                try
                {
                    var nonce = SecretAeadXChaCha20Poly1305.GenerateNonce();
                    cipher = SecretAeadXChaCha20Poly1305.Encrypt(
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
                    report(totalRead);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(plain);
                    if (cipher is not null)
                        CryptographicOperations.ZeroMemory(cipher);
                }
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);
        }

        if (totalRead != sourceInfo.Length || chunkIndex != chunkCount)
            throw new IOException($"'{sourceInfo.Name}' changed while Rice2k was reading it. The pending vault will be discarded.");

        if (sourceInfo.Length == 0)
            report(0);
    }

    private static async Task CopyRangeWithProgressAsync(
        FileStream source,
        FileStream destination,
        long sourceOffset,
        long length,
        Action<long> report,
        CancellationToken cancellationToken)
    {
        source.Position = sourceOffset;
        var buffer = new byte[1024 * 1024];
        long copied = 0;
        try
        {
            while (copied < length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var request = (int)Math.Min(buffer.Length, length - copied);
                var read = await source.ReadAsync(buffer.AsMemory(0, request), cancellationToken);
                if (read <= 0)
                    throw new InvalidDataException("The source vault ended while Rice2k was copying an authenticated entry record.");
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                copied += read;
                report(copied);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);
        }
    }

    private static void ReportVaultProgress(
        IProgress<VaultOperationProgress>? progress,
        string stage,
        long bytesProcessed,
        long totalBytes,
        int itemsProcessed,
        int totalItems,
        string? currentItem)
    {
        progress?.Report(new VaultOperationProgress(
            stage,
            Math.Max(0, bytesProcessed),
            Math.Max(0, totalBytes),
            Math.Max(0, itemsProcessed),
            Math.Max(0, totalItems),
            currentItem));
    }

    private sealed class VaultProgressWriteStream : Stream
    {
        private readonly Stream _inner;
        private readonly Action<long> _report;
        private readonly bool _leaveOpen;
        private long _written;

        public VaultProgressWriteStream(Stream inner, Action<long> report, bool leaveOpen)
        {
            _inner = inner;
            _report = report;
            _leaveOpen = leaveOpen;
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _written;
        public override long Position
        {
            get => _written;
            set => throw new NotSupportedException();
        }

        public override void Flush() => _inner.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
            _inner.Write(buffer, offset, count);
            _written += count;
            _report(_written);
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await _inner.WriteAsync(buffer, cancellationToken);
            _written += buffer.Length;
            _report(_written);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_leaveOpen)
                _inner.Dispose();
            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            if (!_leaveOpen)
                await _inner.DisposeAsync();
            GC.SuppressFinalize(this);
        }
    }
}
