namespace Rice2k.Encryption.Services;

internal enum VaultMutationCheckpoint
{
    PendingVaultVerified,
    ActiveVaultReplaced,
    FinalVaultVerified
}

public sealed partial class SecureVaultService
{
    internal Action<VaultMutationCheckpoint>? MutationCheckpointForTesting { get; set; }

    private void HitVaultMutationCheckpoint(VaultMutationCheckpoint checkpoint) =>
        MutationCheckpointForTesting?.Invoke(checkpoint);
}
