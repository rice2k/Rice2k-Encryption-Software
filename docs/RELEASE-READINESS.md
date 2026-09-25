# Rice2k Encryption Software — Release Readiness

This checklist is the stabilization gate between the current development preview and a Beta/Release Candidate/Stable build.

Current source version: **0.6.0-preview.4**

## Stop-feature-creep rule

During stabilization, new major features should not be added unless they are required to fix a release blocker, security problem, data-loss risk, accessibility blocker, or build/test failure. Cosmetic work can wait.

## Beta readiness checklist

### 1. Build the complete solution on Windows + .NET 10 — **BLOCKED ON HOSTED RUNNER / LOCAL WINDOWS VALIDATION AVAILABLE**

Recommended command from Windows PowerShell 5.1 or a newer PowerShell edition:

```powershell
PowerShell -ExecutionPolicy Bypass -File .\tools\Validate-Rice2k.ps1
```

The validator now runs a static WPF/source preflight first, followed by the equivalent of:

```powershell
dotnet --info
dotnet restore Rice2kEncryption.sln
dotnet build Rice2kEncryption.sln --configuration Release --no-restore -p:ContinuousIntegrationBuild=true
dotnet test tests\Rice2k.Tests\Rice2k.Tests.csproj --configuration Release --no-build
```

The source preflight checks WPF/WinForms namespace isolation, XAML handler resolution, duplicate XAML-handler definitions, duplicate lifecycle overrides, duplicate ordinary partial-class methods using canonicalized parameter type/modifier signatures, and the known overflow-prone chunk ceiling-division pattern. It is an early fail-fast guard only; the compiler remains authoritative.

Pass criteria:
- static WPF/source preflight succeeds;
- the pinned .NET 10 SDK environment is recorded;
- `dotnet restore` succeeds;
- Release build succeeds for the complete solution;
- no unresolved compile errors;
- warnings are reviewed and security/data-safety-relevant warnings are fixed before Beta;
- exact SDK/runtime and build result are recorded below.

Current result: **Not passed yet.** A temporary two-platform hosted-runner probe demonstrated that both `ubuntu-latest` and `windows-latest` jobs can be created but fail before any step executes, with no logs. This confirms `R2K-CI-001` is broader hosted-runner availability for this repository/account, not a Windows image or Rice2k compiler result. The current validation workflow is manual-dispatch only so runner infrastructure failures do not create misleading red checks for every stabilization commit.

During the static stabilization sweep, a genuine compile blocker was found and fixed: duplicate `SettingsSearchPanel` partial members in `SettingsSearchPanel.PrivacyHistory.cs` and `SettingsSearchPanel.PrivacyExtras.cs`. The obsolete duplicate partial was removed and the fix is recorded as `R2K-SET-004`.

### 2. Execute the complete automated security/regression suite — **NOT STARTED AS A VALIDATED RUN**

```powershell
dotnet test tests/Rice2k.Tests/Rice2k.Tests.csproj --configuration Release --no-build
```

All security/regression tests must pass. See GitHub Issue #6.

Source preparation now includes round trips, corruption/truncation/resource-limit checks, cancellation/no-overwrite behavior, deterministic short-read handling, destination races, malformed `R2KENC01/02/03` header matrices, malformed `.r2kkey`/`.r2krecovery`/private `.r2kid` package matrices, vault fault injection/recovery tests, identity/signature/contact tests, Settings/App Lock/privacy persistence tests, protected-clipboard generation/timer tests, and bounded same-handle file-read tests. **These source files do not satisfy this gate until the supported Windows/.NET 10 test run actually executes and passes.**

### 3. Restart/round-trip persistence test — **PENDING**

For each supported format, create protected data, close Rice2k, restart it, and successfully recover/verify the data using the intended credentials. Include `.r2kenc` v1/v2/v3, `.r2kkey`, `.r2krecovery`, `.r2kvault`, `.r2kid`, `.r2kpub`, and `.r2ksig` where applicable.

Source-level restart preparation now explicitly constructs fresh service instances across every format named above: `R2KENC01` decrypts with a new file service; `R2KENC02` reloads its persisted `.r2kkey`; `R2KENC03` reloads its persisted private `.r2kid`; Secure Vault disposes the original session then reopens/verifies/extracts through a new vault service; key, recovery, private/public identity, and signature tests likewise reopen their persisted artifacts through fresh service objects. Settings, App Lock credentials, and optional privacy history also reload through new service instances while stale interrupted temp files are ignored. **The real application close/restart acceptance sequence for every supported format is still required on Windows before this step passes.**

### 4. Negative/failure testing — **PENDING VALIDATED RUN**

Test wrong passwords, wrong key files, wrong identities, damaged/truncated containers, modified signatures, missing/reordered data, output collisions, cancellation, interrupted writes and recovery artifacts. Source/original data must remain unchanged.

