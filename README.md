# Rice2k Encryption Software

**Rice2k Encryption Software** is a modern, user-friendly Windows encryption application designed to make strong data protection understandable and practical for everyday users while still providing advanced controls for experienced users.

> **Project status:** early development / security-focused foundation. The repository is being built in milestones. Do not rely on pre-1.0 builds as the only copy of irreplaceable data.

## Goals

- Make encryption easy enough for a first-time user.
- Use established cryptographic primitives instead of custom cryptography.
- Never silently destroy or overwrite a user's original data.
- Show clear progress, status, timing, verification, and recovery information.
- Keep advanced cryptographic settings out of the way unless the user asks for them.
- Remain useful offline and avoid sending user files to a server.

## Planned user experience

Rice2k uses a beginner-first interface with a **Command Center**, drag-and-drop workflows, Simple and Advanced modes, plain-English errors, real-time progress, verification stages, recovery guidance, privacy controls, searchable settings, and accessible Windows-friendly typography.

### Protect

- File encryption and decryption
- Folder/batch encryption
- Text encryption
- `.r2kenc` encrypted containers
- `.r2kvault` secure vaults
- Queue management for large jobs

### Security tools

- XChaCha20-Poly1305 authenticated encryption
- AES-256-GCM interoperability option
- Argon2id password-based key derivation
- Key manager
- Password/passphrase generator
- SHA-256/SHA-512 integrity verification
- Public-key encryption
- Digital signatures (`.r2ksig`)
- Recovery Center and recovery testing

### Safety and usability

- Verify encrypted output before offering source removal
- Temporary-file/finalize workflow to reduce corruption risk
- Clear success and failure screens
- Percentage, bytes processed, speed, elapsed time, estimated time remaining, and current stage
- Pause/cancel-aware operations
- Privacy Mode
- Clipboard auto-clear
- Automatic locking
- Activity history that never records passwords, decrypted text, or secret keys
- Simple Mode by default; Advanced Mode for technical controls

## File formats

| Extension | Purpose |
|---|---|
| `.r2kenc` | Rice2k encrypted file/container |
| `.r2kvault` | Rice2k secure vault |
| `.r2ksig` | Rice2k detached signature |
| `.r2kkey` | Rice2k exported key package |
| `.r2krecovery` | Rice2k recovery package |

## Technology direction

The Windows client targets **.NET 10 LTS**. The cryptographic foundation is designed around libsodium-compatible primitives, with **XChaCha20-Poly1305** for authenticated encryption and **Argon2id** for password-derived keys. Cryptographic code is kept separate from UI code so it can be reviewed and tested independently.

## Repository layout

```text
src/Rice2k.App/          Windows desktop application
src/Rice2k.Core/         Shared models and application logic (planned split)
src/Rice2k.Cryptography/ Cryptographic/container logic (planned split)
tests/                   Automated tests
docs/                    Architecture, format and security documentation
.github/workflows/       CI/build automation
```

## Security notice

Rice2k is security-sensitive software. Pre-1.0 builds should be treated as experimental. Keep independent backups of important files and test decryption before trusting any encryption workflow. See [SECURITY.md](SECURITY.md) and [docs/SECURITY-DESIGN.md](docs/SECURITY-DESIGN.md).

## Development roadmap

See [docs/ROADMAP.md](docs/ROADMAP.md) for the implementation plan and feature status.

## License

MIT License. See [LICENSE](LICENSE).

---

**Mission:** Make powerful encryption secure, understandable, and easy enough for anyone to use.
