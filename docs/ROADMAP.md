# Rice2k Encryption Software Roadmap

This document tracks implementation status and the remaining release gates. A checked item means the feature exists in source; it does **not** mean a pre-1.0 security review or release validation has been completed.

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
- [x] SHA-256 and SHA-512 integrity utilities
- [x] Password/passphrase generator foundation
- [x] Source-controlled round-trip, wrong-password, corruption, truncation, resource-limit, and cancellation tests
- [ ] Successful full build/test run on a supported .NET 10 environment
- [ ] Signed development builds

## Milestone 0.2 — User-friendly workflows

- [x] Three-step Encrypt wizard
- [x] Three-step Decrypt wizard
- [x] Main-window drag-and-drop routing
- [x] Batch queue with multi-file and folder intake
- [x] Pause/resume at safe chunk boundaries
- [x] Safe cancellation and partial-output cleanup
- [x] Collision handling with non-overwrite defaults and user choices
- [x] Preflight disk-space, path, readability, and destination-writability checks
- [x] Plain-English unexpected-error dialog with expandable/copyable technical details
- [x] Detailed completion/verification screens
- [x] First-run tour and contextual hints
- [x] Searchable settings
- [x] Keyboard shortcuts and primary screen-reader metadata
- [ ] Route every expected/caught workflow error through the same expandable technical-details experience
- [ ] Full accessibility review: keyboard order, focus, screen reader, text scaling, high contrast, and reduced motion

## Milestone 0.3 — Key and recovery tools

- [x] Key Manager
- [x] `.r2kkey` encrypted key export/import format
- [x] Safe key fingerprints
- [x] Password + key-file protection (`R2KENC02`)
- [x] Recovery Center
- [x] `.r2krecovery` recovery package
- [x] Guided Test Recovery workflow
- [x] Restore a fresh `.r2kkey` from a verified recovery package
- [x] Recovery health/status card on Command Center
- [x] Source-controlled key/recovery/key-file regression tests
- [ ] Execute the full v0.3 regression suite on a supported build runner

## Milestone 0.4 — Vault and folder protection

- [x] Secure vault format `.r2kvault`
- [x] Vault create/unlock/lock lifecycle
- [x] Encrypted filenames, relative paths, sizes, timestamps, and manifest metadata
- [x] Authenticated chunked file contents
- [x] Vault browser and filename/path search after unlock
- [x] Add individual files
- [x] Add complete folders while preserving relative paths
- [x] Extract, rename, and remove protected entries
- [x] Full vault verification
- [x] Configurable 1/5/10/15/30-minute inactivity auto-lock with persisted preference
- [x] Atomic pending-vault mutation workflow
- [x] Verify pending vault before active replacement
- [x] Recovery `.backup` during final replacement
- [x] Verify finalized vault before releasing recovery backup
- [x] Verify / restore / move-aside recovery-backup controls
- [x] Preserve the newer active vault as a pre-recovery copy during manual backup restoration
- [x] Detailed vault operation progress: stage, percent, bytes, rate, elapsed time, ETA, and current item
- [x] Safe cancellation for progress-aware add/extract/rename/remove/verify operations
- [x] Lock/close race guards during active vault operations
- [x] Source-controlled vault fault-injection checkpoints around pending verification and atomic replacement
- [x] Source-controlled vault fault-injection and progress/cancellation tests
- [ ] Execute fault-injection tests on Windows/.NET 10
- [ ] Large-vault performance profiling and multi-gigabyte release-gate runs
- [ ] Dedicated one-click folder-container workflow outside the Vault Browser
- [ ] Evaluate optional vault size-hiding/padding without making misleading privacy claims
- [ ] Full Vault Browser accessibility review

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
- [ ] App lock on minimize / Windows lock
- [ ] Optional recent-file history
- [x] In-memory activity log with sensitive-data avoidance
- [ ] Persistent activity log with explicit opt-in and sensitive-data redaction
- [ ] Full Security Center
- [x] Basic offline/local status indicator
- [ ] Theme and reduced-motion options
- [ ] Windows notifications

## Milestone 0.7 — Interoperability and advanced controls

- [ ] AES-256-GCM interoperability mode
- [ ] Advanced Argon2id profiles
- [ ] Import/export integrity manifests
- [x] SHA-512 file integrity calculation
- [ ] Advanced format inspector
- [ ] Diagnostics with no secret material

## 1.0 release criteria

A 1.0 release should not be published until:

1. The complete solution builds cleanly on the supported Windows/.NET 10 toolchain.
2. All source-controlled unit/security/regression tests execute successfully.
3. File, text, key, recovery, key-file, and vault round-trip tests cover supported formats.
4. Corruption, wrong-password, wrong-key, truncation, malformed-header, reordered/missing-chunk, and hostile-resource-parameter tests fail safely.
5. Large-file and large-vault tests cover multi-gigabyte data without loading entire files into memory.
6. Crash/interruption and fault-injection tests confirm original source data and the last known-good vault state are preserved.
7. Accessibility and keyboard navigation have been reviewed on Windows with scaling and high-contrast scenarios.
8. Release builds are signed and the update path verifies signatures.
9. Stable file formats and cryptographic choices have had independent review.
10. Recovery documentation and workflows have been tested by someone other than the developer.
11. A stable release is not advertised as capable of recovering forgotten passwords or keys when no valid recovery material exists.
