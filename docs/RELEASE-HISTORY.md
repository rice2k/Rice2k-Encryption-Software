# Rice2k Encryption Software — Release History

This file is the permanent version ledger for Rice2k Encryption Software. Every development preview, beta, release candidate, and stable release should be recorded here even if no binary package was published.

> A version appearing here means the source reached that development milestone. It does **not** imply that the build was compiled, tested, security-reviewed, signed, or recommended for production use unless the entry explicitly says so.

## Version ledger

| Version / milestone | Status | Major scope | Validation at the time |
|---|---|---|---|
| 0.1 development milestone | Historical development | Initial Windows/WPF foundation, text/file encryption foundation, XChaCha20-Poly1305, Argon2id, `.r2kenc`, integrity tools | Source-development milestone; no production validation claimed |
| 0.2 development milestone | Historical development | Guided Encrypt/Decrypt workflows, progress/status UX, preflight checks, batch queue, drag/drop, first-run help and settings foundation | Source-development milestone; no production validation claimed |
| 0.3.0-preview.1 | Development preview | Key Manager, `.r2kkey`, recovery packages, password + key-file protection | Source-reviewed; full supported Windows build/test gate not completed |
| 0.4.0-preview.1 | Development preview | `.r2kvault`, Vault Browser, atomic vault mutation/recovery flow, folder protection | Source-reviewed; full supported Windows build/test gate not completed |
| 0.5.0-preview.1 | Development preview | Identities, saved public contacts, multi-recipient encryption and detached signatures | Source-reviewed; full supported Windows build/test gate not completed |
| 0.6.0-preview.1 | Development preview | Privacy Mode, protected clipboard foundation and privacy settings | Source-reviewed; full supported Windows build/test gate not completed |
| 0.6.0-preview.2 | Development preview | Authenticated App Lock, startup/minimize/session/inactivity locking, accessibility foundations | Source-reviewed; full supported Windows build/test gate not completed |
| 0.6.0-preview.3 | Development preview | App Lock hardening, Security Center, additional keyboard/high-contrast work | Source-reviewed; GitHub validation attempts still executed zero workflow steps |
| 0.6.0-preview.4 | Current development preview | Feature stabilization: optional recent-file/redacted activity history, reduced-motion/live High Contrast behavior, privacy-safe notifications, lifecycle/cancellation hardening across secret-owning windows, safe main-window exit/integrity handling, clipboard/privacy cleanup reporting, WPF/partial-class static preflight, and fail-closed local validation tooling | **Stabilization/build validation in progress. Supported Windows Release build and full test gates have not passed yet. Not production-ready.** |

## Release status definitions

- **Development milestone** — source functionality exists, but no packaged build is implied.
- **Development preview** — intended for development/testing only; do not rely on it as the sole copy of important data.
- **Beta** — full solution builds and automated tests pass on the supported Windows toolchain; suitable for broader testing on copies of real data.
- **Release candidate** — beta gates plus packaging, upgrade/install testing, accessibility acceptance and release-security review are complete.
- **Stable** — all documented 1.0 release gates are complete and the release is signed/published with checksums.

## Versioning policy going forward

For every new version:

1. Update the application version in `src/Rice2k.App/Rice2k.App.csproj`.
2. Add the version to this file.
3. Add detailed changes to `CHANGELOG.md`.
4. Update `docs/KNOWN-ISSUES.md` with bugs discovered/fixed in that version.
5. Record build/test results in `docs/RELEASE-READINESS.md`.
6. For packaged releases, create a Git tag/GitHub Release and attach checksums/signature information.
7. Never mark a version as Beta/RC/Stable unless its required validation gates actually passed.
