# Changelog

All notable changes to Rice2k Encryption Software will be documented here.

## [Unreleased]

### Added

- Project security policy and contribution guidelines.
- Product roadmap, build guide, UX specification, file-format documentation, and security design documentation.
- Initial Windows desktop application foundation.
- Versioned `.r2kenc` encrypted-file format.
- XChaCha20-Poly1305 + Argon2id cryptographic foundation.
- Beginner-first UI direction with detailed operation status and progress.
- Three-step Encrypt workflow: choose file, choose protection, review and run.
- Three-step Decrypt workflow: choose container, unlock/output selection, review and run.
- Drag-and-drop routing for normal files and `.r2kenc` files.
- Password confirmation and locally generated strong-password option for file encryption.
- Dedicated operation screens with current stage, percentage, bytes processed, speed, elapsed time, and estimated time remaining.
- Dedicated completion screens with output path and Open Folder actions.
- Preflight checks for source readability, destination writability, source/destination conflicts, destination availability, and free disk space when available.
- Automatic non-overwriting Keep Both naming plus late-collision user choices.
- User-facing Batch Queue accessible from the sidebar and Command Center.
- Multi-select and drag-and-drop batch file intake.
- Folder intake from Add Folder and drag-and-drop, scanned off the UI thread.
- Recursive folder enumeration that skips inaccessible and reparse/junction directories.
- Per-file batch status/progress plus overall queue progress.
- Sequential batch encryption using the same authenticated `.r2kenc` engine.
- Batch failure isolation: one failed item does not discard successful outputs or stop later items.
- Safe batch cancellation that keeps completed outputs and cleans the active temporary file.
- Pause/resume support for single-file and batch encryption/decryption at safe chunk boundaries while keeping cancellation responsive.
- Five-page first-run welcome tour explaining workflows, source-file safety, passwords, batches, progress, and pre-1.0 limitations.
- Non-sensitive LocalApplicationData preference storage for onboarding, helpful hints, and recovery-health status.
- Searchable Settings panel with guidance, safety, privacy, and interface-mode sections.
- Persisted Show Helpful Hints preference and contextual Encrypt/Decrypt tips.
- Replay Welcome Tour action in Settings.
- Keyboard shortcuts and screen-reader metadata for primary workflows.
- Friendly error-dialog component with collapsed copyable technical details.
- Global unexpected-UI-error handling that reports technical details and exits rather than continuing in an unknown state.
- **Key Manager** for generating in-memory 256-bit symmetric keys with human-readable fingerprints.
- Password-protected authenticated `.r2kkey` export and import.
- Duplicate-key detection by key identity/fingerprint within a Key Manager session.
- **Password + Rice2k key-file protection** in the normal Encrypt/Decrypt workflow.
- `R2KENC02` v2 containers requiring both a file password and the matching 256-bit key from an encrypted `.r2kkey` package.
- Automatic v2 detection in the Decrypt workflow with the required non-secret key fingerprint shown to the user.
- Password-derived and key-file secret combination using Argon2id plus domain-separated HMAC-SHA-256 before XChaCha20-Poly1305 content encryption.
- Pause/resume, cancellation cleanup, output verification, and progress reporting for password + key-file operations.
- **Recovery Center** for creating separately password-protected `.r2krecovery` packages from `.r2kkey` packages.
- Test Recovery workflow that authenticates/decrypts in memory, validates the 256-bit key, displays only the safe fingerprint, and then clears recovered key bytes.
- Restore workflow that creates a fresh password-protected `.r2kkey` from a valid recovery package.
- Command Center recovery-health status based on the last successful local recovery test.
- **Secure Vault v0.4 preview** with versioned `.r2kvault` containers.
- Encrypted vault manifest containing filenames, relative paths, file sizes, timestamps, vault sequence, and entry mapping.
- Authenticated chunked vault file contents using XChaCha20-Poly1305.
- Vault create, unlock, explicit lock, full verification, and extraction workflows.
- Vault Browser with add files, add folder, search, extract, rename, remove, verify, and lock actions.
- Recursive folder intake preserving relative paths while skipping inaccessible and reparse/junction folders.
- 10-minute inactivity auto-lock option in the Vault Browser.
- Authenticated vault sequence numbers used to reject stale/concurrent mutation finalization.
- Atomic vault mutation pipeline: verify current → build pending → verify pending → replace with backup → verify final.
- Preserved `<vault>.backup` recovery copy during the final atomic replace step.
- Recovery-backup controls to verify, restore, or move a valid backup aside.
- Recovery restore preserves the newer active vault as a separate pre-recovery backup rather than deleting it.
- Development format documentation for `.r2kenc`, `.r2kkey`, `.r2krecovery`, and `.r2kvault`.
- xUnit v3 security/regression test project included in the solution.
- Key/recovery tests covering package round trips, wrong passwords, tampering, recovery validation, and restored fingerprint preservation.
- Password + key-file tests covering round trips, wrong-key rejection, wrong-password rejection, tampering, cancellation cleanup, fingerprint inspection, and v1 compatibility detection.
- Vault tests covering create/unlock, add/extract, rename/remove, wrong-password rejection, tamper detection, duplicate paths, overwrite prevention, backup verification, backup restoration, and safe backup preservation.

