# Contributing to Rice2k Encryption Software

Thanks for helping improve Rice2k Encryption Software.

## Development priorities

1. Data safety before convenience.
2. Established cryptographic primitives instead of custom cryptography.
3. Clear user-facing behavior and recoverable workflows.
4. Tests for every security-sensitive change.
5. Accessibility and understandable language.

## Pull requests

Keep pull requests focused. Security-sensitive changes should describe:

- what behavior changes;
- what threat or failure mode is addressed;
- how the change was tested;
- whether the `.r2kenc` format changes;
- whether backward compatibility changes.

## Cryptography changes

Changes involving algorithms, key derivation, nonces, salts, authentication, container parsing, signatures, or key storage should include tests and documentation. Avoid inventing new cryptographic constructions.

## UI changes

User-facing changes should preserve:

- keyboard navigation;
- readable text at Windows scaling levels;
- status conveyed by text/icons, not color alone;
- plain-English errors;
- safe defaults;
- Simple Mode behavior unless the change explicitly targets Advanced Mode.

## Sensitive information

Never commit passwords, secret keys, recovery packages, personal encrypted files, or real private test data. Use generated test fixtures.
