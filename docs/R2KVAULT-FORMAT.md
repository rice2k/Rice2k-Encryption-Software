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

## Known development limitations

Before stable 1.0 the vault still needs:

- dedicated recovery-backup UI;
- auto-lock integration;
- large-vault performance profiling;
- interruption/fault-injection tests around each atomic-replace stage;
- fuzzing of header, manifest, and record parsers;
- optional size-hiding/padding analysis;
- external security review.
