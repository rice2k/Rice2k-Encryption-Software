# Rice2k Encryption Software — Release Readiness

This checklist is the stabilization gate between the current development preview and a Beta/Release Candidate/Stable build.

Current source version: **0.6.0-preview.4**

## Stop-feature-creep rule

During stabilization, new major features should not be added unless they are required to fix a release blocker, security problem, data-loss risk, accessibility blocker, or build/test failure. Cosmetic work can wait.

## Beta readiness checklist

### 1. Build the complete solution on Windows + .NET 10 — **IN PROGRESS**

Required command sequence:

```powershell
dotnet --info
dotnet restore Rice2kEncryption.sln
dotnet build Rice2kEncryption.sln --configuration Release --no-restore
```

Pass criteria:
- `dotnet restore` succeeds;
- Release build succeeds for the complete solution;
- no unresolved compile errors;
- warnings are reviewed and security/data-safety-relevant warnings are fixed before Beta;
- exact SDK/runtime and build result are recorded below.

Current result: **Not passed yet.** Prior GitHub-hosted jobs were created but executed zero workflow steps. Automatic Windows validation is being enabled so this gate can be retried.

### 2. Execute the complete automated security/regression suite — **NOT STARTED AS A VALIDATED RUN**

```powershell
dotnet test tests/Rice2k.Tests/Rice2k.Tests.csproj --configuration Release --no-build
```

All security/regression tests must pass. See GitHub Issue #6.

### 3. Restart/round-trip persistence test — **PENDING**

For each supported format, create protected data, close Rice2k, restart it, and successfully recover/verify the data using the intended credentials. Include `.r2kenc` v1/v2/v3, `.r2kkey`, `.r2krecovery`, `.r2kvault`, `.r2kid`, `.r2kpub`, and `.r2ksig` where applicable.

### 4. Negative/failure testing — **PENDING VALIDATED RUN**

Test wrong passwords, wrong key files, wrong identities, damaged/truncated containers, modified signatures, missing/reordered data, output collisions, cancellation, interrupted writes and recovery artifacts. Source/original data must remain unchanged.

### 5. Large-file and large-vault tests — **PENDING**

Run multi-gigabyte streaming tests and the vault benchmark harness. Record peak memory, throughput, failures and recovery behavior.

### 6. App Lock/privacy lifecycle acceptance — **PENDING**

Validate startup lock, manual lock, minimize lock, Windows session lock, inactivity lock, wrong passwords, lock-screen Exit, Privacy Mode, clipboard auto-clear and optional history behavior on Windows.

### 7. Accessibility acceptance — **PENDING**

Complete Encrypt and Decrypt without a mouse. Review Narrator/screen-reader announcements, tab/focus order, 125/150/200% Windows scaling, High Contrast toggled before and during runtime, reduced motion and multi-monitor behavior.

### 8. Portable Beta package — **PENDING**

Create a self-contained Windows x64 portable ZIP from the validated commit. Include version, license, README, SHA-256 checksum and clear preview/beta warning.

### 9. Installer/signing/release-candidate work — **PENDING**

See GitHub Issue #7. Installer/uninstaller, Authenticode/code signing, artifact checksums and safe signed update design are RC/Stable gates.

### 10. Independent review and stable release — **PENDING**

Before 1.0, obtain review of cryptographic construction, container parsing, recovery behavior, public-key identity/trust wording, update/signing design and release documentation.

## Build/test result log

Append each attempted validation; do not erase failures.

| Date (UTC) | Version/commit | Environment | Stage | Result | Notes |
|---|---|---|---|---|---|
| 2026-09-23 | 0.6.0-preview.2/.3-era commits | GitHub hosted Actions | Runner assignment | Failed before execution | Multiple jobs were created with zero executed steps; no compiler/test result was produced. |
| 2026-09-23 | 0.6.0-preview.4 | Current ChatGPT working container | SDK availability | Blocked | `dotnet` SDK is not installed in the working container, so it cannot be used as the supported Windows build environment. |

## Bug/release records

- Version history: [`RELEASE-HISTORY.md`](RELEASE-HISTORY.md)
- Known errors/bugs/fixes: [`KNOWN-ISSUES.md`](KNOWN-ISSUES.md)
- Detailed change log: [`../CHANGELOG.md`](../CHANGELOG.md)
- Security test work: GitHub Issue #6
- Packaging/release work: GitHub Issue #7

## Promotion rules

### Promote to Beta only when
- Steps 1 and 2 pass on supported Windows/.NET 10;
- critical round-trip/negative tests in Steps 3–4 pass;
- no open data-loss/security Blocker exists.

### Promote to Release Candidate only when
- Beta gates remain green;
- Steps 5–9 are substantially complete;
- accessibility/privacy acceptance is complete;
- distributable artifacts are reproducible and signed/verified where required.

### Promote to Stable 1.0 only when
- every documented 1.0 release criterion in `ROADMAP.md` is satisfied;
- independent review is complete;
- release artifacts, checksums/signatures and recovery documentation are finalized.
