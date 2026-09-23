# Secure Vault benchmarking

Rice2k Encryption Software includes a small console benchmark project at `tools/Rice2k.VaultBench`.

Its purpose is to produce repeatable **local** measurements for `.r2kvault` creation, add/update, full verification, and extraction without inventing performance claims in documentation.

## Requirements

- Windows
- .NET 10 SDK
- Enough free disk space for the generated plaintext files, active vault, pending vault during mutation, temporary recovery copy during replacement, and extracted correctness sample

Do not run the benchmark in a folder containing important data. The tool creates its own timestamped directory beneath the current user's temporary directory and deletes it at the end unless `--keep` is supplied.

## Default run

```powershell
dotnet run --project .\tools\Rice2k.VaultBench\Rice2k.VaultBench.csproj -c Release
```

Default workload:

- 4 files
- 64 MiB per file
- 256 MiB total plaintext

The benchmark reports:

- empty-vault creation time;
- add + pending/final verification time and plaintext throughput;
- independent full-vault verification time and throughput;
- first-file extraction time and throughput;
- final vault size and authenticated sequence number;
- SHA-256 source/restored equality for the extracted correctness sample.

## Larger runs

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
- observed peak process memory if available;
- whether correctness checks passed;
- whether `.pending` / `.backup` cleanup completed normally.

## Interpreting results

The current v0.4 mutation model intentionally performs extensive verification for safety. Adding, renaming, or removing an entry can require reading/authenticating the current vault, writing a complete pending replacement, authenticating that pending vault, replacing the active file with a recovery backup retained, and authenticating the finalized vault.

That means mutation throughput is expected to be lower than raw disk or cipher throughput. Do not market a single benchmark number as universal performance; hardware, storage, antivirus, filesystem caching, vault composition, and Argon2id cost can materially change results.

The benchmark is a development/release-validation tool, not a security proof.
