# Rice2k Encryption Software

**Rice2k Encryption Software** is a modern, user-friendly Windows encryption application designed to make strong data protection understandable and practical for everyday users while still providing advanced controls for experienced users.

> **Project status:** early development / security-focused foundation. Current application version: **0.2.0-preview.1**. Do not rely on a pre-1.0 build as the only copy of irreplaceable data.

## What works in the current development foundation

### Guided Windows experience

- Windows WPF Command Center with a dark, readable Rice2k interface
- Three-step beginner-friendly **Encrypt** workflow
- Three-step beginner-friendly **Decrypt** workflow
- Five-page first-run guided tour
- Replay Welcome Tour action
- Searchable Settings panel
- Persisted Show Helpful Hints preference
- Contextual Encrypt/Decrypt hints that can be hidden
- Plain-English global error dialog with collapsed/copyable technical details for unexpected UI failures
- Detailed progress: percentage, bytes, speed, elapsed time, current stage, and estimated time remaining
- Dedicated completion screens with output path, elapsed time, and Open Folder actions

### Files, folders, and queues

- Drag-and-drop routing: normal files open Encrypt; `.r2kenc` files open Decrypt
- Multiple files or folders route into the **Batch Queue**
- Add Files and Add Folder batch intake
- Background recursive folder scanning so directory discovery does not intentionally block the UI
- Reparse/junction directories and inaccessible folders are skipped during folder scans
- Per-file batch status/progress and overall queue progress
- Sequential batch encryption with isolated per-file failures
- Pause/resume at safe chunk boundaries for single-file and batch operations
- Safe cancellation while running or paused
- Completed batch outputs remain intact if a later item fails or the queue is cancelled
- Password confirmation before file and batch encryption
- Local strong-password generation for Encrypt and Batch workflows
- Preflight checks for source readability, source/destination conflicts, destination availability, and free space when the destination exposes it
- Automatic **Keep Both** naming so existing destination files are not overwritten

### Cryptographic foundation

- File encryption to versioned `.r2kenc` containers
- File decryption with authentication and format checks
- XChaCha20-Poly1305 authenticated encryption
- Argon2id password-based key derivation with a random per-container salt
- Chunked/streaming file processing for large files
- Encrypted container metadata
- Unique randomized same-directory `.partial` temporary outputs
- Optional verification of encrypted data before finalization
- Original source file preserved by default
- Parser resource limits applied before expensive KDF/allocation work
- Authenticated metadata consistency checks
- Authenticated text encryption/decryption
- Secure random password generator
- SHA-256 and SHA-512 file integrity tools
- In-memory activity view that does not record passwords, plaintext, or secret keys

### Security/regression testing foundation

The solution includes an xUnit v3 test project with current coverage for:

- encrypted-text round trips and fresh randomness;
- wrong-password and ciphertext-tamper failures;
- malformed text-token validation;
- empty, normal, and multi-chunk file round trips;
- source-file preservation;
- file tampering and truncation rejection;
- unauthenticated KDF resource-limit rejection before key derivation;
- existing-destination overwrite prevention;
- service-level password policy;
- cancellation cleanup.

The suite is source-controlled but has **not yet been executed by hosted CI** because GitHub has not assigned a runner to this repository during the attempted validation jobs.

## Planned product experience

The full product is being implemented in milestones. Major planned work includes:

- Dedicated folder-container encryption workflow beyond batch-per-file encryption
- Batch output-folder controls and batch decryption
- Full caught-error technical-details integration across every routine workflow
- Finished Advanced Mode and compatibility controls
- `.r2kvault` secure vaults
- Key Manager and `.r2kkey` exports
- Password + key-file protection
- Recovery Center and `.r2krecovery` packages
- Public-key recipient encryption
- Digital signatures (`.r2ksig`)
- Privacy Mode, auto-lock and clipboard auto-clear
- Full accessibility review for keyboard, screen readers, scaling, high contrast and reduced motion
- AES-256-GCM interoperability mode
- Fuzzing, interrupted-write simulation, concurrency tests, and very-large-file release-gate tests
- Signed Windows installer, portable build and signed update path

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
| `.r2kenc` | Rice2k encrypted file/container | Development implementation |
| `.r2kvault` | Rice2k secure vault | Planned |
| `.r2ksig` | Rice2k detached signature | Planned |
| `.r2kkey` | Rice2k exported key package | Planned |
| `.r2krecovery` | Rice2k recovery package | Planned |

The development `.r2kenc` layout is documented in [docs/R2KENC-FORMAT.md](docs/R2KENC-FORMAT.md). It may change before 1.0.

## Security direction

The Windows client targets **.NET 10 LTS**. The current encryption foundation uses libsodium-compatible **XChaCha20-Poly1305** authenticated encryption and **Argon2id** for password-derived keys.

The current prototype keeps cryptographic operations behind service classes rather than UI button logic. A later refactor will move the security-sensitive code into dedicated `Rice2k.Cryptography` / `Rice2k.Core` projects before stable release so it can be reviewed and tested independently.

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
  Rice2k.App/              Current Windows desktop application and security-service foundation
tests/
  Rice2k.Tests/            Automated security/regression tests
docs/
  BUILDING.md              Local build/run/test instructions
  ROADMAP.md               Milestones and 1.0 release gates
  SECURITY-DESIGN.md       Security architecture and threat notes
  R2KENC-FORMAT.md         Development encrypted-container format
  UX-SPEC.md               User-interface and usability specification
.github/workflows/
  build.yml                Manual build/test validation workflow
```

## Current build-validation note

A GitHub Actions workflow is included, but during initial setup GitHub-hosted jobs terminated before any runner was assigned (`runner_id: 0`, zero executed steps), including both Windows and Ubuntu attempts. Automatic push builds are therefore disabled for now to avoid presenting infrastructure failure as a source-code compilation failure. The manual workflow remains available for dispatch once a hosted runner is assigned.

## License

MIT License. See [LICENSE](LICENSE).

---

**Mission:** Make powerful encryption secure, understandable, and easy enough for anyone to use.
