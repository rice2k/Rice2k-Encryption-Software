# R2KVAULT v1 Development Format

> Status: development format. It may change before Rice2k Encryption Software 1.0.

## Goals

`.r2kvault` provides persistent encrypted storage with:

- encrypted filenames and file metadata;
- chunked authenticated file contents;
- a clear lock/unlock lifecycle;
- atomic mutations that keep the previous vault intact until a replacement has authenticated successfully;
- a recovery backup during the final replace step.

The current preview prioritizes correctness and recoverability over maximum mutation speed.

## Cryptographic profile

- Manifest/content encryption: XChaCha20-Poly1305
- Password KDF: Argon2id
- Content key: 256 bits
- Salt: 16 bytes
- Nonce: 24 bytes per encrypted manifest/chunk
- Default file chunk size: 4 MiB
- Current Argon2id operations limit: 4
- Current Argon2id memory limit: 64 MiB

## Public header

```text
8 bytes   Magic: ASCII "R2KVAULT"
1 byte    Format version = 1
1 byte    Algorithm identifier
1 byte    KDF identifier
8 bytes   Argon2id operations limit (Int64)
4 bytes   Argon2id memory limit (Int32)
4 bytes   File chunk size (Int32)
16 bytes  Salt
16 bytes  Random vault identifier (Guid bytes)
24 bytes  Manifest nonce
4 bytes   Encrypted manifest ciphertext length
N bytes   Encrypted manifest ciphertext + Poly1305 tag
```

The public header exposes only information needed to identify/derive/unlock the vault plus the encrypted-manifest size. Filenames, relative paths, timestamps, and exact plaintext file lengths are kept inside the authenticated encrypted manifest.

Physical encrypted entry records necessarily reveal approximate ciphertext sizes and the number of stored records. Padding/size-hiding is not implemented in this development version.

## Encrypted manifest

The authenticated JSON manifest contains:

```json
{
  "VaultId": "00000000-0000-0000-0000-000000000000",
  "CreatedUtc": "2026-09-23T16:00:00+00:00",
  "UpdatedUtc": "2026-09-23T16:10:00+00:00",
  "Sequence": 3,
  "ChunkSize": 4194304,
  "Entries": [
    {
      "Id": "00000000-0000-0000-0000-000000000000",
      "Path": "Documents/example.pdf",
      "Length": 123456,
      "ModifiedUtc": "2026-09-22T20:00:00+00:00"
    }
  ]
}
```

The manifest is the authoritative mapping between random entry IDs and user-visible filenames/paths.

## Encrypted entry records

Each physical entry record is:

```text
16 bytes  Random entry identifier (Guid bytes)
8 bytes   Record payload length (Int64)

Record payload:
8 bytes   Chunk count (Int64)

Repeated chunk records:
8 bytes   Chunk index (Int64)
4 bytes   Ciphertext length (Int32)
24 bytes  Chunk nonce
N bytes   Chunk ciphertext + Poly1305 tag
```

The entry identifier is random and does not contain the original filename. Exact plaintext length is stored only in the encrypted manifest, although ciphertext size still reveals an approximation.

## Associated data

The manifest authenticates a stable public-header profile containing:

- magic;
- format version;
- algorithm ID;
- KDF ID;
- Argon2id parameters;
- chunk size;
- salt;
- vault identifier.

Each file chunk additionally authenticates:

- SHA-256 of the stable header-authentication data;
- its random entry ID;
- its sequential chunk index.

This prevents a valid encrypted chunk from being silently moved to another entry or position.

## Lock lifecycle

Unlocking derives the vault content key from the user password and authenticates/decrypts the manifest. A `SecureVaultSession` holds a copy of that content key only while unlocked.

Locking/disposal:

- clears the session content-key buffer on a best-effort basis;
- removes the in-memory decrypted entry list from the session;
- requires a password to establish a new unlocked session.

No vault password is intentionally persisted by the vault service.

The Vault Browser supports inactivity auto-lock choices of 1, 5, 10, 15, or 30 minutes. The preference is non-secret local application configuration. Missing preference fields from older settings files fall back to auto-lock enabled at 10 minutes.

The UI blocks manual locking while a vault operation is active. A close request during a cancellable operation requests safe cancellation first rather than disposing the live session underneath the operation.

## Mutation safety model

Add/remove/rename operations follow this order:

