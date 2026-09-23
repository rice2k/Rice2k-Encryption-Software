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

## Fast WPF source preflight

Before restore/build, the repository can scan common WPF/source-wiring problems:

```powershell
PowerShell -ExecutionPolicy Bypass -File .\tools\Static-WpfPreflight.ps1
```

The preflight checks:

- WPF + WinForms implicit-using isolation so WinForms/Drawing namespaces do not silently create ambiguous WPF type names;
- XAML event-handler names that have no code-behind implementation;
- XAML event handlers accidentally implemented more than once across partial-class files;
- duplicate WPF lifecycle overrides such as `OnInitialized`, `OnContentRendered`, `OnSourceInitialized`, or `OnDrop` across the same partial class;
- duplicate ordinary methods across partial-class files using a canonicalized parameter type/modifier signature that ignores parameter names and default values.

This is an early-warning source check only. It does **not** replace the C# compiler or the security/regression suite.

## Recommended stabilization validation

For Beta/readiness work, run the repository validator from Windows PowerShell 5.1 or a newer PowerShell edition:

```powershell
PowerShell -ExecutionPolicy Bypass -File .\tools\Validate-Rice2k.ps1
```

It records:

- static WPF/source preflight output;
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

`artifacts/` is ignored by Git so local validation logs are not accidentally committed. A failed build/test attempt remains visible in the generated report rather than being rewritten as a pass. The validator deliberately avoids PowerShell-7-only path APIs so the recommended Windows PowerShell command can generate the report correctly.

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

`.github/workflows/build.yml` is currently **manual-dispatch only** while hosted-runner access is unavailable. When dispatched, it requests `windows-latest`, runs the static WPF preflight, installs the SDK from `global.json`, restores the complete solution, builds Release, runs the security/regression project, and uploads TRX results.

During stabilization, a temporary two-platform probe requested both `ubuntu-latest` and `windows-latest`. Both jobs were created but failed before any step ran and returned no logs. Earlier Windows jobs also reported `runner_id: 0`, empty runner name/group, and zero executed steps. This is tracked as `R2K-CI-001` in `docs/KNOWN-ISSUES.md` and is **not** being treated as a compiler/test failure.

Automatic push/PR validation is intentionally paused until hosted-runner access is restored so infrastructure failures do not create misleading red checks for every stabilization commit.

See also:

- [`RELEASE-READINESS.md`](RELEASE-READINESS.md)
- [`KNOWN-ISSUES.md`](KNOWN-ISSUES.md)
- [`RELEASE-HISTORY.md`](RELEASE-HISTORY.md)
- [`RELEASE-PROCESS.md`](RELEASE-PROCESS.md)
