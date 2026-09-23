# Security Design

## Design goals

Rice2k Encryption Software is designed around four primary goals:

1. **Confidentiality** — encrypted content should not be readable without the correct secret.
2. **Integrity/authentication** — modified or corrupted encrypted content must fail authentication rather than produce silently corrupted plaintext.
3. **Data-loss resistance** — the application must avoid destructive default behavior and should finalize outputs only after successful processing.
4. **Usability** — safe behavior should be the easiest behavior for a normal user to choose.

## Current cryptographic foundation

### Authenticated encryption

The initial `.r2kenc` implementation uses **XChaCha20-Poly1305** through Sodium.Core/libsodium-compatible APIs.

Each metadata block and file chunk uses a fresh 24-byte nonce. File chunks also authenticate a value derived from the container header plus the chunk index so chunks cannot be silently reordered or transplanted within the same container.

### Password-based key derivation

Passwords are converted to 256-bit encryption keys with **Argon2id** and a random 16-byte per-container salt.

The initial development profile is intentionally explicit in the file header:

- Argon2id
- operations limit: 4
- memory limit: 64 MiB
- output key: 256 bits

These parameters are versioned and can be increased in later format versions while preserving compatibility.

### Encrypted metadata

The current format encrypts metadata such as original filename, original length, chunk count, creation time, and chunk size. The password/KDF parameters and random salt remain in the public header because they are needed to derive the decryption key.

## Safe file workflow

Encryption follows this pattern:

```text
Read original (never modify it)
        ↓
Create destination.partial
        ↓
Encrypt authenticated metadata
        ↓
Encrypt authenticated chunks
        ↓
Flush data to disk
        ↓
Optionally verify by decrypting/authenticating the temporary container
        ↓
Rename/finalize destination.r2kenc
```

If encryption, verification, or cancellation fails, Rice2k attempts to remove only the incomplete `.partial` output. It does not delete the source file.

Decryption uses the same temporary-output approach and finalizes only after authentication and length/chunk-count checks pass.

## Format validation

The parser rejects:

- incorrect magic/version values;
- unsupported algorithm/KDF identifiers;
- unreasonable Argon2id limits;
- unreasonable chunk sizes;
- truncated metadata or nonce records;
- impossible ciphertext lengths;
- missing, duplicated, or reordered chunk indexes;
- authentication failures;
- unexpected final plaintext length or chunk count.

## Logging policy

The UI's activity view must never store:

- passwords;
- derived keys;
- private keys;
- decrypted plaintext;
- encrypted-text contents;
- recovery secrets.

The initial activity list is in-memory only.

## Threat-model notes

Rice2k protects stored/transmitted encrypted data. It cannot fully protect secrets from an already-compromised computer. Malware, keyloggers, screen capture, memory inspection, malicious accessibility tools, or an attacker controlling the user's Windows session may capture passwords or plaintext while the user is actively working with them.

The application should communicate this limitation accurately rather than using claims such as “100% secure.”

## Features requiring additional review

The following are intentionally not marked complete until their designs and tests exist:

- persistent key storage;
- `.r2kvault` vault format;
- public-key recipient encryption;
- digital signatures;
- recovery packages;
- source-file removal/secure deletion options;
- auto-lock and Windows session integration;
- signed automatic updates.