```text
1. Fully authenticate the current vault and all retained file chunks.
2. Build a new vault in a unique same-directory .pending file.
3. Write a freshly encrypted manifest.
4. Copy unchanged authenticated ciphertext records without decrypting/re-encrypting them.
5. Encrypt newly added files in chunks.
6. Flush the pending file to disk.
7. Fully authenticate the pending vault.
8. Re-check that the live vault sequence did not change concurrently.
9. Atomically replace the live vault while retaining <vault>.backup.
10. Fully authenticate the finalized vault.
11. Remove the backup only after successful final verification.
```

If final verification fails, Rice2k attempts to restore the backup. If automatic restoration cannot complete, the `.backup` file is deliberately preserved for recovery review rather than deleted.

A pre-existing `.backup` blocks further mutation so Rice2k does not overwrite potential recovery data.

## Progress and cancellation

The v0.4 Vault Browser has a progress-aware operation path for add, extract, rename, remove, and full verification. It reports the current stage and, when a meaningful byte total is available:

- percentage;
- bytes processed and total bytes;
- approximate processing rate;
- elapsed time;
- estimated remaining time;
- current vault entry/path.

Long-running operations expose **Cancel safely**. Cancellation is checked before and between chunk/copy boundaries. Behavior depends on the stage:

- before active replacement, the unique `.pending` file is discarded where possible and the active vault is left unchanged;
- during restoration/extraction, the incomplete `.partial` output is discarded where possible and the vault is unchanged;
- after the active vault has been replaced but while final verification is still running, cancellation/failure enters the same recovery path as a verification failure and attempts to restore the retained `.backup`.

Completed authenticated source vault data is never intentionally deleted as part of cancellation.

## Recovery-backup controls

When `<vault>.backup` exists, normal vault mutations are blocked until the recovery copy is reviewed. The Vault Browser provides:

- **Verify Backup** — authenticates the backup manifest and every encrypted file chunk without modifying either copy;
- **Restore Backup** — verifies the backup, preserves the current active vault as a separate pre-recovery copy, restores the backup, then verifies the restored active vault;
- **Move Backup Aside** — verifies the backup before moving it to a user-selected non-overwriting path.

These controls are recovery aids, not a password bypass. The unlocked vault key is still required to authenticate the recovery copy.

## Sequence numbers and concurrent modification

Every authenticated manifest has a monotonically increasing `Sequence` value. An unlocked session remembers its sequence. Before a mutation is finalized, Rice2k reopens the live vault and confirms that the sequence still matches the session.

If another process changed the vault, the mutation is rejected and the user must lock/reopen the vault.

## Path rules

Vault paths are normalized relative paths using `/` separators. Rice2k rejects:

- rooted paths;
- drive/URI prefixes;
- `.` and `..` path components;
- invalid filename characters;
- duplicate paths using case-insensitive comparison.

This prevents extraction/navigation logic from treating manifest paths as arbitrary host-system paths.

## Verification

Full verification authenticates:

- the encrypted manifest;
- vault ID and chunk-size consistency;
- unique entry IDs and paths;
- exact manifest-to-record membership;
- sequential chunk indexes;
- every encrypted file chunk;
- recovered plaintext length against the authenticated manifest.

A corrupt vault fails closed rather than returning unauthenticated plaintext.

## Fault-injection coverage in source

The development service exposes internal test-only mutation checkpoints to the `Rice2k.Tests` assembly. Source-controlled tests inject failures at these boundaries:

1. after the pending vault has fully authenticated but before active replacement;
2. immediately after active replacement while the recovery backup exists;
3. after the finalized vault has fully authenticated but before the recovery backup is released.

The tests assert that the last known-good vault remains available or is restored and that the unlocked session is not advanced when the mutation does not complete.

These tests are currently **source-controlled but not yet executed in this environment** because the .NET SDK / hosted runner is unavailable. Their presence is not equivalent to a successful release-gate run.

## Known development limitations

Before stable 1.0 the vault still needs:

- successful execution of the full vault/fault-injection regression suite on the supported Windows/.NET 10 toolchain;
- large-vault and multi-gigabyte performance profiling;
- fuzzing of header, manifest, and record parsers;
- a full keyboard/screen-reader/text-scaling/high-contrast accessibility review;
- optional size-hiding/padding analysis;
- independent external security review.
