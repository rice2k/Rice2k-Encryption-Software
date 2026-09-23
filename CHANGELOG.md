# Changelog

All notable changes to Rice2k Encryption Software will be documented here.

## [Unreleased]

### Added

- Project security policy and contribution guidelines.
- Product roadmap and security design documentation.
- Initial Windows desktop application foundation.
- Planned `.r2kenc`, `.r2kvault`, `.r2ksig`, `.r2kkey`, and `.r2krecovery` formats.
- XChaCha20-Poly1305 + Argon2id cryptographic foundation.
- Beginner-first UI direction with detailed operation status and progress.
- Three-step Encrypt workflow: choose file, choose protection, review and run.
- Three-step Decrypt workflow: choose container, unlock/output selection, review and run.
- Drag-and-drop routing for normal files and `.r2kenc` files.
- Password confirmation and locally generated strong-password option for file encryption.
- Dedicated operation screens with current stage, percentage, bytes processed, speed, elapsed time, and estimated time remaining.
- Dedicated completion screens with output path and Open Folder actions.
- Preflight checks for source readability, source/destination conflicts, destination availability, and free disk space when available.
- Automatic non-overwriting Keep Both naming for output collisions.

### Changed

- Windows application development version advanced to `0.2.0-preview.1`.
- Encrypt/Decrypt screens now hide cryptographic details behind recommended defaults instead of exposing them as required choices.
- Operation failures return users to a recoverable workflow step with plain-English guidance.

### Security

- Original files are preserved by default.
- Encrypted file writes use temporary output before finalization.
- Authentication failure causes decryption to fail closed.
- Existing destination files are never overwritten automatically.
- Cancelled encryption/decryption removes incomplete temporary output.
- Encryption now requires password confirmation before an operation can begin.
