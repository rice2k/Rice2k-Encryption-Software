# Rice2k Encryption Software

**Rice2k Encryption Software** is a modern, user-friendly Windows encryption application designed to make strong data protection understandable and practical for everyday users while still providing advanced controls for experienced users.

> **Project status:** early development / security-focused foundation. Current application version: **0.4.0-preview.1**. Do not rely on a pre-1.0 build as the only copy of irreplaceable data or key material.

## What works in the current development foundation

### Guided Windows experience

- Windows WPF Command Center with a dark, readable Rice2k interface
- Three-step beginner-friendly **Encrypt** workflow
- Three-step beginner-friendly **Decrypt** workflow
- Five-page first-run guided tour and replay action
- Searchable Settings panel and persisted helpful-hints preference
- Contextual Encrypt/Decrypt hints
- Keyboard shortcuts for major workflows
- Screen-reader metadata for primary controls
- Plain-English global error dialog with collapsed/copyable technical details for unexpected UI failures
- Detailed progress for file operations: percentage, bytes, speed, elapsed time, current stage, and estimated time remaining
- Dedicated completion screens with output path, elapsed time, and Open Folder actions

### Files, folders, and queues

- Drag-and-drop routing: normal files open Encrypt; `.r2kenc` files open Decrypt
- Multiple files or folders route into the **Batch Queue**
- Add Files and Add Folder batch intake
- Background recursive folder scanning
- Reparse/junction directories and inaccessible folders are skipped during folder scans
- Per-file batch status/progress and overall queue progress
- Sequential batch encryption with isolated per-file failures
- Pause/resume at safe chunk boundaries for single-file and batch operations
- Safe cancellation while running or paused
- Completed batch outputs remain intact if a later item fails or the queue is cancelled
- Password confirmation and local strong-password generation
- Preflight checks for source readability, destination writability, conflicts, destination availability, and free space when available
- Automatic **Keep Both** naming and late-collision choices

### Cryptographic foundation

- XChaCha20-Poly1305 authenticated encryption
- Argon2id password-based key derivation with random per-container salts
- Chunked/streaming file processing for large files
- Encrypted container metadata
- Randomized same-directory `.partial` temporary outputs
- Optional verification before finalization
- Original source files preserved by default
- Defensive parser resource limits before expensive KDF/allocation work
- Authenticated metadata consistency checks
- Authenticated text encryption/decryption
- Secure random password generator
- SHA-256 and SHA-512 file-integrity tools
- In-memory activity view that does not record passwords, plaintext, or secret keys

### Password + key-file protection

The guided Encrypt workflow offers:

- **Password only** — `R2KENC01` v1;
- **Password + Rice2k key file** — `R2KENC02` v2, requiring both the file password and the matching 256-bit key from an encrypted `.r2kkey` package.

For v2 files Rice2k unlocks the selected `.r2kkey` only in memory, combines the Argon2id password-derived key with the 256-bit key-file secret using domain-separated HMAC-SHA-256, and uses the resulting 256-bit key with XChaCha20-Poly1305. Only a non-secret key fingerprint is exposed in the public header, and the required fingerprint is also bound into authenticated data. Decrypt automatically detects v2 and identifies the expected fingerprint.

Existing `R2KENC01` files remain supported and are not silently rewritten.

### Key Manager

- Generate random **256-bit symmetric keys** locally
- Human-readable names and safe SHA-256-derived fingerprints
- Password-protected authenticated `.r2kkey` export/import
- Duplicate-key detection in the active Key Manager session
- In-memory secret-key clearing when a key is removed or the Key Manager closes
- Existing key-package files are never overwritten automatically

> Key persistence is intentionally explicit in this preview: generated keys remain in memory unless the user exports an encrypted `.r2kkey` package.

### Recovery Center

- Create a separately password-protected `.r2krecovery` package from an existing `.r2kkey`
- Use a recovery password separate from the normal key-package password
- **Test Recovery** entirely in memory
- Display only the safe recovered-key fingerprint
- Clear temporary recovered secret-key bytes after testing
- Restore a fresh `.r2kkey` with a new package password
- Command Center **Recovery health** indicator based on the last successful local recovery test

A recovery package is not a password bypass. If its recovery password is lost, Rice2k cannot decrypt it.

### Secure Vault — v0.4 preview

The application now includes an operational **`.r2kvault` Secure Vault** foundation:

- Create and unlock versioned password-protected vaults
- Encrypted filenames, relative paths, timestamps, sizes, and other manifest metadata
- XChaCha20-Poly1305 authenticated chunk encryption for stored files
- Explicit locked/unlocked state
- In-memory vault content key cleared when the vault is locked or closed
- Add individual files
- Add complete folders while preserving relative paths
- Search filenames/paths after unlock
- Extract files without automatic overwrite
- Rename vault entries
- Remove vault entries
- Full manifest and encrypted-file verification
- 10-minute inactivity auto-lock option in the Vault Browser
- Sequence-number protection against concurrent stale-session writes
- Atomic mutation workflow using a same-directory pending vault
- Full verification of the pending vault before it can replace the active vault
- Recovery `.backup` retained during final replacement
- Full verification after replacement before the recovery backup is released
- Recovery-backup controls to **Verify Backup**, **Restore Backup**, or **Move Backup Aside**
- Backup restore preserves the newer active vault as a separate pre-recovery copy instead of deleting it

Locked vaults do not retain decrypted filenames in the session. The public container still reveals unavoidable structural information such as approximate encrypted record sizes and record count; padding/size-hiding is not implemented yet.

### Security/regression testing foundation

The solution includes an xUnit v3 test project with source-controlled coverage for:

