namespace Rice2k.Encryption.Models;

public sealed record FolderProtectionFile(
    string SourcePath,
    string RelativePath,
    long Length);

public sealed record FolderProtectionPlan(
    string SourceFolder,
    string DestinationPath,
    string VaultRootName,
    IReadOnlyList<FolderProtectionFile> Files,
    long TotalBytes,
    int SkippedFiles,
    int SkippedFolders);
