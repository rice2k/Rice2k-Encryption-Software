# Secure Vault benchmarking

Rice2k Encryption Software includes a console benchmark/correctness project at `tools/Rice2k.VaultBench`.

Its purpose is to produce repeatable **local** measurements for `.r2kvault` creation, add/update, full verification, and extraction without inventing performance claims in documentation. It is also designed to fail closed when source-preservation or restore-correctness checks fail.

## Requirements

- Windows
- .NET 10 SDK
- Enough free disk space for the generated plaintext files, active vault, pending vault during mutation, temporary recovery copy during replacement, and extracted correctness sample

The harness performs a disk-space preflight before generating the workload. Do not run the benchmark in a folder containing important data. The tool creates its own timestamp + random-suffix directory beneath the current user's temporary directory and deletes it at the end unless `--keep` is supplied.

## Default run

```powershell
dotnet run --project .\tools\Rice2k.VaultBench\Rice2k.VaultBench.csproj -c Release
```

Default workload:

- 4 files
- 64 MiB per file
- 256 MiB total plaintext

The benchmark now reports and validates:

- Rice2k informational version;
- UTC start/completion times;
- OS, .NET runtime, process architecture, and processor count;
- empty-vault creation time;
- add + pending/final verification time and plaintext throughput;
- authenticated manifest entry count and total plaintext bytes;
- independent full-vault verification time and throughput;
- first-file extraction time and throughput;
- SHA-256 equality between the extracted file and its pre-vault source baseline;
- SHA-256 preservation of **every original source file** after vault operations;
- final vault size and authenticated sequence number;
- peak managed memory observed by the harness;
- process peak working set.

The harness flushes generated source files to disk before protection work begins. A correctness or source-preservation mismatch throws and causes a non-zero process exit.

## Integrated validation run

`tools/Validate-Rice2k.ps1` can run both large-data harnesses after a successful Release build/test pass:

```powershell
PowerShell -ExecutionPolicy Bypass -File .\tools\Validate-Rice2k.ps1 -RunBenchmarks
```

The default integrated benchmark profile is:

- FileBench: 2,048 MiB plaintext
- VaultBench: 4 files × 512 MiB = 2,048 MiB plaintext

Override the sizes when a different release-gate profile is required:

```powershell
PowerShell -ExecutionPolicy Bypass -File .\tools\Validate-Rice2k.ps1 `
  -RunBenchmarks `
  -FileBenchmarkMb 4096 `
  -VaultBenchmarkFileSizeMb 1024 `
  -VaultBenchmarkFiles 8
```

The validator captures FileBench and VaultBench console output in the same timestamped `artifacts\validation\...` folder as the build/test logs. Benchmark failures make that validation attempt fail. When `-RunBenchmarks` is omitted, the summary explicitly states that release-readiness Step 5 has **not** been satisfied.

## Larger direct runs

Example 4 GiB plaintext run:

```powershell
dotnet run --project .\tools\Rice2k.VaultBench\Rice2k.VaultBench.csproj -c Release -- --file-size-mb 1024 --files 4
```

Example many-file run:

```powershell
dotnet run --project .\tools\Rice2k.VaultBench\Rice2k.VaultBench.csproj -c Release -- --file-size-mb 16 --files 1000
```

Keep generated files for manual inspection:

```powershell
dotnet run --project .\tools\Rice2k.VaultBench\Rice2k.VaultBench.csproj -c Release -- --file-size-mb 256 --files 8 --keep
```

## Release-gate matrix

Before a stable 1.0 release, record results for at least these workload shapes on the minimum supported Windows hardware profile and a representative modern PC:

| Workload | Purpose |
|---|---|
| 4 × 64 MiB | quick regression baseline |
| 1 × 4 GiB | large single-file streaming |
| 8 × 1 GiB | multi-gigabyte vault mutation/verification |
| 1,000 × 1 MiB | many-entry manifest/record overhead |
| 10,000 × small files | path/search/manifest scaling |

For each run record:

- Rice2k commit SHA;
- Windows version;
- CPU model;
- RAM;
- storage type and filesystem;
- .NET SDK/runtime version;
- workload parameters;
- add/verify/extract times;
- peak managed memory and process working set;
- whether source-preservation and restored-content correctness checks passed;
- whether `.pending` / `.backup` cleanup completed normally.

Some of this information is emitted automatically by the harness; CPU model, installed RAM, storage model/type, filesystem, and commit SHA should still be recorded with the release evidence when they are not present in the captured environment log.

## Interpreting results

The current v0.4 mutation model intentionally performs extensive verification for safety. Adding, renaming, or removing an entry can require reading/authenticating the current vault, writing a complete pending replacement, authenticating that pending vault, replacing the active file with a recovery backup retained, and authenticating the finalized vault.

That means mutation throughput is expected to be lower than raw disk or cipher throughput. Do not market a single benchmark number as universal performance; hardware, storage, antivirus, filesystem caching, vault composition, and Argon2id cost can materially change results.

The benchmark is a development/release-validation tool, not a security proof.
