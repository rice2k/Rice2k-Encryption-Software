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
| R2K-CI-001 | High | Open / infrastructure | 0.6.0-preview.2 | Prior GitHub-hosted validation attempts created jobs but executed zero steps. Automatic push validation is being enabled during stabilization so this can be re-tested. |
| R2K-UI-001 | Low | Open | 0.6.0-preview.4 stabilization | Legacy `MainWindow.xaml.cs` still contains hard-coded `v0.2-dev` status text after successful operations. Replace with dynamic assembly version/status text. |
| R2K-UI-002 | Medium | Open | 0.6.0-preview.4 stabilization | Legacy drag/drop messaging still says multi-file/folder queue support is a future milestone even though Batch Queue is implemented. Route the drop to the implemented queue or update the message/behavior. |
| R2K-A11Y-001 | Blocker for RC/Stable | Open | 0.6 | Full Windows keyboard-only Encrypt/Decrypt acceptance run has not been performed. |
| R2K-A11Y-002 | Blocker for RC/Stable | Open | 0.6 | Narrator/screen-reader, text scaling/DPI, multi-monitor, High Contrast and reduced-motion visual acceptance review remains unexecuted on Windows. |
| R2K-PERF-001 | Blocker for Stable | Open | 0.4 | Multi-gigabyte file/vault performance and memory-use release-gate runs remain unexecuted. |
| R2K-PKG-001 | Blocker for RC/Stable | Open | pre-1.0 | No signed portable/installer release path has completed packaging, install/uninstall and Authenticode/signature verification tests. Tracked in GitHub Issue #7. |
| R2K-REVIEW-001 | Blocker for Stable | Open | pre-1.0 | Stable cryptographic/file-format/identity trust design has not had independent security review. |

## Fixed issues

| ID | Severity | Fixed in | Description / resolution |
|---|---|---|---|
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
6. Move the entry to **Fixed issues** only after the fix exists in source; do not claim runtime validation until the relevant test/build actually runs.
