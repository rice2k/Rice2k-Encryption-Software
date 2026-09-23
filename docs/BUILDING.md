# Building Rice2k Encryption Software

Rice2k Encryption Software is a Windows WPF application targeting **.NET 10**.

## Requirements

On Windows, install:

- .NET 10 SDK
- A current Visual Studio installation with the **.NET desktop development** workload, or another editor plus the .NET 10 SDK
- Git (optional if downloading the repository as a ZIP)

## Clone

```powershell
git clone https://github.com/rice2k/Rice2k-Encryption-Software.git
cd Rice2k-Encryption-Software
```

## Restore and build

```powershell
dotnet restore Rice2kEncryption.sln
dotnet build Rice2kEncryption.sln --configuration Release
```

## Run security tests

The solution includes `tests/Rice2k.Tests`, an xUnit v3 test project targeting the same Windows/.NET generation as the application.

Run all tests:

```powershell
dotnet test .\tests\Rice2k.Tests\Rice2k.Tests.csproj --configuration Release
```

Current automated coverage includes:

- encrypted-text round trips;
- fresh salt/nonce behavior;
- wrong-password failure;
- text-token tamper and malformed-field rejection;
- empty, normal, and multi-chunk file round trips;
- source preservation;
- file wrong-password failure;
- ciphertext tamper detection;
- truncated-container rejection;
- destination overwrite prevention;
- service-level password validation;
- cancelled-operation temporary-file cleanup.

Security-sensitive changes should add or update tests rather than relying only on manual UI testing.

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

The development build uses:

- `Sodium.Core` for libsodium-compatible cryptographic primitives;
- `xunit.v3` for automated security/regression tests;
- `xunit.runner.visualstudio` for Visual Studio/VSTest integration;
- `Microsoft.NET.Test.Sdk` for test discovery/execution tooling.

## Important pre-1.0 warning

The project is under active development. Keep independent backups of important files and test decryption before relying on any pre-1.0 build for real data.

## CI note

A manual GitHub Actions validation workflow is included at `.github/workflows/build.yml`. It restores the solution, builds a Release configuration, executes the security test project on Windows, and uploads TRX test results when a hosted runner is available.

At initial project setup, GitHub-hosted runners were terminating before any runner was assigned (`runner_id: 0`, zero executed steps), so automatic push-triggered builds remain disabled to avoid presenting infrastructure failures as source-code failures. The workflow can be manually dispatched once GitHub provides a hosted runner for the repository.
