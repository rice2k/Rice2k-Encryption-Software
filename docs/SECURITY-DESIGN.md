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

File and text encryption enforce a 12-character minimum at the service boundary; the UI recommends longer generated passwords. Decryption does not impose the same length minimum so future or legacy compatible data is not rejected solely because its original password was shorter.

### Encrypted metadata

The current format encrypts metadata such as original filename, original length, chunk count, creation time, and chunk size. The password/KDF parameters and random salt remain in the public header because they are needed to derive the decryption key.

After metadata authentication succeeds, Rice2k checks internal consistency between the metadata and authenticated header, including chunk size and the chunk count implied by original length.

## Safe file workflow

Encryption follows this pattern:

```text
Read original (never modify it)
        ↓
Run preflight checks
        ↓
Create unique randomized .partial file in destination directory
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

Temporary filenames are randomized rather than deterministic. This reduces interference between separate Rice2k processes or concurrent operations that happen to target similar names.

If encryption, verification, or cancellation fails, Rice2k attempts to remove only the incomplete `.partial` output. It does not delete the source file.

Decryption uses the same temporary-output approach and finalizes only after authentication and length/chunk-count checks pass.

The UI also generates non-colliding output names by default. The crypto service independently refuses to overwrite an existing destination, so overwrite protection does not depend solely on the UI.

## Format validation and resource limits

The parser rejects:

- incorrect magic/version values;
- unsupported algorithm/KDF identifiers;
- unreasonable Argon2id limits;
- unreasonable chunk sizes;
- malformed or truncated headers;
- truncated metadata or nonce records;
- impossible ciphertext lengths;
- inconsistent authenticated metadata;
- missing, duplicated, or reordered chunk indexes;
- authentication failures;
- unexpected final plaintext length or chunk count.

Because Argon2 and chunk parameters are read before password authentication, the parser applies explicit limits **before** performing expensive work. The current development limits cap Argon2 operations at 10, Argon2 memory at 256 MiB, chunk size at 64 MiB, and metadata ciphertext at 64 KiB. This prevents a malformed container from simply requesting unbounded resources through its public header.

## Batch-processing safety

The Batch Queue processes files sequentially in the current development build. Each item gets its own destination, preflight check, temporary file, authentication/verification cycle, and finalization step.

A failure on one file is isolated to that queue item. Rice2k continues with later files, and already completed encrypted outputs remain intact. Cancelling a batch stops the active item through the same cancellation path used for single-file encryption; successfully completed items are retained.

## Automated regression/security tests

The repository includes an xUnit v3 project covering current high-risk behaviors, including:

- text and file round trips;
- fresh-randomness behavior for text encryption;
- wrong-password failures;
- modified ciphertext rejection;
- malformed text-token fields;
- empty and multi-chunk files;
- source preservation;
- truncated-container rejection;
- existing-destination overwrite protection;
- service-level password policy;
- cancelled-operation temporary cleanup.

Additional fuzzing, interrupted-write simulation, header mutation, concurrency, and very-large-file tests remain release gates before 1.0.

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
