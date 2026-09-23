# Rice2k Encryption Software

**Rice2k Encryption Software** is a modern, user-friendly Windows encryption application designed to make strong data protection understandable and practical for everyday users while still providing advanced security tools for experienced users.

> **Project status:** early development / security-focused preview. Current application version: **0.5.0-preview.1**. Do not use a pre-1.0 build as the only copy of irreplaceable data, recovery material, or private keys.

## Current development features

### Beginner-friendly Windows experience

- Windows WPF Command Center with a dark, readable Rice2k interface
- Three-step guided **Encrypt** and **Decrypt** workflows
- First-run tour, contextual hints, searchable Settings, and keyboard shortcuts
- Plain-English errors with expandable technical details
- Detailed operation status: percentage, bytes, speed, elapsed time, ETA, and current stage
- Completion screens that clearly identify the output and verification result
- Main status bar reads the actual application assembly version

### File and folder protection

- Password-protected `.r2kenc` files (`R2KENC01`)
- Password + `.r2kkey` protection (`R2KENC02`)
- Drag-and-drop encryption/decryption
- Batch queue with folder scanning, per-file status, pause/resume, and safe cancellation
- Preflight checks for readability, writability, conflicts, free space, and output collisions
- Keep Both behavior instead of silent overwrite
- One-click **Protect Folder** using the same authenticated `.r2kvault` format as Secure Vault
- Source files and folders preserved by default

### Cryptographic foundation

- XChaCha20-Poly1305 authenticated encryption
- Argon2id password-based key derivation with random per-container salts
- Chunked/streaming processing for large files
- Encrypted metadata
- Authenticated chunk ordering and integrity checks
- Randomized same-directory temporary outputs
- Optional post-encryption verification before finalization
- Defensive parser/resource limits before expensive KDF or allocation work
- SHA-256 and SHA-512 file-integrity tools
- Secure random password generator

### Key Manager and Recovery Center

- Generate local random 256-bit symmetric keys
- Human-readable safe key fingerprints
- Password-protected authenticated `.r2kkey` export/import
- Duplicate-key detection
- In-memory secret-key clearing when removed/closed
- Separately password-protected `.r2krecovery` packages
- In-memory **Test Recovery** workflow
- Restore a fresh `.r2kkey` from a valid recovery package
- Command Center recovery-health status

A recovery package is not a password bypass. Losing the recovery password means Rice2k cannot decrypt that recovery package.

## Secure Vault — v0.4/v0.5 foundation

Rice2k includes an operational `.r2kvault` Secure Vault:

- encrypted filenames, relative paths, timestamps, sizes, and manifest metadata;
- authenticated chunked file contents;
- create, unlock, lock, verify, search, add, extract, rename, and remove;
- configurable persisted inactivity auto-lock (1, 5, 10, 15, or 30 minutes);
- sequence protection against stale/concurrent writes;
- same-directory pending vault construction;
- full verification before and after atomic replacement;
- preserved `<vault>.backup` recovery copy during final replacement;
- Verify Backup, Restore Backup, and Move Backup Aside controls;
- detailed progress and **Cancel safely** for long vault operations;
- lock/close lifecycle guards during active mutations;
- interruption recovery tooling that discovers `.backup` and stale `.pending` artifacts;
- cryptographic verification of interrupted pending saves before they may be preserved separately;
- Vault Recovery Files window so recovery artifacts do not require manual filesystem investigation;
- `Ctrl+F` vault search, `Ctrl+L` lock, and `Esc` lock shortcuts when idle.

Locked vault sessions clear the in-memory content key and decrypted entry list. The public container can still reveal structural information such as approximate encrypted record sizes and record count; padding/size-hiding is not implemented yet.

## Identities, recipient encryption, and digital signatures — v0.5

Rice2k now includes a public/private identity system for secure sharing.

### Rice2k identities

- Generate encryption and signing key pairs locally
- Password-protected authenticated private identity packages (`.r2kid`)
- Shareable self-signed public identity cards (`.r2kpub`)
- Human-readable `R2KI-...` fingerprints
- Public-card validation before import
- Public Identity Contacts that preserve and revalidate the original `.r2kpub` card
- Multi-select saved contacts directly from the Recipient Encryption workflow

A valid self-signature proves a public card is internally authentic to its own signing key. It does **not** prove that the display-name label belongs to a particular real-world person. Compare fingerprints through an independent trusted channel when identity matters.

### Recipient encryption

`R2KENC03` allows one encrypted file to be protected for one or many Rice2k public identities:

- fresh random 256-bit content key per file;
- libsodium sealed-box wrapping of the content key for each recipient;
- XChaCha20-Poly1305 authenticated metadata and chunked file contents;
- anonymous wrapped-recipient entries in the public container header;
- multi-recipient support;
- progress, ETA, cancellation, verification, and non-overwrite protections;
- private identity required to recover a matching wrapped content key.

