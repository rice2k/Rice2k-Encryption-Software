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

## Completion checks

After decryption, Rice2k checks that:

- every chunk index was sequential;
- decoded chunk count matches encrypted metadata;
- decoded total length matches encrypted metadata;
- every XChaCha20-Poly1305 authentication check passed.

## Versioning

Parsers must reject unsupported versions rather than guessing how to interpret a container. If a future format changes security parameters or layout, it should receive a new version identifier and maintain explicit compatibility logic.
