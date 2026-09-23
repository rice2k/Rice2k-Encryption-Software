# Rice2k Encryption Software — Known Issues and Bug Register

This is the permanent engineering register for errors, bugs, release blockers, and notable fixes. Do not remove historical entries after they are fixed; change their status to **Fixed** and record the version/commit where appropriate.

## Severity

- **Blocker** — prevents a Beta/RC/Stable release or risks data/security correctness.
- **High** — major feature/security/reliability defect.
- **Medium** — important usability/compatibility defect with a workaround.
- **Low** — cosmetic/documentation/polish issue.

## Open issues

| ID | Severity | Status | First noted | Description / required fix |
|---|---|---|---|---|
| R2K-VAL-001 | Blocker | Open | pre-0.6 | Complete solution has not yet completed a successful supported Windows + .NET 10 Release build. Step 1 of stabilization is to make this pass and record the result. |
| R2K-TEST-001 | Blocker | Open | pre-0.6 | Source-controlled security/regression suite has not yet completed a successful supported Windows/.NET 10 run. Tracked in GitHub Issue #6. |
| R2K-CI-001 | High | Open / infrastructure | 0.6.0-preview.2 | Hosted Actions checks continue to fail before executing a step. Automatic push validation has repeatedly produced jobs with no assigned runner/steps/logs. This still does not constitute a compiler result. |
| R2K-A11Y-001 | Blocker for RC/Stable | Open | 0.6 | Full Windows keyboard-only Encrypt/Decrypt acceptance run has not been performed. |
| R2K-A11Y-002 | Blocker for RC/Stable | Open | 0.6 | Narrator/screen-reader, text scaling/DPI, multi-monitor, High Contrast and reduced-motion visual acceptance review remains unexecuted on Windows. |
| R2K-PERF-001 | Blocker for Stable | Open | 0.4 | Multi-gigabyte file/vault performance and memory-use release-gate runs remain unexecuted. |
| R2K-PKG-001 | Blocker for RC/Stable | Open | pre-1.0 | No signed portable/installer release path has completed packaging, install/uninstall and Authenticode/signature verification tests. Tracked in GitHub Issue #7. |
| R2K-REVIEW-001 | Blocker for Stable | Open | pre-1.0 | Stable cryptographic/file-format/identity trust design has not had independent security review. |

## Fixed / mitigated issues

