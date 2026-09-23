# Rice2k Encryption Software — UX Specification

## Product principle

A person who does not know what a nonce, KDF, AEAD, salt, or authentication tag is should still be able to protect a file correctly.

Security terminology belongs behind **Advanced Mode** unless the user specifically asks for it.

## Visual direction

- Windows-friendly high-tech design, not a "hacker console".
- Near-black / dark navy background.
- Electric blue primary accent.
- Teal secondary accent.
- Green success, amber warning, red error.
- Segoe UI Variable / Segoe UI for normal interface text.
- Monospace font only for hashes, fingerprints, keys, and other technical values.
- 8–12 px rounded panels.
- Thin low-contrast borders.
- Motion should be subtle and respect reduced-motion preferences.
- Status must never rely on color alone.

## Main navigation

```text
HOME
  Command Center

PROTECT
  Encrypt
  Decrypt
  Text Encryption
  Secure Vault

SECURITY TOOLS
  Key Manager / Passwords
  File Integrity
  Digital Signatures
  Recovery Center
  Security Center

SYSTEM
  Activity
  Settings
```

## Simple Mode

Default for new users.

A normal encryption workflow asks only:

1. What do you want to protect?
2. How do you want to protect it?
3. Where should the protected file go?
4. Ready to encrypt?

The default security profile is selected automatically.

## Advanced Mode

Advanced Mode may expose:

- encryption algorithm;
- password derivation profile;
- Argon2id memory/operations settings;
- metadata protection behavior;
- chunk sizing;
- interoperability modes;
- detailed container information.

It must always include **Restore Recommended Settings**.

## Encrypt wizard

### Step 1 — Choose data

```text
Choose what to protect

[ File ] [ Folder ] [ Text ] [ Vault ]

Drag files here
or
[ Browse ]
```

### Step 2 — Protection

```text
How should this be protected?

● Password
○ Password + Key File
○ Encryption Key
○ Encrypt for Someone
```

Unavailable options should be visibly labeled as unavailable rather than silently doing something different.

### Step 3 — Review

Show:

- source name and size;
- output location;
- protection method;
- security profile;
- verification setting;
- whether filename/metadata are encrypted;
- estimated extra disk space required.

The primary button should say exactly what will happen: **Encrypt File**, **Encrypt Folder**, etc.

## Operation progress

Every operation expected to last more than about a second should show activity immediately.

When measurable, show:

- percentage;
- bytes processed / total;
- current throughput;
- elapsed time;
- estimated time remaining;
- current stage;
- queue position for batch work.

Example:

```text
Encrypting GraalArchive.zip

████████████████░░░░ 72%
12.8 GB / 17.8 GB
184 MB/s
Elapsed: 1m 14s
Estimated remaining: about 27s

✓ Checking file
✓ Preparing encryption
● Encrypting
○ Verifying
○ Finalizing

[ Pause ] [ Cancel ]
```

Time remaining must always be labeled as estimated/about.

## Cancellation

Cancel should explain what happens:

```text
Stop encryption?

The incomplete encrypted file will be removed.
Your original file will not be changed.

[ Keep Encrypting ] [ Stop Encryption ]
```

The default/safer button should be visually clear.

## Completion

Do not silently return to the start screen.

Show a completion view containing:

- success icon + text;
- output filename/path;
- output size;
- verification result;
- protection profile;
- elapsed time;
- Open Folder;
- Copy Path;
- Verify Again;
- Encrypt Another.

## Errors

Never make a hexadecimal error code the primary message.

Bad:

```text
ERROR 0x80070005
```

Preferred:

```text
Couldn't save the encrypted file

Rice2k does not have permission to write to:
C:\Protected Files\

[ Choose Another Folder ]

Technical details ▾
```

Technical details are optional/expandable.

## Preflight checks

Before a long operation check as applicable:

- source exists and can be read;
- destination folder can be written;
- source and destination are not the same file;
- enough free disk space is available;
- destination collision exists;
- secure random source is available;
- encryption engine initialized;
- filename/path is valid.

## Destination collisions

Never overwrite automatically.

Offer:

- Keep both (default)
- Replace (requires explicit action)
- Choose another name

## Original-file safety

Default: **Keep original**.

Source removal must never happen before encrypted output has been fully created and authenticated/verified.

Any future removal option must accurately explain that ordinary deletion on SSDs is not guaranteed physical secure erasure.

## Password UX

Password setup should show:

- show/hide control;
- length;
- useful strength guidance;
- common-password/pattern warnings when implemented;
- Generate Secure Password/Passphrase action;
- prominent warning that Rice2k cannot recover a forgotten password unless the user created a separate recovery/key arrangement.

Do not claim exact crack times.

## Password generator

Modes planned:

- easy-to-remember passphrase;
- random password;
- custom character policy.

Copying a secret should provide a clipboard-clear option/timer once Privacy Mode is implemented.

## Drag and drop

Dropping a normal file should lead to Encrypt.
Dropping `.r2kenc` should lead to Decrypt/Inspect.
Dropping multiple files should create a queue.
Dropping a folder should lead to folder protection.

## Queue

Show both overall and current-item progress.

```text
3 of 8 complete
Overall: 61%

Current: GraalArchive.zip
72% • 12.8 GB / 17.8 GB • 188 MB/s • about 27s
```

Users must be able to identify failures per item without losing successful queue results.

## Recovery Center

Recovery is a primary product area.

Planned actions:

- Create Recovery Kit
- Back Up Key
- Restore Backup
- Test Recovery
- Show recovery-health status

A guided recovery test should encrypt a disposable sample, decrypt it, and compare the result.

## Privacy Mode

Planned controls:

- hide filenames in activity history;
- don't remember recent files;
- clipboard auto-clear;
- lock when minimized;
- lock on Windows session lock;
- hide previews/content snippets.

## Activity

Never log:

- passwords;
- private/secret key material;
- decrypted text;
- recovery secrets.

Activity should be optional and clearable.

## Status bar

Permanent bottom status should summarize the current state.

Examples:

```text
● Ready | Offline/local | Vault Locked | v1.0.0
● Encrypting Archive.zip | 72% | 184 MB/s | about 27s
⚠ Recovery backup not configured
```

A warning should link directly to the setting or workflow that resolves it.

## Hints and learn mode

Hints should be short, dismissible, and gradually disappear as the user becomes familiar with the application.

A `?` / What is this? mode may explain controls in plain language.

Example:

```text
Integrity verification
Checks whether an encrypted file was changed or damaged after it was created.
```

## First-run experience

Keep the initial tour brief (roughly five screens or fewer):

1. What Rice2k does.
2. Drop a file → choose protection → Encrypt.
3. Password/recovery warning.
4. Vault/Recovery overview.
5. Simple vs Advanced Mode.

## Accessibility requirements

- Full keyboard navigation.
- Visible keyboard focus.
- Logical tab order.
- Windows text scaling support.
- Screen-reader names/descriptions for meaningful controls.
- High-contrast compatibility.
- Status text in addition to color/icon.
- Reduced-motion option/system preference support.
- Avoid extremely small UI text.

## Terminology

Use these words consistently:

- Encrypt
- Decrypt
- Verify
- Password
- Key
- Vault
- Recovery
- Integrity

Avoid switching between technical synonyms unnecessarily.
