# Rice2k Encryption Software — Stabilization Notes

Current source version: **0.6.0-preview.4**

This short status note summarizes source-level stabilization completed during the current preview. It does not replace the permanent release records in `KNOWN-ISSUES.md`, `RELEASE-READINESS.md`, `RELEASE-HISTORY.md`, or `CHANGELOG.md`.

## Current source-level hardening

- Main Encrypt/Decrypt and integrity work participates in cancellation-aware safe exit.
- App Lock credential setup/removal is tracked as a security-sensitive Settings operation; ordinary exit waits for it to reach a consistency boundary.
- App Lock triggers are deferred while credentials are being changed or removed, then re-evaluated against the final saved state.
- Secret-owning Key Manager, Recovery, Identity, Signature, Vault, Recipient Encryption, Public Contacts, and recovery-artifact windows use cancellation/deferred-close lifecycle guards where applicable.
- Protect Folder captures the vault password once before async work and clears visible password controls before scanning.
- App Lock credentials and application settings use same-directory temporary files flushed before replacement.
- Application settings are size-bounded before JSON parsing and have isolated persistence regression tests.
- Privacy Mode reports clipboard/history cleanup failure rather than silently implying success.
- Protected clipboard generation prevents older Rice2k timers from intentionally erasing a newer value; manual Clear Clipboard Now advances the same generation.
- Static WPF/source preflight checks namespace isolation, XAML handler wiring, duplicate lifecycle overrides, and duplicate ordinary partial-class method signatures.
- Local validation is fail-closed and compatible with the documented Windows PowerShell command.

## Still unvalidated at runtime

The following gates remain open and must not be inferred from source review:

- supported Windows + .NET 10 Release build;
- complete security/regression test execution;
- restart/persistence acceptance;
- negative/interruption recovery acceptance;
- multi-gigabyte file/vault performance testing;
- App Lock/Privacy lifecycle acceptance on Windows;
- keyboard/Narrator/scaling/High Contrast/reduced-motion acceptance;
- portable/installer/signing release path;
- independent security review before Stable 1.0.

Use `tools\Validate-Rice2k.ps1` on a supported Windows machine and record results in `docs/RELEASE-READINESS.md`.
