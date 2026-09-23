param(
    [string]$SourceRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($SourceRoot)) {
    $SourceRoot = Join-Path $repoRoot 'src\Rice2k.App'
}

if (-not (Test-Path $SourceRoot)) {
    Write-Error "WPF source root was not found: $SourceRoot"
    exit 2
}

$failures = [System.Collections.Generic.List[string]]::new()
$checks = 0

# Event attributes commonly used by the Rice2k WPF XAML. The preflight only
# treats identifier-shaped values as code-behind handlers.
$eventNames = @(
    'Click', 'Checked', 'Unchecked', 'SelectionChanged', 'TextChanged',
    'PasswordChanged', 'Loaded', 'Unloaded', 'Closing', 'Closed',
    'KeyDown', 'KeyUp', 'PreviewKeyDown', 'PreviewKeyUp',
    'MouseDown', 'MouseUp', 'PreviewMouseDown', 'PreviewMouseUp',
    'MouseMove', 'StateChanged', 'Drop', 'DragOver', 'DragEnter', 'DragLeave'
)

$xamlFiles = Get-ChildItem -Path $SourceRoot -Filter '*.xaml' -File -Recurse
foreach ($xamlFile in $xamlFiles) {
    $xaml = Get-Content -Raw -Path $xamlFile.FullName
    $classMatch = [regex]::Match($xaml, 'x:Class\s*=\s*"(?<class>[^"]+)"')
    if (-not $classMatch.Success) {
        continue
    }

    $fullClassName = $classMatch.Groups['class'].Value
    $className = ($fullClassName -split '\.')[-1]
    $classFiles = Get-ChildItem -Path $xamlFile.DirectoryName -Filter "$className*.cs" -File
    $classSource = ($classFiles | ForEach-Object { Get-Content -Raw -Path $_.FullName }) -join "`n"

    foreach ($eventName in $eventNames) {
        $attributePattern = '\b' + [regex]::Escape($eventName) + '\s*=\s*"(?<handler>[A-Za-z_][A-Za-z0-9_]*)"'
        foreach ($match in [regex]::Matches($xaml, $attributePattern)) {
            $handler = $match.Groups['handler'].Value
            $checks++

            $definitionPattern = '(?m)^\s*(?:private|protected|public|internal)\s+(?:async\s+)?[A-Za-z_][A-Za-z0-9_<>,\.\?\[\]]*\s+' +
                [regex]::Escape($handler) + '\s*\('
            $definitions = [regex]::Matches($classSource, $definitionPattern)

            if ($definitions.Count -eq 0) {
                $failures.Add("$($xamlFile.Name): XAML handler '$handler' for $eventName was not found in $className code-behind/partials.")
            }
            elseif ($definitions.Count -gt 1) {
                $failures.Add("$($xamlFile.Name): XAML handler '$handler' is defined $($definitions.Count) times across $className partials.")
            }
        }
    }
}

# Lifecycle overrides cannot be duplicated across partial files with the same
# signature. This specifically catches collisions such as two OnInitialized
# implementations before the WPF compiler is invoked.
$lifecycleNames = @(
    'OnInitialized', 'OnContentRendered', 'OnSourceInitialized', 'OnDrop',
    'OnExit', 'OnStartup', 'OnClosed', 'OnClosing'
)

$csFiles = Get-ChildItem -Path $SourceRoot -Filter '*.cs' -File -Recurse
$classLifecycle = @{}
foreach ($csFile in $csFiles) {
    $source = Get-Content -Raw -Path $csFile.FullName
    $classMatch = [regex]::Match($source, '\bpartial\s+class\s+(?<class>[A-Za-z_][A-Za-z0-9_]*)')
    if (-not $classMatch.Success) {
        continue
    }

    $className = $classMatch.Groups['class'].Value
    foreach ($methodName in $lifecycleNames) {
        $pattern = '\bprotected\s+override\s+void\s+' + [regex]::Escape($methodName) + '\s*\('
        if (-not [regex]::IsMatch($source, $pattern)) {
            continue
        }

        $key = "$className::$methodName"
        if (-not $classLifecycle.ContainsKey($key)) {
            $classLifecycle[$key] = [System.Collections.Generic.List[string]]::new()
        }
        $classLifecycle[$key].Add($csFile.Name)
    }
}

foreach ($entry in $classLifecycle.GetEnumerator()) {
    $checks++
    if ($entry.Value.Count -gt 1) {
        $failures.Add("Duplicate lifecycle override $($entry.Key) found in: $($entry.Value -join ', ').")
    }
}

Write-Host "Rice2k static WPF preflight: $checks check(s) across $($xamlFiles.Count) XAML file(s)."

if ($failures.Count -gt 0) {
    Write-Host "STATIC PREFLIGHT FAILED ($($failures.Count) problem(s)):" -ForegroundColor Red
    foreach ($failure in $failures) {
        Write-Host " - $failure" -ForegroundColor Red
    }
    exit 1
}

Write-Host 'STATIC PREFLIGHT PASSED: no missing/duplicate XAML handlers or duplicate lifecycle overrides detected.' -ForegroundColor Green
exit 0
