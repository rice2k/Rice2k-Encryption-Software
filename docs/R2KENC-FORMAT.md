# R2KENC v1 Development Format

> Status: development format. It may change before Rice2k Encryption Software 1.0.

## Purpose

`.r2kenc` is the Rice2k encrypted-file container. Version 1 is designed for authenticated, chunked file encryption so very large files do not need to be loaded completely into memory.

## Cryptographic profile

- Encryption: XChaCha20-Poly1305
- Password KDF: Argon2id
- Derived key length: 32 bytes
- Salt: 16 bytes
- Nonce: 24 bytes per metadata/chunk record
- Default chunk size: 4 MiB
- Current Argon2id operations limit: 4
- Current Argon2id memory limit: 64 MiB

## Binary layout

All integer fields are currently written using .NET `BinaryWriter` little-endian representation.

```text
8 bytes   Magic: ASCII "R2KENC01"
1 byte    Format version
1 byte    Algorithm identifier
1 byte    KDF identifier
8 bytes   Argon2id operations limit (Int64)
4 bytes   Argon2id memory limit (Int32)
4 bytes   Chunk size (Int32)
16 bytes  Salt
24 bytes  Encrypted-metadata nonce
4 bytes   Encrypted-metadata ciphertext length
N bytes   Encrypted metadata ciphertext + Poly1305 tag

Repeated encrypted chunk records until EOF:
8 bytes   Chunk index (Int64)
4 bytes   Ciphertext length (Int32)
24 bytes  Chunk nonce
N bytes   Chunk ciphertext + Poly1305 tag
```

## Encrypted metadata

The metadata plaintext is serialized JSON containing values such as:

```json
{
  "OriginalName": "example.zip",
  "OriginalLength": 123456789,
  "ChunkCount": 30,
  "ChunkSize": 4194304,
  "CreatedUtc": "2026-09-23T14:00:00+00:00"
}
```

The metadata is encrypted and authenticated. The public header values required for password-derived key creation remain outside the encrypted metadata.

During parsing, Rice2k also checks that the authenticated metadata is internally consistent:

- `OriginalName` must be present;
- lengths and chunk counts cannot be negative;
- metadata `ChunkSize` must match the authenticated public header;
- `ChunkCount` must match the count implied by `OriginalLength` and `ChunkSize`.

## Associated data

Metadata authentication binds these public header values:

- magic;
- format version;
- algorithm ID;
- KDF ID;
- Argon2id operation limit;
- Argon2id memory limit;
- chunk size;
- salt.

Each chunk authenticates:

- SHA-256 of the above header-authentication data;
- its sequential chunk index.

This is intended to detect accidental or malicious modification, chunk reordering, duplication, or transplantation.

## Defensive parser limits

The KDF and chunk parameters are stored in the public header because they must be known before deriving the key. A malformed file therefore must not be allowed to request unlimited CPU or memory before authentication occurs.

The current development parser rejects values outside these bounds before running Argon2id or allocating a chunk buffer:

| Field | Accepted development range |
|---|---:|
| Argon2id operations limit | 3–10 |
| Argon2id memory limit | 8–256 MiB |
| Chunk size | 64 KiB–64 MiB |
| Encrypted metadata ciphertext | 16 bytes–64 KiB |

These are parser safety limits, not a promise that every future `.r2kenc` version will use the same settings. A future format version can define new bounds explicitly.

## Completion checks

After decryption, Rice2k checks that:

- every chunk index was sequential;
- decoded chunk count matches encrypted metadata;
- decoded total length matches encrypted metadata;
- every XChaCha20-Poly1305 authentication check passed.

A wrong password, modified metadata, modified chunk, missing/reordered chunk, truncation, or inconsistent authenticated metadata causes decryption to fail rather than finalize a plaintext output.

## Temporary-output behavior

Encryption and decryption write to a unique temporary file in the **same destination directory**. The filename is randomized and ends in `.partial`.

This provides two practical properties:

1. two Rice2k processes targeting similar output names do not intentionally share a deterministic temporary filename;
2. finalization is a same-directory rename/move rather than a cross-volume copy.

On failure or cancellation, Rice2k makes a best-effort attempt to delete only its incomplete temporary output. It never deletes the source file as part of this workflow.

## Versioning

Parsers must reject unsupported versions rather than guessing how to interpret a container. If a future format changes security parameters or layout, it should receive a new version identifier and maintain explicit compatibility logic.