| ID | Severity | Fixed in | Description / resolution |
|---|---|---|---|
| R2K-KEY-001 | High / secret lifecycle | 0.6.0-preview.4 stabilization source | Key Manager allowed its window to close while asynchronous import/export was active. A late import could add a newly decrypted secret key after the normal closing cleanup had already disposed the session keys. Import/export now use a cancellable operation lifecycle; close requests cancel and wait for cleanup, imported keys are disposed if cancellation/failure wins, and sensitive package password fields are cleared on cancellation/completion. |
| R2K-FOLDER-001 | Medium | 0.6.0-preview.4 stabilization source | Closing Protect Folder while work was active requested safe cancellation but discarded the user's close intent, leaving the window open after cleanup. The window now remembers the close request, cancels safely, and closes automatically after the active operation reaches its cleanup boundary. |
| R2K-BUILD-001 | High compile risk | 0.6.0-preview.4 stabilization source | The WPF project also enables WinForms for desktop notifications while implicit usings are enabled. WinForms implicit namespaces can collide with WPF names such as `Application`, `Clipboard`, `Color`, `Point`, and `Size`. The project now explicitly removes `System.Drawing` and `System.Windows.Forms` from implicit/global usings while keeping WinForms enabled for fully-qualified notification types; the static preflight enforces this isolation. |
| R2K-UI-003 | Low | 0.6.0-preview.4 stabilization source | The password + key-file workflows still wrote milestone-era `v0.3-dev` text into the global status bar, while the earlier normalizer only replaced `v0.2-dev`. The version-status layer now normalizes any legacy `v…-dev` token to the current assembly informational version. |
| R2K-VAL-002 | High / release tooling | 0.6.0-preview.4 stabilization source | `Validate-Rice2k.ps1 -SkipTests` could report PASS after the static preflight even when a fatal validation exception occurred before a failing stage result was recorded (for example, when the .NET SDK was missing). Added an explicit fail-closed validation flag and failure reason so any caught fatal validation error forces a non-zero final result. |
| R2K-SET-004 | Blocker / compile | 0.6.0-preview.4 stabilization source | Two Settings partial files defined the same `OnInitialized`, `ReduceMotion_Changed`, and `ReviewStoredHistory_Click` members, which would cause duplicate-member compiler errors. Removed the obsolete `SettingsSearchPanel.PrivacyHistory.cs`; the consolidated `SettingsSearchPanel.PrivacyExtras.cs` remains as the single implementation. |
| R2K-SET-003 | Medium | 0.6.0-preview.4 stabilization source | Optional-history checkboxes were wired to the broad Privacy handler in XAML and also received a dedicated history handler at runtime, causing duplicate settings writes and an intermediate callback with stale history values. The legacy handlers are now detached before the dedicated history handlers are attached. |
| R2K-UI-001 | Low | 0.6.0-preview.4 stabilization source | Legacy `MainWindow.xaml.cs` contains milestone-era `v0.2-dev` status suffixes. The version-status partial now normalizes those user-visible strings to the actual assembly informational version. Large-file source decomposition/cleanup can happen later without exposing the stale version to users. |
| R2K-UI-002 | Medium | 0.6.0-preview.4 stabilization source | Replaced user-visible stale drag/drop behavior via a routed `OnDrop` override: one normal file routes to Encrypt, one `.r2kenc` routes to Decrypt, folders/multiple normal files open the implemented Batch Queue, and multiple encrypted containers receive accurate guidance instead of claiming batch support is a future milestone. |
| R2K-SET-001 | High compile risk | 0.6.0-preview.4 source | Settings XAML exposed `ReduceMotion_Changed` and `ReviewStoredHistory_Click` without corresponding handlers. Added code-behind integration. |
| R2K-SET-002 | Medium | 0.6.0-preview.4 source | Optional-history checkboxes existed in Settings but were not loaded/persisted by the code-behind. Added explicit settings synchronization and persistence. |
| R2K-HIST-001 | Privacy | 0.6.0-preview.4 source | Persistent activity needed a privacy-safe storage policy. Added explicit opt-in action-only redaction, bounded storage, review/clear UI and tests. |
| R2K-HC-001 | Accessibility | 0.6.0-preview.4 source | High Contrast palette adaptation was startup-only. Added shared mutable brushes and runtime reaction to Windows High Contrast changes. |
| R2K-LOCK-001 | High | 0.6.0-preview.3 source | Unexpected lock-dialog failure could risk leaving presentation blanked. Restoration moved to a guarded `finally` path with per-window failure isolation. |
| R2K-LOCK-002 | High | 0.6.0-preview.3 source | App Lock setup/removal could become inconsistent if settings persistence failed. Added rollback/recoverable orphan-state handling. |
| R2K-CLIP-001 | Privacy | 0.6.0-preview.1 source | Independent clipboard timers across windows could clear a same-value newer copy too early. Replaced with app-wide clipboard generation and exact-value checks. |

## Recording a new bug

When a bug is found:

1. Assign the next `R2K-<AREA>-NNN` identifier.
2. Record severity, affected version and reproduction details here.
3. Create/link a GitHub Issue for Blocker/High defects or any item requiring multi-step work.
4. Record whether encrypted/source data can be affected.
5. Add a regression test when practical before marking the bug fixed.
6. Move the entry to **Fixed / mitigated issues** only after the source change exists; do not claim runtime validation until the relevant test/build actually runs.
