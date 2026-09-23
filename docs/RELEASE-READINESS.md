# Rice2k Encryption Software — Release Readiness

This checklist is the stabilization gate between the current development preview and a Beta/Release Candidate/Stable build.

Current source version: **0.6.0-preview.4**

## Stop-feature-creep rule

During stabilization, new major features should not be added unless they are required to fix a release blocker, security problem, data-loss risk, accessibility blocker, or build/test failure. Cosmetic work can wait.

## Beta readiness checklist

### 1. Build the complete solution on Windows + .NET 10 — **BLOCKED ON HOSTED RUNNER / LOCAL WINDOWS VALIDATION AVAILABLE**

Recommended command:

```powershell
PowerShell -ExecutionPolicy Bypass -File .\tools\Validate-Rice2k.ps1
```

The validator now runs a static WPF preflight first, followed by the equivalent of:

```powershell
dotnet --info
dotnet restore Rice2kEncryption.sln
dotnet build Rice2kEncryption.sln --configuration Release --no-restore -p:ContinuousIntegrationBuild=true
dotnet test tests\Rice2k.Tests\Rice2k.Tests.csproj --configuration Release --no-build
```

Pass criteria:
- static WPF XAML/partial-class preflight succeeds;
- the pinned .NET 10 SDK environment is recorded;
- `dotnet restore` succeeds;
- Release build succeeds for the complete solution;
- no unresolved compile errors;
- warnings are reviewed and security/data-safety-relevant warnings are fixed before Beta;
- exact SDK/runtime and build result are recorded below.

Current result: **Not passed yet.** A temporary two-platform hosted-runner probe demonstrated that both `ubuntu-latest` and `windows-latest` jobs can be created but fail before any step executes, with no logs. This confirms `R2K-CI-001` is broader hosted-runner availability for this repository/account, not a Windows image or Rice2k compiler result.

During the static stabilization sweep, a genuine compile blocker was found and fixed: duplicate `SettingsSearchPanel` partial members in `SettingsSearchPanel.PrivacyHistory.cs` and `SettingsSearchPanel.PrivacyExtras.cs`. The obsolete duplicate partial was removed and the fix is recorded as `R2K-SET-004`.

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

Each attempt gets the static WPF preflight result plus separate SDK/restore/build/test logs and `VALIDATION-SUMMARY.md`. `artifacts/` is git-ignored by default. Successful and failed attempts should be summarized back into this table when they are used as release evidence.

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