Source-controlled negative coverage has been expanded to include malformed-header/resource matrices for all three `.r2kenc` generations and encrypted key/recovery/private-identity packages, deterministic late destination collisions on both encrypt and decrypt finalization for `R2KENC01/02/03`, late destination collision during Secure Vault extraction, cancellation cleanup, package no-overwrite behavior, source snapshot/chunk-count invariants, Secure Vault recovery/fault injection, and parser limits. The late-collision tests require the competing destination to remain byte-for-byte under the other writer's control while Rice2k cleans only its own temporary output. **This remains pending until the complete supported Windows test run and manual recovery/interruption acceptance steps execute successfully.**

### 5. Large-file and large-vault tests — **PENDING**

Run multi-gigabyte streaming tests and the vault benchmark harness. Record peak memory, throughput, failures and recovery behavior.

`tools/Rice2k.FileBench` and `tools/Rice2k.VaultBench` provide source-controlled correctness/throughput harnesses. Both now emit Rice2k version plus OS/.NET/architecture/CPU metadata, use collision-resistant working directories, report managed and process peak memory, and fail closed on correctness/preservation mismatches. FileBench records the original SHA-256, performs full encrypted-container verification, decrypts, re-hashes the original source, and requires both source preservation and restored SHA-256 equality. VaultBench preflights disk space, hashes the entire source set before vault work, validates manifest entry count and total plaintext bytes, fully verifies the vault, extracts and checks a file, then re-hashes every original source file to prove vault operations did not modify them. A file-encryption release-gate run should use at least a 2 GiB source; representative multi-gigabyte and many-file vault workloads must also be executed and recorded. **The existence of these hardened harnesses does not satisfy this step until those supported Windows runs complete successfully.**

### 6. App Lock/privacy lifecycle acceptance — **PENDING**

Validate startup lock, manual lock, minimize lock, Windows session lock, inactivity lock, wrong passwords, lock-screen Exit, Privacy Mode, clipboard auto-clear and optional history behavior on Windows.

Source-level privacy preparation now includes App Lock credential replacement/removal and hostile-parameter tests, fresh-instance persistence for App Lock/settings/optional redacted history, and deterministic protected-clipboard tests using an internal adapter/delay boundary. Those clipboard tests prove that an older Rice2k auto-clear timer cannot clear a newer Rice2k copy, a timer will not clear clipboard text that changed externally, Clear Now invalidates older timers, unchanged values clear through the expected-value check, and disabled auto-clear schedules no timer. **These tests exercise Rice2k's generation/timer logic only. Real Windows clipboard ownership, WPF dispatcher behavior, startup/minimize/session-lock/inactivity triggers, lock-screen focus/Exit, and Privacy Mode lifecycle still require manual Windows acceptance before Step 6 can pass.**

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
| 2026-09-23 | 0.6.0-preview.2/.3-era commits | GitHub hosted Actions | Runner assignment | Failed before execution | Multiple earlier jobs were created with zero executed steps; no compiler/test result was produced. |
| 2026-09-23 | 0.6.0-preview.4 | Current ChatGPT working container | SDK availability | Blocked | `dotnet` SDK is not installed and outbound DNS/download is unavailable in the working container, so it cannot be converted into the supported Windows build environment. |
| 2026-09-23 | commit `3d4764f0e2b623c18e6855be755b8940f0a702e6` | GitHub Actions `windows-latest` | Automatic push validation | Failed before execution | Run `35907363363`, job `107338276220`; job completed in ~2 seconds with zero steps and no runner execution. |
| 2026-09-23 | commit `1e3853afd5062842da77595b07f7c12e70080061` | GitHub Actions `windows-latest` | Automatic push validation | Failed before execution | Run `35907842065`, job `107339876446`; `runner_id: 0`, empty runner name/group, `steps: []`; no compiler/test result. |
| 2026-09-23 | commit `09e73f7629349fd7ada88a716def989b3ecd13cc` | GitHub Actions `windows-latest` | Automatic push validation | Failed before execution | Run `35912484001`, job `107355544383`; no logs or executed steps. |
| 2026-09-23 | commit `7038c5956e0fb9c7e48c7652e6b53afeff5215b6` | GitHub Actions `ubuntu-latest` + `windows-latest` | Cross-platform hosted-runner probe | Failed before execution on both | Run `35913172673`; Ubuntu job `107357889592` and Windows job `107357889952` both returned no logs and no steps. This isolates the blocker to hosted-runner availability rather than Windows-only capacity/image behavior. |

## Local validation artifact policy

`tools/Validate-Rice2k.ps1` writes each local attempt to:

```text
artifacts\validation\<timestamp>\
```

Each attempt gets the static WPF/source preflight result plus separate SDK/restore/build/test logs and `VALIDATION-SUMMARY.md`. `artifacts/` is git-ignored by default. The summary-generation path is compatible with Windows PowerShell 5.1; successful and failed attempts should be summarized back into this table when they are used as release evidence.

## Bug/release records

- Version history: [`RELEASE-HISTORY.md`](RELEASE-HISTORY.md)
- Known errors/bugs/fixes: [`KNOWN-ISSUES.md`](KNOWN-ISSUES.md)
- Release recording rules: [`RELEASE-PROCESS.md`](RELEASE-PROCESS.md)
- Detailed change log: [`../CHANGELOG.md`](../CHANGELOG.md)
- Stabilization umbrella: GitHub Issue #8
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
