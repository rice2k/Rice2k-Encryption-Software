# Rice2k Identity, Recipient Encryption, and Signature Formats

> Status: development formats for the Rice2k Encryption Software v0.5 preview. They may change before 1.0.

## Security and trust model

Rice2k separates three different questions that users can easily confuse:

1. **Can this public identity card authenticate its own key material?**
   - A valid `.r2kpub` self-signature shows that the card's public keys, fingerprint, label, identity ID, and creation time have not been internally altered since the holder of its signing private key created the card.
2. **Does the display name belong to the person I think it does?**
   - Not automatically. A self-signed public card does not establish real-world identity. Compare the Rice2k fingerprint through an independent trusted channel before relying on the label.
3. **Was this file signed by the expected Rice2k identity?**
   - A valid `.r2ksig` proves the signature matches the embedded signing public key and the signed file hash. If an expected `.r2kpub` identity is supplied, Rice2k additionally checks that the identity ID, fingerprint, and public keys match that expected identity.

Recipient encryption and digital signing are deliberately separate. Encrypting for someone does **not** prove who sent the file.

## `.r2kid` — encrypted private identity

Private Rice2k identities contain two key pairs:

- a public/private encryption key pair used for recipient encryption;
- an Ed25519 signing key pair used for detached signatures and public-card self-signatures.

Private identity packages:

- use the `R2KID001` magic value and format version 1;
- use Argon2id to derive a 256-bit key from the package password;
- use XChaCha20-Poly1305 authenticated encryption for the private identity payload;
- store KDF parameters and a random salt in the public header;
- authenticate security-relevant header fields as associated data;
- validate that imported private/public key pairs are internally consistent;
- validate the identity fingerprint after decryption;
- reject unsupported KDF parameters and oversized payloads before expensive processing;
- never overwrite an existing destination automatically.

The private payload contains the identity ID, label, creation time, fingerprint, encryption public/private keys, and signing public/private keys.

## `.r2kpub` — shareable public identity card

A public identity card is JSON using format identifier `R2KPUB1` and version 1. It contains only public information:

- identity ID;
- display-name label;
- creation time;
- Rice2k fingerprint;
- encryption public key;
- Ed25519 signing public key;
- Ed25519 self-signature covering the identity payload.

Import validates:

- format/version;
- key lengths;
- fingerprint recomputation from the public keys;
- the Ed25519 self-signature.

A successful import means the card is internally authentic to its own signing key. It does **not** independently prove that the display-name label belongs to a particular person.

## Public Identity Contacts

Rice2k can save validated `.r2kpub` cards in the local Public Identity Contacts store.

Important design choices:

- contacts contain public material only;
- Rice2k preserves the original `.r2kpub` card rather than converting it into an unsigned local record;
- the exact stored bytes are re-imported and cryptographically validated before a contact is finalized;
- saved contacts are revalidated each time the contact book is loaded;
- malformed or modified contact files are omitted rather than silently trusted;
- identity-ID collisions with different fingerprints are rejected rather than overwritten;
- fingerprint comparison remains the user's trust decision even after a card is saved.

## `R2KENC03` — recipient-encrypted file container

Recipient encryption uses `.r2kenc` with magic `R2KENC03` and format version 3.

### High-level design

1. Rice2k generates a fresh random 256-bit content key for the file.
2. For each selected recipient, the content key is wrapped using libsodium sealed-box public-key encryption and that recipient's encryption public key.
3. File metadata is encrypted with XChaCha20-Poly1305 under the content key.
4. File contents are encrypted in authenticated chunks with XChaCha20-Poly1305.
5. The recipient-specific wrapped keys and security-relevant public header information are bound into authenticated associated data.
6. A recipient decrypts by using their private encryption key to open one of the anonymous wrapped content-key entries, then authenticates/decrypts metadata and file chunks.

### Multi-recipient behavior

- A single container may contain multiple wrapped copies of the same random content key.
- The development parser limits the number of recipients and wrapped-key sizes before expensive work.
- Recipient names are not placed in the public header.
- Authenticated encrypted metadata records the recipient identity IDs/fingerprints for consistency after the content key has been recovered.
- A private identity that is not one of the recipients fails closed.

### What recipient encryption does not prove

Recipient encryption establishes confidentiality for the selected public keys. It does not authenticate the sender. Use a detached Rice2k signature when sender authenticity matters.

## `.r2ksig` — detached digital signature

Rice2k detached signatures use JSON format identifier `R2KSIG1`, version 1.

The signature document includes:

- signing time;
- signer identity ID;
- signer display-name label;
- signer fingerprint;
- encryption public key and Ed25519 signing public key;
- original filename and length;
- SHA-512 hash of the signed file;
- Ed25519 detached signature over the canonical Rice2k signature payload.

Verification performs two independent checks:

1. **Signature-document authentication** — validates the embedded public keys/fingerprint and verifies the Ed25519 signature.
2. **Current file match** — recomputes SHA-512 and checks file length/hash against the signed values.

If the user supplies an expected `.r2kpub` identity, verification also checks that the signature's identity ID, fingerprint, encryption public key, and signing public key all match that expected identity.

A valid signature without an independently verified expected identity should be described as signed by the displayed fingerprint/key, not as proof of a real-world person's identity.

## Fingerprints

Rice2k identity fingerprints are derived from both the encryption and signing public keys using a domain-separated SHA-256 construction. They are displayed in a grouped `R2KI-...` representation for human comparison.

Fingerprints are safe to share. Private keys are not.

## Pre-1.0 compatibility

These identity, recipient-encryption, contact-store, and signature formats are development formats. Parsers reject unsupported versions rather than guessing. Format changes before 1.0 may require explicit migration or compatibility code.
