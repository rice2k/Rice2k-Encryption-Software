# Security Design

## Design goals

Rice2k Encryption Software is designed around four primary goals:

1. **Confidentiality** — encrypted content should not be readable without the correct secret.
2. **Integrity/authentication** — modified or corrupted encrypted content must fail authentication rather than produce silently corrupted plaintext.
3. **Data-loss resistance** — the application must avoid destructive default behavior and should finalize outputs only after successful processing.
4. **Usability** — safe behavior should be the easiest behavior for a normal user to choose.

## Current cryptographic foundation

### Authenticated encryption

The current `.r2kenc` and vault implementations use **XChaCha20-Poly1305** through Sodium.Core/libsodium-compatible APIs.

Each metadata block and file chunk uses a fresh 24-byte nonce. File/vault chunks authenticate security-relevant container context plus a sequential chunk index so chunks cannot be silently reordered or transplanted within a container.

### Password-based key derivation

Passwords are converted to 256-bit keys with **Argon2id** and random salts.

The primary development profile is:

- Argon2id
- operations limit: 4
- memory limit: 64 MiB
- output key: 256 bits

File/text creation and newly protected key/identity/app-lock workflows enforce a 12-character minimum where applicable. Decryption/opening does not invent a new minimum that would reject an already-created compatible container solely because its historical password was shorter.

### Encrypted metadata

Encrypted containers keep sensitive metadata such as original filenames, original lengths, vault paths, timestamps, and chunk mapping inside authenticated ciphertext where the format permits. Public KDF parameters, random salts, format identifiers, and unavoidable structural information remain outside encryption when they are required to derive keys or parse a container safely.

After metadata authentication succeeds, Rice2k checks internal consistency between authenticated metadata and the surrounding container structure before restoring plaintext.

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

Parsers reject or bound, as appropriate:

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

Because Argon2 and chunk parameters may be read before password authentication, parsers apply explicit limits **before** performing expensive work. Current development limits vary by format but intentionally cap Argon2 operations/memory, chunk sizes, metadata lengths, recipient counts, and other unauthenticated allocation inputs.

## Vault mutation and recovery model

Vault mutations authenticate the currently unlocked state before rewriting. Changes are built into a same-directory pending vault, the pending state is fully authenticated, the active vault is replaced while retaining a recovery backup, and the finalized active vault is authenticated again before the recovery backup is released.

Sequence numbers reject stale/concurrent mutation finalization. Fault-injection checkpoints and recovery tests cover interruption around pending verification and active replacement. Stale `.pending` artifacts are treated as recovery candidates only after cryptographic verification against the unlocked vault identity and sequence state.

## Identity, recipient encryption, and signatures

Rice2k v0.5 introduces separate encryption and signing key pairs:

- private identities are protected in authenticated `.r2kid` packages;
- public `.r2kpub` cards are self-signed and fingerprinted;
- `R2KENC03` wraps a fresh content key independently for each recipient using libsodium sealed boxes;
- file contents remain chunked XChaCha20-Poly1305 ciphertext;
- `.r2ksig` detached signatures use Ed25519 and bind a SHA-512 file hash plus signed metadata.

A valid `.r2kpub` self-signature proves consistency/authenticity relative to the embedded signing key. It does **not** establish that a display-name label belongs to a particular real-world person. Fingerprints must be compared through an independent trusted channel when real-world identity matters.

Recipient encryption establishes confidentiality to the selected public keys. It does not authenticate the sender. Signatures provide authenticity/integrity relative to the signing key. Users who need both properties should use both mechanisms.

## Batch-processing safety

The Batch Queue processes files sequentially in the current development build. Each item gets its own destination, preflight check, temporary file, authentication/verification cycle, and finalization step.

A failure on one file is isolated to that queue item. Rice2k continues with later files, and already completed encrypted outputs remain intact. Cancelling a batch stops the active item through the same cancellation path used for single-file encryption; successfully completed items are retained.

## Privacy Mode and protected clipboard

Privacy Mode is an interface-level privacy feature. It can hide/clear in-memory Activity entries and clear transient text/password/generated-password previews.