### Changed

- Windows application development version advanced to `0.4.0-preview.1`.
- Encrypt/Decrypt screens hide cryptographic details behind recommended defaults instead of exposing them as required choices.
- Encrypt offers **Password only** and **Password + Rice2k key file** without changing existing password-only files.
- Decrypt automatically distinguishes password-only `R2KENC01` containers from password + key-file `R2KENC02` containers.
- Operation failures return users to a recoverable workflow step with plain-English guidance.
- Dropping multiple normal files or a folder onto the main window routes them to the Batch Queue.
- Passwords & Keys exposes the operational Key Manager instead of only the password generator.
- Recovery Center exposes package creation, testing, and restore workflows instead of a placeholder card.
- Secure Vault now opens an operational Vault Browser instead of a placeholder card.
- GitHub validation workflow builds and runs the security test project when manually dispatched and a hosted runner is available.

### Security

- Original files are preserved by default.
- Encrypted file writes use unique randomized same-directory temporary outputs before finalization.
- Authentication failure causes decryption to fail closed.
- Existing destination files are never overwritten automatically.
- Cancelled encryption/decryption removes incomplete temporary output.
- Encryption requires password confirmation before an operation can begin.
- Batch encryption performs preflight checks per item and preserves completed outputs if later items fail or are cancelled.
- `.r2kenc` parsers cap unauthenticated KDF, chunk-size, metadata-length, and v2 fingerprint-length values before expensive resource use.
- Authenticated metadata is validated for internal consistency before decryption continues.
- v2 key-file containers bind the required key fingerprint into authenticated metadata/header associated data.
- The public v2 fingerprint is treated only as a selection hint until authenticated metadata is successfully opened.
- Password + key-file encryption fails closed unless both the password-derived key and matching key-file secret are present.
- Text tokens validate size, salt, nonce, and ciphertext structure before decryption.
- `.r2kkey` and `.r2krecovery` package parsers bound KDF and ciphertext parameters before expensive work.
- Key/recovery packages authenticate both encrypted payloads and security-relevant header parameters.
- Raw symmetric key bytes are not displayed in the normal user interface.
- In-memory managed keys are cleared when removed or when the Key Manager closes.
- Workflow copies of key-file secrets and derived keys are cleared on a best-effort basis after use.
- Recovery tests clear the temporary recovered key after fingerprint verification.
- Vault passwords are not persisted by the vault service; unlocked sessions retain only a derived content-key copy until lock/disposal.
- Vault manifests keep filenames and metadata encrypted at rest.
- Vault chunk authentication binds the stable vault header, random entry ID, and sequential chunk index.
- Vault mutations fully authenticate the current state before rewriting and fully authenticate pending/final states before releasing recovery data.
- A pre-existing vault recovery backup blocks later mutation so possible recovery data is not silently overwritten.
- Recovery-backup restore authenticates the candidate first and preserves the current active vault as a separate recovery artifact.
- Security tests cover round trips, wrong passwords, wrong keys, tampering, truncation, resource-limit rejection, overwrite protection, password policy, cancellation cleanup, key packages, key-file containers, recovery packages, and vault lifecycle/recovery behavior.
