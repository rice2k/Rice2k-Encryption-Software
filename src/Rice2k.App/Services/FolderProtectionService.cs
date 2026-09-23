using Rice2k.Encryption.Models;

namespace Rice2k.Encryption.Services;

public sealed class FolderProtectionService
{
    private readonly SecureVaultService _vaultService = new();

    public Task<FolderProtectionPlan> PrepareAsync(
        string sourceFolder,
        string destinationPath,
        IProgress<VaultOperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var source = ValidateSource(sourceFolder);
        var destination = ValidateDestination(source, destinationPath);

        return Task.Run(
            () => Scan(source, destination, progress, cancellationToken),
            cancellationToken);
    }

    public async Task ProtectAsync(
        FolderProtectionPlan plan,
        string password,
        IProgress<VaultOperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Files.Count == 0)
            throw new InvalidOperationException("The selected folder does not contain any regular files that Rice2k can protect.");

        SecureVaultSession? session = null;
        try
        {
            session = await _vaultService.CreateAsync(plan.DestinationPath, password, cancellationToken);
            var files = plan.Files
                .Select(file => (
                    SourcePath: file.SourcePath,
                    VaultPath: $"{plan.VaultRootName}/{file.RelativePath}"))
                .ToArray();

            await _vaultService.AddFilesWithProgressAsync(
                session,
                files,
                progress,
                cancellationToken);
        }
        catch
        {
            var canRemoveEmptyOutput = session is not null &&
                                       session.Sequence == 0 &&
                                       session.Entries.Count == 0 &&
                                       !SecureVaultService.HasRecoveryBackup(plan.DestinationPath);
            session?.Dispose();
            session = null;

            if (canRemoveEmptyOutput)
                TryDeleteEmptyOutput(plan.DestinationPath);
            throw;
        }
        finally
        {
            session?.Dispose();
        }
    }

    private static FolderProtectionPlan Scan(
        string source,
        string destination,
        IProgress<VaultOperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        var root = new DirectoryInfo(source);
        var pending = new Stack<DirectoryInfo>();
        var files = new List<FolderProtectionFile>();
        var skippedFiles = 0;
        var skippedFolders = 0;
        long totalBytes = 0;
        pending.Push(root);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = pending.Pop();

            try
            {
                if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    skippedFolders++;
                    continue;
                }

                try
                {
                    foreach (var file in current.EnumerateFiles())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        try
                        {
                            if ((file.Attributes & FileAttributes.ReparsePoint) != 0)
                            {
                                skippedFiles++;
                                continue;
                            }

                            var relative = Path.GetRelativePath(root.FullName, file.FullName).Replace('\\', '/');
                            files.Add(new FolderProtectionFile(file.FullName, relative, file.Length));
                            totalBytes = checked(totalBytes + file.Length);
                            progress?.Report(new VaultOperationProgress(
                                "Scanning folder",
                                0,
                                0,
                                files.Count,
                                0,
                                relative));
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        {
                            skippedFiles++;
                        }
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    skippedFolders++;
                }

                try
                {
                    foreach (var child in current.EnumerateDirectories())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        try
                        {
                            if ((child.Attributes & FileAttributes.ReparsePoint) != 0)
                                skippedFolders++;
                            else
                                pending.Push(child);
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        {
                            skippedFolders++;
                        }
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    skippedFolders++;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                skippedFolders++;
            }
        }

        if (files.Count == 0)
            throw new InvalidOperationException("The selected folder does not contain any regular files that Rice2k can protect.");

        CheckFreeSpace(destination, totalBytes);

        var rootName = string.IsNullOrWhiteSpace(root.Name) ? "Protected Folder" : root.Name;
        rootName = SecureVaultService.NormalizeVaultPath(rootName);
        return new FolderProtectionPlan(
            source,
            destination,
            rootName,
            files,
            totalBytes,
            skippedFiles,
            skippedFolders);
    }

    private static string ValidateSource(string sourceFolder)
    {
        if (string.IsNullOrWhiteSpace(sourceFolder) || !Directory.Exists(sourceFolder))
            throw new DirectoryNotFoundException("Choose an existing folder to protect.");
        return Path.GetFullPath(sourceFolder);
    }

    private static string ValidateDestination(string sourceFolder, string destinationPath)
    {
        if (string.IsNullOrWhiteSpace(destinationPath))
            throw new ArgumentException("Choose where to save the encrypted folder container.");

        var destination = Path.GetFullPath(destinationPath);
        if (File.Exists(destination))
            throw new IOException("A file already exists at the selected destination. Rice2k will not overwrite it automatically.");

        var directory = Path.GetDirectoryName(destination);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            throw new DirectoryNotFoundException("The selected destination folder does not exist.");

        var sourcePrefix = Path.TrimEndingDirectorySeparator(sourceFolder) + Path.DirectorySeparatorChar;
        if (destination.StartsWith(sourcePrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException(
                "Choose a destination outside the folder being protected so the encrypted container cannot be scanned into itself.");
        }

        return destination;
    }

    private static void CheckFreeSpace(string destination, long plaintextBytes)
    {
        try
        {
            var root = Path.GetPathRoot(destination);
            if (string.IsNullOrWhiteSpace(root))
                return;

            var drive = new DriveInfo(root);
            if (!drive.IsReady)
                return;

            var safetyMargin = Math.Max(64L * 1024 * 1024, plaintextBytes / 10);
            var estimatedRequired = checked(plaintextBytes + safetyMargin);
            if (drive.AvailableFreeSpace < estimatedRequired)
            {
                throw new IOException(
                    $"The destination may not have enough free space. About {FormatBytes(estimatedRequired)} is estimated for this operation, but only {FormatBytes(drive.AvailableFreeSpace)} is available.");
            }
        }
        catch (OverflowException)
        {
            throw new IOException("The selected folder is too large for this development build to estimate safely.");
        }
    }

    private static void TryDeleteEmptyOutput(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best effort only. Source files are never deleted by this cleanup.
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = Math.Max(0, bytes);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.##} {units[unit]}";
    }
}