Protected clipboard operations use an app-wide generation counter and exact-value comparison. A timer clears a value only when:

1. the timer still represents the newest Rice2k clipboard copy; and
2. the Windows clipboard still contains exactly that copied value.

This avoids intentionally erasing a newer value the user copied afterward in another application. Clipboard access remains best-effort because another Windows process may temporarily own the clipboard.

## Authenticated App Lock

App Lock is a **local application privacy barrier**, separate from file/vault encryption.

### Credential storage

Rice2k does not store the App Lock password. It stores a versioned local credential record containing:

- random 16-byte salt;
- Argon2id operation/memory parameters;
- a 256-bit verifier derived from the Argon2id output with domain-separated HMAC-SHA-256;
- format/version metadata.

The current App Lock profile uses the same 4-operation / 64 MiB Argon2id development cost and requires a password of at least 12 characters. Verification uses fixed-time comparison. KDF parameters are bounded before expensive work.

The credential is stored separately from ordinary non-secret UI preferences. Changing/removing App Lock requires the current password once a credential exists.

### Lock triggers

After App Lock is configured, startup requires authentication. Optional triggers include:

- manual **Lock Rice2k**;
- minimizing the main window;
- Windows `SessionSwitch` session-lock notification;
- configurable Rice2k-input inactivity.

Before the lock screen appears, Rice2k clears transient sensitive previews and its tracked clipboard value. Existing application windows are visually blanked and removed from taskbar presentation while the modal lock window owns application input, then restored after successful authentication. This avoids ending an already-running modal dialog merely to hide it.

### App Lock security boundary

App Lock does **not** encrypt already-unlocked process memory and is not a substitute for Windows sign-in controls, BitLocker/full-disk encryption, or the independent keys/passwords protecting Rice2k containers.

An attacker or process that already controls the same Windows account and can modify/delete Rice2k's LocalApplicationData files is outside the App Lock security boundary. The project therefore does not claim App Lock protects against administrator-level compromise, malware, keyloggers, screen capture, memory inspection, or arbitrary same-account filesystem modification.

## Automated regression/security tests

The repository includes an xUnit v3 project covering current high-risk behaviors, including:

- text and file round trips;
- wrong-password/wrong-key/wrong-recipient failures;
- modified ciphertext and signature rejection;
- malformed/truncated/resource-hostile inputs;
- empty and multi-chunk files;
- source preservation and overwrite prevention;
- cancellation cleanup;
- key/recovery/identity package validation;
- vault lifecycle, recovery, progress, parser limits, and deterministic fault injection;
- recipient and multi-recipient encryption;
- detached signatures;
- public-contact validation;
- App Lock credential round trip, wrong-password rejection, removal behavior, verifier modification, and hostile KDF parameter rejection.

The source-controlled tests are not a substitute for executing them on the supported Windows/.NET 10 toolchain. Fuzzing, concurrency, very-large-file tests, accessibility review, and external security review remain release gates before 1.0.

## Logging policy

The UI's activity view must never store:

- passwords;
- derived keys;
- private keys;
- decrypted plaintext;
- encrypted-text contents;
- recovery secrets.

The current activity list is in-memory only. Any future persistent activity feature must be explicit opt-in and redact sensitive paths/data by design.

## Threat-model notes

Rice2k protects stored/transmitted encrypted data. It cannot fully protect secrets from an already-compromised computer. Malware, keyloggers, screen capture, memory inspection, malicious accessibility tools, or an attacker controlling the user's Windows session may capture passwords or plaintext while the user is actively working with them.

The application should communicate this limitation accurately rather than using claims such as “100% secure.”

## Features requiring additional review

The following remain release gates or require further design/review before 1.0:

- stable-format review for `.r2kenc`, `.r2kvault`, identity/sharing/signature, key, and recovery formats;
- optional persistent key/contact/history behavior beyond current explicit packages/public contacts;
- source-file removal/secure deletion options;
- App Lock behavior testing across Windows lock/unlock, sleep/resume, multi-monitor, scaling, and assistive-technology scenarios;
- optional vault size-hiding/padding analysis;
- signed installers and automatic updates;
- external cryptographic/security review.
