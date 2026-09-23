# Rice2k Encryption Software

**Rice2k Encryption Software** is a modern, user-friendly Windows encryption application designed to make strong data protection understandable and practical for everyday users while still providing advanced controls for experienced users.

> **Project status:** early development / security-focused foundation. Do not rely on a pre-1.0 build as the only copy of irreplaceable data.

## What works in the current development foundation

- Windows WPF Command Center with a dark, readable Rice2k interface
- File encryption to versioned `.r2kenc` containers
- File decryption with authentication and format checks
- XChaCha20-Poly1305 authenticated encryption
- Argon2id password-based key derivation with a random per-container salt
- Chunked/streaming file processing for large files
- Encrypted container metadata
- Temporary `.partial` output and finalize-on-success workflow
- Optional verification of encrypted data before finalization
- Cancellation with incomplete-output cleanup
- Original source file preserved by default
- Detailed progress model: percentage, bytes, speed, elapsed time, stage and estimated time remaining
- Authenticated text encryption/decryption
- Secure random password generator
- SHA-256 and SHA-512 file integrity tools
- In-memory activity view that does not record passwords, plaintext, or secret keys

## Planned product experience

The full product is being implemented in milestones. Planned features include:

- Three-step beginner-friendly Encrypt/Decrypt wizard
- Drag-and-drop file and folder workflows
- Folder and batch encryption
- Batch queue with overall and per-item progress
- Simple and Advanced modes
- Preflight disk-space/permission/collision checks
- Plain-English errors with expandable technical details
- Dedicated success/verification screens
- `.r2kvault` secure vaults
- Key Manager and `.r2kkey` exports
- Password + key-file protection
- Recovery Center and `.r2krecovery` packages
- Public-key recipient encryption
- Digital signatures (`.r2ksig`)
- Privacy Mode, auto-lock and clipboard auto-clear
- Searchable settings, first-run tour and contextual hints
- Accessibility review for keyboard, screen readers, scaling, high contrast and reduced motion
- AES-256-GCM interoperability mode
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

## Build and run

See [docs/BUILDING.md](docs/BUILDING.md).

Quick start on Windows with the .NET 10 SDK installed:

```powershell
git clone https://github.com/rice2k/Rice2k-Encryption-Software.git
cd Rice2k-Encryption-Software
dotnet restore Rice2kEncryption.sln
dotnet run --project .\src\Rice2k.App\Rice2k.App.csproj
```

## Repository layout

```text
Rice2kEncryption.sln
src/
  Rice2k.App/              Current Windows desktop application and service foundation
docs/
  BUILDING.md              Local build/run instructions
  ROADMAP.md               Milestones and 1.0 release gates
  SECURITY-DESIGN.md       Security architecture and threat notes
  R2KENC-FORMAT.md         Development encrypted-container format
  UX-SPEC.md               User-interface and usability specification
.github/workflows/
  build.yml                Manual build workflow
```

## Current build-validation note

A GitHub Actions workflow is included, but during initial setup GitHub-hosted jobs were terminating before any runner was assigned (`runner_id: 0`, zero executed steps). Automatic push builds are therefore disabled for now to avoid presenting that infrastructure failure as a source-code compilation failure. The workflow remains available for manual dispatch once hosted runners are available.

## License

MIT License. See [LICENSE](LICENSE).

---

**Mission:** Make powerful encryption secure, understandable, and easy enough for anyone to use.
