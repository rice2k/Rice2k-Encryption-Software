# Rice2k Large-File Encryption Validation

`Rice2k.FileBench` is the manual correctness and performance harness for password-only `.r2kenc` streaming.

It exists because multi-gigabyte validation is intentionally **not** part of every normal xUnit run. A release-gate run can require several gigabytes of temporary disk space and substantial time, while the ordinary security suite should remain practical for routine development.

## What the harness validates

The harness:

1. Creates a random source file using a reusable 1 MiB buffer and flushes it to disk.
2. Computes a baseline source SHA-256 by streaming the file.
3. Encrypts the source with `FileEncryptionService` using normal `R2KENC01` chunked encryption.
4. Requires a non-empty encrypted container and authenticates the complete container using `VerifyEncryptedFileAsync`.
5. Decrypts the container to a separate restored file.
6. Computes the restored SHA-256 by streaming the file.
7. Re-hashes the original source after all encryption/decryption work.
8. Requires the final source hash to match the baseline source hash and the restored hash to match that same baseline.
9. Requires source/restored byte lengths to match the requested test length.
10. Reports source generation, encryption, verification, decryption, and hashing throughput.
11. Samples managed-memory usage during the run and reports the process peak working set.
12. Emits Rice2k informational version, UTC timestamps, OS, .NET runtime, process architecture, and processor count so captured logs are self-identifying.
13. Removes generated files by default after the run.

A successful benchmark demonstrates correctness for the tested build, machine, file size, and run. It is **not** a cryptographic audit and does not replace the xUnit security/regression suite.

## Requirements

- Windows with the supported .NET 10 SDK.
- Enough free local disk space for the plaintext source, encrypted container, restored output, and safety margin.
- A local filesystem is preferred for repeatable throughput results.
- Close unrelated disk-intensive applications for release-gate measurements.

The harness performs a free-space check before generating test data. It estimates approximately three times the selected plaintext size plus a 512 MiB safety margin. Its run directory includes a timestamp and random suffix to avoid collisions between runs started within the same second.

## Quick smoke run

From the repository root:

```powershell
dotnet run --project .\tools\Rice2k.FileBench\Rice2k.FileBench.csproj --configuration Release -- --file-size-mb 256
```

This is useful for confirming that the harness runs, but it does **not** satisfy the multi-gigabyte release gate.

## Multi-gigabyte release gate

Use at least 2 GiB:

```powershell
dotnet run --project .\tools\Rice2k.FileBench\Rice2k.FileBench.csproj --configuration Release -- --file-size-mb 2048
```

A stronger pre-release run can use 4 GiB when disk and time permit:

```powershell
dotnet run --project .\tools\Rice2k.FileBench\Rice2k.FileBench.csproj --configuration Release -- --file-size-mb 4096
```

The harness accepts `--file-size-mb` values from 1 through 32768.

## Integrated validation run

After the normal Release build and security/regression suite, `Validate-Rice2k.ps1` can run FileBench and VaultBench and capture both logs in the same timestamped validation folder:

```powershell
PowerShell -ExecutionPolicy Bypass -File .\tools\Validate-Rice2k.ps1 -RunBenchmarks
```

The default integrated FileBench workload is 2,048 MiB. Override it when needed:

```powershell
PowerShell -ExecutionPolicy Bypass -File .\tools\Validate-Rice2k.ps1 `
  -RunBenchmarks `
  -FileBenchmarkMb 4096
```

When `-RunBenchmarks` is omitted, the validation summary explicitly states that the run cannot by itself satisfy release-readiness Step 5. If a requested benchmark exits non-zero, the validation attempt fails.

## Preserve artifacts for investigation

By default the temporary benchmark directory is deleted. Add `--keep` when investigating a direct harness failure or when release evidence requires the generated files to remain temporarily:

```powershell
dotnet run --project .\tools\Rice2k.FileBench\Rice2k.FileBench.csproj --configuration Release -- --file-size-mb 2048 --keep
```

The console prints the retained working directory.

Do not commit generated plaintext, encrypted benchmark containers, or restored multi-gigabyte files to the repository.

## Required PASS conditions

For a release-gate run, all of the following must be true:

- The harness exits successfully.
- The encrypted container is non-empty and authenticates successfully.
- Decryption completes without an incomplete `.partial` output.
- Restored length equals the requested source length.
- The original source remains present with the requested length.
- Final source SHA-256 exactly matches the baseline source SHA-256.
- Restored SHA-256 exactly matches the same baseline source SHA-256.
- Managed/process memory remains reasonably bounded relative to file size rather than scaling with the complete plaintext size.
- No unexplained `.partial`, recovery, or destination-collision artifacts remain.

There is intentionally no universal throughput threshold because storage hardware varies substantially. Record throughput and memory so regressions can be compared against prior runs on comparable hardware.

## Suggested release record

The harness now prints Rice2k version, OS/.NET/architecture, processor count, timestamps, hashes, rates, and memory observations directly. Also record the commit and hardware details that are not fully represented by the process environment output:

```text
Rice2k version / commit:
Windows version:
.NET SDK version:
CPU model:
RAM:
Storage model/type / volume / filesystem:
Test plaintext size:
Source generation time / rate:
Encryption time / rate:
Full verification time / rate:
Decryption time / rate:
Baseline source SHA-256:
Final source SHA-256:
Restored SHA-256:
Peak managed memory observed:
Process peak working set:
Source preservation result: PASS / FAIL
Correctness result: PASS / FAIL
Notes:
```

## Current project status

The presence of `Rice2k.FileBench` does **not** by itself close the large-file release gate. `R2K-PERF-001` remains open until a supported Windows build completes the required multi-gigabyte run and the result is recorded.
