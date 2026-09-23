# Security Policy

Rice2k Encryption Software handles security-sensitive data. Security reports are taken seriously.

## Supported versions

The project is currently pre-1.0. Only the latest development release is expected to receive security fixes.

## Reporting a vulnerability

Please do **not** open a public issue containing exploit details, secret test data, private keys, passwords, or sensitive files. Use GitHub's private vulnerability reporting/security advisory features when available.

A useful report should include:

- affected version/commit;
- operating system;
- clear reproduction steps;
- expected vs. observed behavior;
- whether confidentiality, integrity, authentication, key handling, or file recovery is affected.

## Security principles

- No custom encryption algorithms.
- Authenticated encryption is required for protected content.
- Password-derived keys use Argon2id with per-container random salts.
- Nonces are generated securely and must not be reused with the same key.
- Secret material must never be written to application logs.
- Encryption writes to a temporary destination first and finalizes only after success.
- The original source file is preserved by default.
- Decryption must fail closed when authentication or format validation fails.
- Container format versions and cryptographic parameters are explicit and versioned.
- Important cryptographic changes require tests and documentation updates.

## Pre-1.0 warning

Until the project has had significant independent review and testing, do not use it as the only protection or only copy of irreplaceable data. Keep backups and perform a test decryption.
