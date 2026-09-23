# R2KENC Development Formats

> Status: development formats. They may change before Rice2k Encryption Software 1.0.

## Purpose

`.r2kenc` is the Rice2k encrypted-file container. Rice2k currently has two explicitly distinguishable development variants:

- **v1 / `R2KENC01`** — password-only authenticated encryption;
- **v2 / `R2KENC02`** — password + encrypted Rice2k key-package protection.

Both variants are chunked so very large files do not need to be loaded completely into memory. Existing v1 files remain supported when v2 key-file protection is enabled in the application.

## Shared cryptographic profile

- Encryption: XChaCha20-Poly1305
- Password KDF: Argon2id
- Content key length: 32 bytes
- Salt: 16 bytes
- Nonce: 24 bytes per metadata/chunk record
- Default chunk size: 4 MiB
- Current Argon2id operations limit: 4
- Current Argon2id memory limit: 64 MiB

All integer fields are currently written using .NET `BinaryWriter` little-endian representation.

---

## v1 — Password-only container

### Binary layout

```text
8 bytes   Magic: ASCII "R2KENC01"
1 byte    Format version = 1
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

The 32-byte content key is derived directly from the user's password with Argon2id using the stored random salt and authenticated KDF parameters.

### v1 encrypted metadata

```json
{
  "OriginalName": "example.zip",
  "OriginalLength": 123456789,
  "ChunkCount": 30,
  "ChunkSize": 4194304,
  "CreatedUtc": "2026-09-23T14:00:00+00:00"
}
```

---

## v2 — Password + Rice2k key-file container

v2 is used when the user selects **Password + Rice2k key file**. The user must later provide:

1. the encrypted file password;
2. the matching `.r2kkey` package;
3. the password that unlocks that `.r2kkey` package.

The `.r2kkey` file remains separately encrypted at rest. Rice2k authenticates and opens it in memory for the operation; raw key bytes are not written into the `.r2kenc` header.

### Binary layout

```text
8 bytes   Magic: ASCII "R2KENC02"
1 byte    Format version = 2
1 byte    Algorithm identifier
1 byte    KDF identifier
1 byte    Protection mode = 2 (password + key file)
8 bytes   Argon2id operations limit (Int64)
4 bytes   Argon2id memory limit (Int32)
4 bytes   Chunk size (Int32)
16 bytes  Salt
1 byte    Key fingerprint length
N bytes   ASCII key fingerprint (non-secret hint)
24 bytes  Encrypted-metadata nonce
4 bytes   Encrypted-metadata ciphertext length
N bytes   Encrypted metadata ciphertext + Poly1305 tag

Repeated encrypted chunk records until EOF:
8 bytes   Chunk index (Int64)
4 bytes   Ciphertext length (Int32)
24 bytes  Chunk nonce
N bytes   Chunk ciphertext + Poly1305 tag
```

### v2 key combination

Rice2k first derives a 32-byte password key with Argon2id. It then combines that password-derived key with the 256-bit secret from the unlocked `.r2kkey` package using HMAC-SHA-256 with an explicit Rice2k domain-separation context.

Conceptually:

```text
passwordKey = Argon2id(password, salt, stored KDF parameters)
contentKey  = HMAC-SHA-256(
                key = passwordKey,
                message = "Rice2k-R2KENC-KeyFile-v1" || keyFileSecret)
```

The intermediate password-derived key, copied key-file secret, and final content key are cleared from managed buffers on a best-effort basis after use.

The file therefore does not decrypt with only the password or only the key file.

### Key fingerprint

The public v2 header contains the safe Rice2k fingerprint of the required key, for example:

```text
R2K-61F4-92AB-80C3-142D-19FE
```

The fingerprint is an identifier, **not secret key material**. It lets the interface tell the user which key package is expected before expensive decryption begins.

Because the public header is not trusted until authenticated metadata is opened, this fingerprint is only a selection hint. The same fingerprint is included inside authenticated encrypted metadata and must match before decryption is accepted.

### v2 encrypted metadata

```json
{
  "OriginalName": "example.zip",
  "OriginalLength": 123456789,
  "ChunkCount": 30,
  "ChunkSize": 4194304,
  "CreatedUtc": "2026-09-23T14:00:00+00:00",
  "RequiredKeyFingerprint": "R2K-61F4-92AB-80C3-142D-19FE"
}
```

---

## Associated data

### v1

Metadata authentication binds:

- magic;
- format version;
- algorithm ID;
- KDF ID;
- Argon2id operation limit;
- Argon2id memory limit;
- chunk size;
- salt.

### v2

Metadata authentication additionally binds:

- protection-mode identifier;
- key-fingerprint length;
- key-fingerprint bytes.

Each encrypted chunk authenticates:

- SHA-256 of that format's header-authentication data;
- its sequential chunk index.

This is intended to detect modification, chunk reordering, duplication, or transplantation.

## Authenticated metadata checks

During parsing Rice2k checks that authenticated metadata is internally consistent:

- `OriginalName` must be present;
- lengths and chunk counts cannot be negative;
- metadata `ChunkSize` must match the authenticated public header;
- `ChunkCount` must match the count implied by `OriginalLength` and `ChunkSize`;
- for v2, `RequiredKeyFingerprint` must match the fingerprint bound into the authenticated header data.

## Defensive parser limits

KDF and chunk parameters must be available before key derivation, so malformed input must not be allowed to request unlimited resources before authentication.

| Field | Accepted development range |
|---|---:|
| Argon2id operations limit | 3–10 |
| Argon2id memory limit | 8–256 MiB |
| Chunk size | 64 KiB–64 MiB |
| Encrypted metadata ciphertext | 16 bytes–64 KiB |
| v2 fingerprint bytes | 1–64 bytes |

These are parser safety limits, not a promise that every future `.r2kenc` version will use identical settings.

## Completion checks

After decryption Rice2k checks that:

- every chunk index was sequential;
- decoded chunk count matches encrypted metadata;
- decoded total length matches encrypted metadata;
- every XChaCha20-Poly1305 authentication check passed;
- v2 containers were opened with a key matching the authenticated key fingerprint.

A wrong password, wrong key, wrong key-package password, modified metadata, modified chunk, missing/reordered chunk, truncation, or inconsistent authenticated metadata causes decryption to fail rather than finalize plaintext output.

## Temporary-output behavior

Encryption and decryption write to a unique temporary file in the **same destination directory**. The filename is randomized and ends in `.partial`.

This means parallel Rice2k operations do not intentionally share a deterministic temporary filename and finalization is a same-directory rename/move rather than a cross-volume copy.

On failure or cancellation, Rice2k makes a best-effort attempt to delete only its incomplete temporary output. It never deletes the source file as part of this workflow.

## Versioning and compatibility

Parsers must reject unsupported formats rather than guessing how to interpret a container.

Rice2k currently distinguishes the variants at the magic field:

```text
R2KENC01  password-only v1
R2KENC02  password + key-file v2
```

The application should keep explicit compatibility logic as future versions are introduced. Existing v1 data must not be silently rewritten merely because a newer protection mode exists.