- encrypted-text round trips, wrong passwords, malformed tokens, and tampering;
- empty, normal, and multi-chunk password-only file round trips;
- password + key-file round trips, wrong-key/password rejection, tampering, cancellation, and v1 compatibility detection;
- source-file preservation, overwrite prevention, truncation, resource limits, and cancellation cleanup;
- `.r2kkey` round trips, wrong-password rejection, and tamper detection;
- `.r2krecovery` round trips, wrong-password rejection, and restored fingerprint preservation;
- `.r2kvault` create/unlock/verify lifecycle;
- vault add/extract, rename/remove, wrong-password rejection, tamper detection, duplicate-path prevention, and extraction overwrite prevention;
- vault recovery-backup verification, restore, current-state preservation, safe move-aside, and destination overwrite prevention.

The suite is source-controlled but has **not yet been executed by hosted CI** because GitHub has not assigned a runner to this repository during attempted validation jobs.

## Planned product experience

Major work still planned before 1.0 includes:

- Dedicated folder-container encryption outside the Vault workflow
- Batch output-folder controls and batch decryption
- Full caught-error technical-details integration across every routine workflow
- Finished Advanced Mode and compatibility controls
- Vault interruption/fault-injection tests and large-vault performance profiling
- Optional vault size-hiding/padding analysis
- Public-key recipient encryption
- Digital signatures (`.r2ksig`)
- Broader Privacy Mode, application auto-lock, and clipboard auto-clear
- Full accessibility review for keyboard, screen readers, scaling, high contrast, and reduced motion
- AES-256-GCM interoperability mode
- Fuzzing, concurrency tests, and very-large-file release-gate tests
- Signed Windows installer, portable build, and signed update path
- External security review before stable use is recommended

See [docs/ROADMAP.md](docs/ROADMAP.md), [docs/UX-SPEC.md](docs/UX-SPEC.md), and the repository's open issues for the detailed implementation plan.

## Design goals

- Make encryption easy enough for a first-time user.
- Use established cryptographic primitives instead of custom cryptography.
- Never silently destroy or overwrite a user's original data.
- Show clear progress, status, timing, verification, and recovery information.
- Keep advanced cryptographic settings out of the way unless the user asks for them.
- Remain useful offline and avoid sending user files to a server.

## File formats

| Extension | Purpose | Status |
|---|---|---|
| `.r2kenc` | Encrypted file/container (`R2KENC01` password-only and `R2KENC02` password + key-file) | Development implementation |
| `.r2kkey` | Password-protected symmetric key package | Development implementation |
| `.r2krecovery` | Separately password-protected recovery package | Development implementation |
| `.r2kvault` | Persistent encrypted secure vault | **v0.4 development implementation** |
| `.r2ksig` | Detached signature | Planned |

Format documentation:

- [R2KENC development formats](docs/R2KENC-FORMAT.md)
- [R2KKEY and recovery formats](docs/R2KKEY-RECOVERY-FORMATS.md)
- [R2KVAULT development format](docs/R2KVAULT-FORMAT.md)

These formats may change before 1.0.

## Security direction

The Windows client targets **.NET 10 LTS**. The current encryption foundation uses libsodium-compatible **XChaCha20-Poly1305** authenticated encryption and **Argon2id** for password-derived keys.

Security-sensitive operations are kept behind service classes rather than UI button logic. A later refactor will move them into dedicated `Rice2k.Cryptography` / `Rice2k.Core` projects before stable release so they can be reviewed and tested independently.

Read [SECURITY.md](SECURITY.md) and [docs/SECURITY-DESIGN.md](docs/SECURITY-DESIGN.md) before relying on development builds.

## Build, run, and test

See [docs/BUILDING.md](docs/BUILDING.md).

Quick start on Windows with the .NET 10 SDK installed:

```powershell
git clone https://github.com/rice2k/Rice2k-Encryption-Software.git
cd Rice2k-Encryption-Software
dotnet restore Rice2kEncryption.sln
dotnet build Rice2kEncryption.sln --configuration Release
dotnet test .\tests\Rice2k.Tests\Rice2k.Tests.csproj --configuration Release
dotnet run --project .\src\Rice2k.App\Rice2k.App.csproj
```

## Repository layout

```text
Rice2kEncryption.sln
src/
  Rice2k.App/                       Windows desktop application and current security-service foundation
tests/
  Rice2k.Tests/                     Automated security/regression tests
docs/
  BUILDING.md                       Local build/run/test instructions
  ROADMAP.md                        Milestones and 1.0 release gates
  SECURITY-DESIGN.md                Security architecture and threat notes
  R2KENC-FORMAT.md                  File-container formats
  R2KKEY-RECOVERY-FORMATS.md        Key/recovery package formats
  R2KVAULT-FORMAT.md                Secure Vault format and atomic-update model
  UX-SPEC.md                        User-interface and usability specification
.github/workflows/
  build.yml                         Manual build/test validation workflow
```

## Current build-validation note

A GitHub Actions workflow is included, but during initial setup GitHub-hosted jobs terminated before any runner was assigned (`runner_id: 0`, zero executed steps), including Windows and Ubuntu attempts. Automatic push builds remain disabled so infrastructure failure is not presented as source-code compilation failure. The manual workflow remains available once a hosted runner is assigned.

The current working environment also does not contain the .NET SDK, so the v0.4 vault code and newly added tests have been source-reviewed but **not compiled or executed here**. Treat this preview as development software until the full build/test gate runs successfully.

## License

MIT License. See [LICENSE](LICENSE).

---

**Mission:** Make powerful encryption secure, understandable, and easy enough for anyone to use.