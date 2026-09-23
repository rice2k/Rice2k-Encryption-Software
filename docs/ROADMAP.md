# Rice2k Encryption Software Roadmap

This document tracks the planned implementation order. Security-sensitive features are intentionally staged so the core container format and cryptographic behavior can be tested before more complex workflows are layered on top.

## Milestone 0.1 — Secure foundation

- [x] Project identity and documentation
- [x] Windows desktop application skeleton
- [x] Dark high-tech but readable UI direction
- [x] Simple-mode navigation and Command Center
- [x] Text encryption/decryption service
- [x] Streaming file encryption/decryption service
- [x] XChaCha20-Poly1305 authenticated encryption
- [x] Argon2id password-derived keys
- [x] `.r2kenc` versioned file format
- [x] Temporary output/finalize workflow
- [x] Real-time progress model
- [x] SHA-256 integrity utility
- [x] Password/passphrase generator foundation
- [ ] Automated round-trip and corruption tests
- [ ] Signed development builds

## Milestone 0.2 — User-friendly workflows

- [ ] Three-step Encrypt wizard
- [ ] Three-step Decrypt wizard
- [ ] Drag-and-drop everywhere
- [ ] Batch queue
- [ ] Pause/resume where safely supportable
- [ ] Collision handling: keep both / replace / rename
- [ ] Preflight disk-space and permission checks
- [ ] Human-friendly error center with expandable technical details
- [ ] Detailed completion/verification screen
- [ ] First-run tour and contextual hints
- [ ] Searchable settings
- [ ] Accessibility audit: keyboard, focus, screen reader, scaling, high contrast

## Milestone 0.3 — Key and recovery tools

- [ ] Key Manager
- [ ] `.r2kkey` encrypted key export format
- [ ] Key fingerprints
- [ ] Password + key-file protection
- [ ] Recovery Center
- [ ] `.r2krecovery` recovery package
- [ ] Guided test-recovery workflow
- [ ] Recovery health/status card on Command Center

## Milestone 0.4 — Vault and folder protection

- [ ] Folder encryption
- [ ] Secure vault format `.r2kvault`
- [ ] Vault lock/unlock lifecycle
- [ ] Encrypted filenames and metadata
- [ ] Vault browser
- [ ] Vault auto-lock
- [ ] Safe interrupted-operation recovery

## Milestone 0.5 — Identity and sharing

- [ ] Public/private key identities
- [ ] Encrypt for recipient
- [ ] Multi-recipient encrypted packages
- [ ] Detached digital signatures `.r2ksig`
- [ ] Signature verification
- [ ] Trust/fingerprint comparison UX

## Milestone 0.6 — Privacy and operational polish

- [ ] Privacy Mode
- [ ] Clipboard auto-clear timer
- [ ] Lock on minimize / Windows lock
- [ ] Optional recent-file history
- [ ] Activity log with sensitive-data redaction
- [ ] Security Center
- [ ] Offline/network status indicator
- [ ] Theme and reduced-motion options
- [ ] Windows notifications

## Milestone 0.7 — Interoperability and advanced controls

- [ ] AES-256-GCM interoperability mode
- [ ] Advanced Argon2id profiles
- [ ] Import/export integrity manifests
- [ ] SHA-512 support
- [ ] Advanced format inspector
- [ ] Diagnostics with no secret material

## 1.0 release criteria

A 1.0 release should not be published until:

1. File and text round-trip tests cover supported formats.
2. Corruption, wrong-password, truncation, reordered-chunk, and malformed-header tests fail safely.
3. Large-file tests cover multi-gigabyte files without loading entire files into memory.
4. Crash/interruption tests confirm original source data is preserved.
5. Accessibility and keyboard navigation have been reviewed.
6. Release builds are signed and reproducible enough to audit.
7. The file format and cryptographic choices have had independent review.
8. Recovery documentation has been tested by someone other than the developer.
