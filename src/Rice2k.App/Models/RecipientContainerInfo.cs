namespace Rice2k.Encryption.Models;

public sealed record RecipientContainerInfo(
    int RecipientCount,
    int ChunkSize,
    long ContainerLength);
