param(
    [switch]$SkipTests,
    [switch]$RunBenchmarks,
    [ValidateRange(1, 32768)] [int]$FileBenchmarkMb = 2048,
    [ValidateRange(1, 16384)] [int]$VaultBenchmarkFileSizeMb = 512,
    [ValidateRange(1, 10000)] [int]$VaultBenchmarkFiles = 4
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repoRoot 'Rice2kEncryption.sln'
$testProject = Join-Path $repoRoot 'tests\Rice2k.Tests\Rice2k.Tests.csproj'
$projectFile = Join-Path $repoRoot 'src\Rice2k.App\Rice2k.App.csproj'
$fileBenchmarkProject = Join-Path $repoRoot 'tools\Rice2k.FileBench\Rice2k.FileBench.csproj'
$vaultBenchmarkProject = Join-Path $repoRoot 'tools\Rice2k.VaultBench\Rice2k.VaultBench.csproj'
$staticPreflightScript = Join-Path $PSScriptRoot 'Static-WpfPreflight.ps1'
$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$outputDirectory = Join-Path $repoRoot "artifacts\validation\$timestamp"
$testResultsDirectory = Join-Path $outputDirectory 'TestResults'
$summaryPath = Join-Path $outputDirectory 'VALIDATION-SUMMARY.md'
$vaultTotalMb = ([long]$VaultBenchmarkFileSizeMb) * ([long]$VaultBenchmarkFiles)

New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
New-Item -ItemType Directory -Force -Path $testResultsDirectory | Out-Null

function Invoke-DotNetStep {
    param(
        [Parameter(Mandatory)] [string]$Name,
        [Parameter(Mandatory)] [string[]]$Arguments,
        [Parameter(Mandatory)] [string]$LogName
    )

    $logPath = Join-Path $outputDirectory $LogName
    Write-Host "`n=== $Name ===" -ForegroundColor Cyan
    Write-Host "dotnet $($Arguments -join ' ')"

    & dotnet @Arguments 2>&1 | Tee-Object -FilePath $logPath
    $exitCode = $LASTEXITCODE

    return [pscustomobject]@{
        Name = $Name
        ExitCode = $exitCode
        Passed = ($exitCode -eq 0)
        LogPath = $logPath
    }
}

function Invoke-StaticPreflight {
    $logPath = Join-Path $outputDirectory '01-static-wpf-preflight.log'
    Write-Host "`n=== Static WPF preflight ===" -ForegroundColor Cyan

    & $staticPreflightScript 2>&1 | Tee-Object -FilePath $logPath
    $exitCode = $LASTEXITCODE

    return [pscustomobject]@{
        Name = 'Static WPF preflight'
        ExitCode = $exitCode
        Passed = ($exitCode -eq 0)
        LogPath = $logPath
    }
}

$results = [System.Collections.Generic.List[object]]::new()
$started = Get-Date
$commit = 'unknown'
$version = 'unknown'
$validationFailed = $false
$failureMessage = $null

try {
    if (Get-Command git -ErrorAction SilentlyContinue) {
        try {
            $commit = (& git -C $repoRoot rev-parse HEAD 2>$null).Trim()
        }
        catch {
            $commit = 'unknown'
        }
    }

    if (Test-Path $projectFile) {
        try {
            [xml]$projectXml = Get-Content -Raw $projectFile
            $versionNode = $projectXml.Project.PropertyGroup.Version | Select-Object -First 1
            if ($versionNode) {
                $version = [string]$versionNode
            }
        }
        catch {
            $version = 'unknown'
        }
    }

    if (-not (Test-Path $staticPreflightScript)) {
        throw "Static WPF preflight script was not found: $staticPreflightScript"
    }

    $staticPreflight = Invoke-StaticPreflight
    $results.Add($staticPreflight)
    if (-not $staticPreflight.Passed) { throw 'Static WPF preflight failed.' }

    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw 'The .NET SDK was not found. Install the supported .NET 10 SDK and run this script again.'
    }

    $sdkInfo = Invoke-DotNetStep -Name 'Record .NET SDK environment' -Arguments @('--info') -LogName '02-dotnet-info.log'
    $results.Add($sdkInfo)
    if (-not $sdkInfo.Passed) { throw '.NET SDK environment check failed.' }

    $restore = Invoke-DotNetStep -Name 'Restore complete solution' -Arguments @('restore', $solution) -LogName '03-restore.log'
    $results.Add($restore)
    if (-not $restore.Passed) { throw 'Solution restore failed.' }

    $build = Invoke-DotNetStep -Name 'Build complete solution (Release)' -Arguments @(
        'build', $solution,
        '--configuration', 'Release',
        '--no-restore',
        '-p:ContinuousIntegrationBuild=true'
    ) -LogName '04-build.log'
    $results.Add($build)
    if (-not $build.Passed) { throw 'Release build failed.' }

    if (-not $SkipTests) {
        $test = Invoke-DotNetStep -Name 'Run security/regression tests' -Arguments @(
            'test', $testProject,
            '--configuration', 'Release',
            '--no-build',
            '--results-directory', $testResultsDirectory,
            '--logger', 'trx;LogFileName=rice2k-tests.trx'
        ) -LogName '05-tests.log'
        $results.Add($test)
        if (-not $test.Passed) { throw 'Security/regression tests failed.' }
    }

    if ($RunBenchmarks) {
        if (-not (Test-Path $fileBenchmarkProject)) {
            throw "File benchmark project was not found: $fileBenchmarkProject"
        }
        if (-not (Test-Path $vaultBenchmarkProject)) {
            throw "Vault benchmark project was not found: $vaultBenchmarkProject"
        }

        $fileBenchmark = Invoke-DotNetStep -Name "Run large-file benchmark ($FileBenchmarkMb MiB)" -Arguments @(
            'run',
            '--project', $fileBenchmarkProject,
            '--configuration', 'Release',
            '--no-build',
            '--no-restore',
            '--',
            '--file-size-mb', [string]$FileBenchmarkMb
        ) -LogName '06-file-benchmark.log'
        $results.Add($fileBenchmark)
        if (-not $fileBenchmark.Passed) { throw 'Large-file benchmark failed.' }

        $vaultBenchmark = Invoke-DotNetStep -Name "Run Secure Vault benchmark ($VaultBenchmarkFiles files x $VaultBenchmarkFileSizeMb MiB = $vaultTotalMb MiB)" -Arguments @(
            'run',
            '--project', $vaultBenchmarkProject,
            '--configuration', 'Release',
            '--no-build',
            '--no-restore',
            '--',
            '--file-size-mb', [string]$VaultBenchmarkFileSizeMb,
            '--files', [string]$VaultBenchmarkFiles
        ) -LogName '07-vault-benchmark.log'
        $results.Add($vaultBenchmark)
        if (-not $vaultBenchmark.Passed) { throw 'Secure Vault benchmark failed.' }
    }
}
catch {
    $validationFailed = $true
    $failureMessage = $_.Exception.Message
    Write-Host "`nVALIDATION FAILED: $failureMessage" -ForegroundColor Red
}
finally {
    $finished = Get-Date
    $overallPassed = -not $validationFailed -and
        $results.Count -gt 0 -and
        ($results | Where-Object { -not $_.Passed }).Count -eq 0

    if (-not $SkipTests -and ($results | Where-Object Name -eq 'Run security/regression tests').Count -eq 0) {
        $overallPassed = $false
    }

    if ($RunBenchmarks) {
        if (($results | Where-Object Name -Like 'Run large-file benchmark*').Count -eq 0 -or
            ($results | Where-Object Name -Like 'Run Secure Vault benchmark*').Count -eq 0) {
            $overallPassed = $false
        }
    }

    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add('# Rice2k Local Validation Result')
    $lines.Add('')
    $lines.Add("- Version: **$version**")
    $lines.Add("- Commit: `$commit`")
    $lines.Add("- Started: $($started.ToString('o'))")
    $lines.Add("- Finished: $($finished.ToString('o'))")
    $lines.Add("- Host OS: $([System.Environment]::OSVersion.VersionString)")
    $lines.Add("- Overall result: **$(if ($overallPassed) { 'PASS' } else { 'FAIL / INCOMPLETE' })**")
    if ($failureMessage) {
        $lines.Add("- Failure reason: $failureMessage")
    }
    $lines.Add('')
    $lines.Add('| Stage | Result | Exit code | Log |')
    $lines.Add('|---|---|---:|---|')

    foreach ($result in $results) {
        # Validation step logs are written directly beneath the timestamped output
        # directory. Using only the leaf name keeps this summary compatible with
        # Windows PowerShell 5.1, whose .NET Framework lacks Path.GetRelativePath.
        $relativeLog = Split-Path -Leaf $result.LogPath
        $lines.Add("| $($result.Name) | $(if ($result.Passed) { 'PASS' } else { 'FAIL' }) | $($result.ExitCode) | `$relativeLog` |")
    }

    if ($SkipTests) {
        $lines.Add('')
        $lines.Add('> Tests were intentionally skipped. This result cannot satisfy the Beta test gate.')
    }

    if ($RunBenchmarks) {
        $lines.Add('')
        $lines.Add("> Large-data benchmarks were requested: FileBench=$FileBenchmarkMb MiB; VaultBench=$VaultBenchmarkFiles x $VaultBenchmarkFileSizeMb MiB ($vaultTotalMb MiB total plaintext). Review both benchmark logs before recording Step 5 evidence.")
    }
    else {
        $lines.Add('')
        $lines.Add('> Large-data benchmarks were not requested. This validation result cannot by itself satisfy release-readiness Step 5.')
    }

    $lines.Add('')
    $lines.Add('Generated by `tools/Validate-Rice2k.ps1`. Validation artifacts are written under `artifacts/`, which is git-ignored by default.')
    Set-Content -Path $summaryPath -Value $lines -Encoding UTF8

    Write-Host "`nValidation report: $summaryPath" -ForegroundColor Yellow

    if ($overallPassed) {
        Write-Host 'VALIDATION PASSED.' -ForegroundColor Green
        exit 0
    }

    exit 1
}
