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

### Security

- Original files are preserved by default.
- Encrypted file writes use temporary output before finalization.
- Authentication failure causes decryption to fail closed.
