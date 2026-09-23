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
- **Recovery Center** for creating separately password-protected `.r2krecovery` packages from `.r2kkey` packages.
- Test Recovery workflow that authenticates/decrypts in memory, validates the 256-bit key, displays only the safe fingerprint, and then clears recovered key bytes.
- Restore workflow that creates a fresh password-protected `.r2kkey` from a valid recovery package.
- Command Center recovery-health status based on the last successful local recovery test.
- Development format documentation for `.r2kkey` and `.r2krecovery`.
- xUnit v3 security/regression test project included in the solution.
- Key/recovery tests covering package round trips, wrong passwords, tampering, recovery validation, and restored fingerprint preservation.

### Changed

- Windows application development version advanced to `0.3.0-preview.1`.
- Encrypt/Decrypt screens hide cryptographic details behind recommended defaults instead of exposing them as required choices.
- Operation failures return users to a recoverable workflow step with plain-English guidance.
- Dropping multiple normal files or a folder onto the main window routes them to the Batch Queue.
- Passwords & Keys now exposes the operational Key Manager instead of only the password generator.
- Recovery Center now exposes real package creation, testing, and restore workflows instead of a placeholder card.
- GitHub validation workflow builds and runs the security test project when manually dispatched and a hosted runner is available.

### Security

- Original files are preserved by default.
- Encrypted file writes use unique randomized same-directory temporary outputs before finalization.
- Authentication failure causes decryption to fail closed.
- Existing destination files are never overwritten automatically.
- Cancelled encryption/decryption removes incomplete temporary output.
- Encryption requires password confirmation before an operation can begin.
- Batch encryption performs preflight checks per item and preserves completed outputs if later items fail or are cancelled.
- `.r2kenc` parser caps unauthenticated KDF, chunk-size, and metadata-length values before expensive resource use.
- Authenticated metadata is validated for internal consistency before decryption continues.
- Text tokens validate size, salt, nonce, and ciphertext structure before decryption.
- `.r2kkey` and `.r2krecovery` package parsers bound KDF and ciphertext parameters before expensive work.
- Key/recovery packages authenticate both encrypted payloads and security-relevant header parameters.
- Raw symmetric key bytes are not displayed in the normal user interface.
- In-memory managed keys are cleared when removed or when the Key Manager closes.
- Recovery tests clear the temporary recovered key after fingerprint verification.
- Security tests cover round trips, wrong passwords, tampering, truncation, resource-limit rejection, overwrite protection, password policy, cancellation cleanup, key packages, and recovery packages.
