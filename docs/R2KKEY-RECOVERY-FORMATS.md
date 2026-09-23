# Rice2k Key and Recovery Package Formats

> Development specification. These formats may change before Rice2k Encryption Software 1.0.

## Security goals

`.r2kkey` and `.r2krecovery` are password-protected authenticated containers for 256-bit Rice2k symmetric keys. They use established cryptographic primitives rather than a custom cipher.

Both formats currently use:

- **Argon2id** for password-based key derivation;
- a fresh random salt for every package;
- **XChaCha20-Poly1305** authenticated encryption;
- a fresh random nonce for every package;
- authenticated header parameters;
- bounded parser/KDF parameters before expensive work;
- temporary-file + final-move writes;
- no automatic overwriting of existing destination files.

The application never intentionally writes an unencrypted raw key file.

## `.r2kkey`

Purpose: portable encrypted export/import of one Rice2k 256-bit symmetric key.

### Version 1 envelope

| Field | Size/type | Notes |
|---|---:|---|
| Magic | 8 bytes | ASCII `R2KKEY01` |
| Version | 1 byte | `1` |
| KDF id | 1 byte | Argon2id |
| Argon2 operations | Int64 | currently 4 |
| Argon2 memory | Int32 | currently 64 MiB |
| Salt | 16 bytes | random |
| Nonce | 24 bytes | random XChaCha20 nonce |
| Cipher length | Int32 | bounded before allocation |
| Ciphertext | variable | authenticated encrypted JSON payload |

The authenticated associated data contains the magic, version, KDF id, KDF operation count, KDF memory value, and salt.

### Encrypted payload

The encrypted payload contains:

- key UUID;
- user-facing key name;
- original creation timestamp;
- 32-byte secret key encoded for serialization.

The raw key bytes are only present inside authenticated ciphertext on disk.

### Fingerprints

Rice2k computes a display fingerprint from SHA-256 of the 32-byte secret key and displays the first 80 bits as grouped hexadecimal:

`R2K-ABCD-EF12-3456-7890-ABCD`

The fingerprint is an identifier for comparison. It is **not** the encryption key and cannot be used to decrypt data.

## `.r2krecovery`

Purpose: a separately password-protected recovery copy of a Rice2k symmetric key.

A recovery package is intentionally distinct from a normal `.r2kkey` export so users can:

1. protect it with a different recovery password;
2. store it separately from the normal key package;
3. test recovery without replacing normal key files;
4. restore a fresh `.r2kkey` package with a new package password.

### Version 1 envelope

| Field | Size/type | Notes |
|---|---:|---|
| Magic | 8 bytes | ASCII `R2KREC01` |
| Version | 1 byte | `1` |
| KDF id | 1 byte | Argon2id |
| Argon2 operations | Int64 | currently 4 |
| Argon2 memory | Int32 | currently 64 MiB |
| Salt | 16 bytes | random |
| Nonce | 24 bytes | random XChaCha20 nonce |
| Cipher length | Int32 | bounded before allocation |
| Ciphertext | variable | authenticated encrypted recovery payload |

The encrypted recovery payload contains the key UUID, name, original key creation timestamp, recovery-package creation timestamp, and the 32-byte key material.

## Test Recovery behavior

The Recovery Center test workflow:

1. reads the selected `.r2krecovery` package;
2. validates magic/version/KDF parameters and resource bounds;
3. derives the package key with Argon2id;
4. authenticates and decrypts the package;
5. validates that it contains exactly one 256-bit key;
6. computes and displays the safe fingerprint;
7. records the successful test timestamp/fingerprint locally for the Command Center recovery-health indicator;
8. clears the recovered in-memory secret buffer when the test completes.

A successful test does **not** mean Rice2k can recover the package without its password. Losing both the normal key material and the recovery password can still make protected data unrecoverable.

## Development warning

These formats are pre-1.0 and have not yet completed the full release security gate. Keep independent copies of important data and do not make a pre-1.0 Rice2k package the only copy of irreplaceable key material.