Recipient encryption provides confidentiality for the selected public keys. It does **not** authenticate the sender.

### Detached signatures

Rice2k `.r2ksig` signatures use Ed25519 plus SHA-512 file hashing:

- sign a file with a Rice2k private identity;
- verify the signature document cryptographically;
- verify that the current file still matches the signed SHA-512 hash and length;
- display signer fingerprint and signed metadata;
- optionally compare against an expected `.r2kpub` identity;
- fail closed on malformed or modified signature data.

Use recipient encryption when you need confidentiality. Use signatures when you need authenticity/integrity. Use both when you need both properties.

## Security/regression testing foundation

The source-controlled xUnit suite covers, among other areas:

- password-only and password + key-file file round trips;
- wrong passwords/keys, tampering, truncation, overwrite prevention, and cancellation cleanup;
- `.r2kkey` and `.r2krecovery` round trips and failure cases;
- vault lifecycle, mutation, recovery-backup, parser-safety, progress, and deterministic fault-injection behavior;
- one-click folder protection and source-preservation rules;
- private/public identity package validation and tamper rejection;
- recipient and multi-recipient encryption/decryption;
- detached signature creation, verification, and tamper detection;
- public identity contact-card validation.

`tools/Rice2k.VaultBench` provides a repeatable local benchmark/correctness harness for larger vault workloads.

> **Validation limitation:** the current working environment does not contain the .NET SDK, and GitHub-hosted Actions jobs have not been assigned a runner during attempted validation. The newer tests are committed and source-reviewed but are **not claimed as executed** here.

## File formats

| Extension / magic | Purpose | Status |
|---|---|---|
| `.r2kenc` / `R2KENC01` | Password-protected encrypted file | Development implementation |
| `.r2kenc` / `R2KENC02` | Password + key-file encrypted file | Development implementation |
| `.r2kenc` / `R2KENC03` | One/multi-recipient encrypted file | **v0.5 development implementation** |
| `.r2kkey` | Password-protected symmetric key package | Development implementation |
| `.r2krecovery` | Separately password-protected recovery package | Development implementation |
| `.r2kvault` | Persistent encrypted vault / protected-folder container | Development implementation |
| `.r2kid` | Password-protected private Rice2k identity | **v0.5 development implementation** |
| `.r2kpub` | Shareable self-signed public identity card | **v0.5 development implementation** |
| `.r2ksig` / `R2KSIG1` | Detached Ed25519 signature document | **v0.5 development implementation** |

Formats may change before 1.0.

## Documentation

- [Build / run / test](docs/BUILDING.md)
- [Roadmap](docs/ROADMAP.md)
- [Security design](docs/SECURITY-DESIGN.md)
- [UX specification](docs/UX-SPEC.md)
- [R2KENC formats](docs/R2KENC-FORMAT.md)
- [R2KKEY / Recovery formats](docs/R2KKEY-RECOVERY-FORMATS.md)
- [R2KVAULT format](docs/R2KVAULT-FORMAT.md)
- [Identity / Recipient Encryption / Signatures](docs/R2K-IDENTITY-SHARING-SIGNATURES.md)
- [Vault benchmarking](docs/VAULT-BENCHMARKING.md)

## Build, run, and test

On Windows with the .NET 10 SDK installed:

```powershell
git clone https://github.com/rice2k/Rice2k-Encryption-Software.git
cd Rice2k-Encryption-Software
dotnet restore Rice2kEncryption.sln
dotnet build Rice2kEncryption.sln --configuration Release
dotnet test .\tests\Rice2k.Tests\Rice2k.Tests.csproj --configuration Release
dotnet run --project .\src\Rice2k.App\Rice2k.App.csproj
```

Vault benchmark:

```powershell
dotnet run --project .\tools\Rice2k.VaultBench\Rice2k.VaultBench.csproj -c Release
```

## Remaining work before 1.0

Major release gates still include:

- execute the full test suite on the supported Windows/.NET 10 toolchain;
- large-vault and multi-gigabyte benchmark runs;
- broader Privacy Mode, application auto-lock, and clipboard auto-clear;
- Advanced Mode and compatibility controls;
- AES-256-GCM interoperability mode;
- broader fuzzing/concurrency tests;
- full keyboard/screen-reader/high-contrast/scaling/reduced-motion review;
- signed Windows installer, portable release, and signed update path;
- dedicated security review before stable use is recommended.

## License

MIT License. See [LICENSE](LICENSE).

---

**Mission:** Make powerful encryption secure, understandable, and easy enough for anyone to use.
