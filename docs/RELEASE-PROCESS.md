# Rice2k Encryption Software — Release Recording Process

Every development preview, beta, release candidate and stable release must leave a permanent audit trail in GitHub.

## For every version

1. Update the assembly/package version.
2. Add an entry to `docs/RELEASE-HISTORY.md`.
3. Add detailed user/developer-facing changes to `CHANGELOG.md`.
4. Update `docs/KNOWN-ISSUES.md`:
   - newly discovered bugs/errors;
   - bugs fixed in the version;
   - release blockers still open.
5. Update `docs/RELEASE-READINESS.md` with build/test attempts and results.
6. Update relevant GitHub Issues with reproduction details, fix status and validation status.
7. When packaged distribution begins, create a Git tag and GitHub Release for each externally distributed Beta/RC/Stable build and attach checksums/signature information.

## Bug records

Do not erase bugs after fixing them. Preserve:
- the first affected/noted version;
- severity;
- symptoms/reproduction;
- data/security impact;
- fix version;
- regression-test coverage;
- whether the fix has been executed/validated or is only source-reviewed.

## Failed releases/builds

Failed build, test and release attempts remain in the readiness log. A failed attempt should never be rewritten as a pass simply because a later build succeeds.

## Version promotion

- Preview -> Beta only after the supported Windows build and core automated security tests pass.
- Beta -> RC only after large-file, failure/recovery, privacy and accessibility acceptance work passes and packaging is ready.
- RC -> Stable only after independent review, signed distribution and all 1.0 gates are complete.
