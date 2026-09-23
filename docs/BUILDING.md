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

## Run from source

```powershell
dotnet run --project .\src\Rice2k.App\Rice2k.App.csproj
```

## Visual Studio

1. Open `Rice2kEncryption.sln`.
2. Allow NuGet packages to restore.
3. Set `Rice2k.App` as the startup project if it is not already selected.
4. Build the solution.
5. Run with **F5** or **Ctrl+F5**.

## Current external package

The development build uses `Sodium.Core` to access libsodium-compatible cryptographic primitives.

## Important pre-1.0 warning

The project is under active development. Keep independent backups of important files and test decryption before relying on any pre-1.0 build for real data.

## CI note

A manual GitHub Actions build workflow is included at `.github/workflows/build.yml`. At initial project setup, GitHub-hosted runners were not being assigned to this repository, so automatic push-triggered builds were disabled to avoid misleading failed checks with zero executed steps. Once hosted runners are available, the workflow can be manually dispatched and later re-enabled for pushes and pull requests.
