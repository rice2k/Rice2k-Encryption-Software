# Building Rice2k Encryption Software

Rice2k Encryption Software is a Windows WPF application targeting **.NET 10**.

## Requirements

On Windows, install:

- the .NET 10 SDK selected by `global.json`;
- a current Visual Studio installation with the **.NET desktop development** workload, or another editor plus the .NET 10 SDK;
- Git (optional if downloading the repository as a ZIP).

The repository currently pins SDK feature band **10.0.401** with `latestPatch` roll-forward so validation is reproducible while still allowing later servicing patches in that feature band.

## Clone

```powershell
git clone https://github.com/rice2k/Rice2k-Encryption-Software.git
cd Rice2k-Encryption-Software
```

## Recommended stabilization validation

For Beta/readiness work, run the repository validator from PowerShell:

```powershell
PowerShell -ExecutionPolicy Bypass -File .\tools\Validate-Rice2k.ps1
```

It records:

- `dotnet --info`;
- complete solution restore output;
- Release build output;
- security/regression test output and TRX results;
- exact exit codes;
- application version and Git commit when available;
- a Markdown validation summary.

Results are written to:

```text
artifacts\validation\<timestamp>\
```

`artifacts/` is ignored by Git so local validation logs are not accidentally committed. A failed build/test attempt remains visible in the generated report rather than being rewritten as a pass.

## Restore and build manually

```powershell
dotnet --info
dotnet restore Rice2kEncryption.sln
dotnet build Rice2kEncryption.sln --configuration Release --no-restore -p:ContinuousIntegrationBuild=true
```

## Run security tests manually

The solution includes `tests/Rice2k.Tests`, an xUnit v3 test project targeting the same Windows/.NET generation as the application.

```powershell
dotnet test .\tests\Rice2k.Tests\Rice2k.Tests.csproj --configuration Release --no-build
```

Current source-controlled coverage includes file/text/key/recovery/vault/identity/recipient/signature/privacy and App Lock regression scenarios. Security-sensitive changes should add or update tests rather than relying only on manual UI testing.

## Run from source

```powershell
dotnet run --project .\src\Rice2k.App\Rice2k.App.csproj
```

## Visual Studio

1. Open `Rice2kEncryption.sln`.
2. Allow NuGet packages to restore.
3. Build the solution.
4. Use **Test Explorer** to run `Rice2k.Tests`.
5. Set `Rice2k.App` as the startup project.
6. Run the application with **F5** or **Ctrl+F5**.

## Current external packages

The development build uses, among other dependencies:

- `Sodium.Core` for libsodium-compatible cryptographic primitives;
- `Microsoft.Win32.SystemEvents` for Windows session-lock integration;
- `xunit.v3` for automated security/regression tests;
- `xunit.runner.visualstudio` for Visual Studio/VSTest integration;
- `Microsoft.NET.Test.Sdk` for test discovery/execution tooling.

## Important pre-1.0 warning

The project is under active development. Keep independent backups of important files and test decryption before relying on any pre-1.0 build for real data.

## CI status

`.github/workflows/build.yml` now validates pushes and pull requests to `main` and also supports manual dispatch. It requests `windows-latest`, installs the SDK from `global.json`, restores the complete solution, builds Release, runs the security/regression project, and uploads TRX results.

As of the current stabilization attempt, GitHub creates the validation check but does not assign a hosted runner: observed jobs report `runner_id: 0`, an empty runner name/group, zero executed steps, and no job log. This is tracked as `R2K-CI-001` in `docs/KNOWN-ISSUES.md` and is **not** being treated as a compiler/test failure.

See also:

- [`RELEASE-READINESS.md`](RELEASE-READINESS.md)
- [`KNOWN-ISSUES.md`](KNOWN-ISSUES.md)
- [`RELEASE-HISTORY.md`](RELEASE-HISTORY.md)
- [`RELEASE-PROCESS.md`](RELEASE-PROCESS.md)
