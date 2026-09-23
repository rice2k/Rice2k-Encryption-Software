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

function Get-CanonicalParameterSignature {
    param([string]$Parameters)

    if ([string]::IsNullOrWhiteSpace($Parameters)) {
        return ''
    }

    # Split only on top-level commas so generic/tuple/array type syntax is not
    # mistaken for a parameter boundary.
    $parts = [System.Collections.Generic.List[string]]::new()
    $builder = [System.Text.StringBuilder]::new()
    $angleDepth = 0
    $parenDepth = 0
    $bracketDepth = 0

    foreach ($ch in $Parameters.ToCharArray()) {
        $appendCharacter = $true
        switch ($ch) {
            '<' { $angleDepth++ }
            '>' { if ($angleDepth -gt 0) { $angleDepth-- } }
            '(' { $parenDepth++ }
            ')' { if ($parenDepth -gt 0) { $parenDepth-- } }
            '[' { $bracketDepth++ }
            ']' { if ($bracketDepth -gt 0) { $bracketDepth-- } }
            ',' {
                if ($angleDepth -eq 0 -and $parenDepth -eq 0 -and $bracketDepth -eq 0) {
                    $parts.Add($builder.ToString())
                    [void]$builder.Clear()
                    $appendCharacter = $false
                }
            }
        }

        if ($appendCharacter) {
            [void]$builder.Append($ch)
        }
    }

    if ($builder.Length -gt 0) {
        $parts.Add($builder.ToString())
    }

    $canonical = foreach ($part in $parts) {
        $parameter = $part.Trim()
        # Attribute text and default values do not participate in an ordinary C#
        # member signature. Parameter names do not either. Keep ref/out/in because
        # value-vs-byref is relevant to duplicate detection; params/this are not.
        $parameter = [regex]::Replace($parameter, '^\s*(?:\[[^\]]+\]\s*)+', '')
        $parameter = [regex]::Replace($parameter, '\s*=\s*.*$', '')
        $parameter = [regex]::Replace($parameter, '\s+', ' ').Trim()

        $tokens = @($parameter -split ' ' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        if ($tokens.Count -le 1) {
            $parameter
            continue
        }

        # The final token is the parameter identifier. Remove non-signature
        # declaration modifiers while preserving the remaining type declaration.
        $typeTokens = @($tokens[0..($tokens.Count - 2)] | Where-Object { $_ -notin @('params', 'this') })
        ($typeTokens -join ' ').Trim()
    }

    return ($canonical -join ',')
}

# Rice2k is a WPF application that enables WinForms only for its optional
# notification bridge. With implicit usings enabled, the Windows Forms SDK can
# inject System.Drawing and System.Windows.Forms globally; those namespaces
# contain names that can collide with WPF names (for example Application,
# Clipboard, Color, Point, and Size). Require the project to remove those two
# namespaces from implicit usings while retaining the WinForms framework.
$projectFile = Join-Path $SourceRoot 'Rice2k.App.csproj'
if (Test-Path $projectFile) {
    try {
        [xml]$projectXml = Get-Content -Raw -Path $projectFile
        $propertyGroups = @($projectXml.Project.PropertyGroup)
        $useWpf = [string](($propertyGroups.UseWPF | Where-Object { $_ } | Select-Object -First 1))
        $useWinForms = [string](($propertyGroups.UseWindowsForms | Where-Object { $_ } | Select-Object -First 1))
        $implicitUsings = [string](($propertyGroups.ImplicitUsings | Where-Object { $_ } | Select-Object -First 1))

        if ($useWpf -eq 'true' -and $useWinForms -eq 'true' -and $implicitUsings -match '^(?i:true|enable)$') {
            $removedUsings = @(
                $projectXml.Project.ItemGroup.Using |
                    Where-Object { $_.Remove } |
                    ForEach-Object { [string]$_.Remove }
            )

            foreach ($requiredRemoval in @('System.Drawing', 'System.Windows.Forms')) {
                $checks++
                if ($removedUsings -notcontains $requiredRemoval) {
                    $failures.Add("Rice2k.App.csproj enables WPF + WinForms + implicit usings but does not remove '$requiredRemoval'. This can create ambiguous WPF/WinForms type names.")
                }
            }
        }
    }
    catch {
        $failures.Add("Rice2k.App.csproj could not be inspected by the static preflight: $($_.Exception.Message)")
    }
}
else {
    $failures.Add("Rice2k.App.csproj was not found beneath the WPF source root.")
}

# Event attributes commonly used by the Rice2k WPF XAML. The preflight only
# treats identifier-shaped values as code-behind handlers.
$eventNames = @(
    'Click', 'Checked', 'Unchecked', 'SelectionChanged', 'TextChanged',
    'PasswordChanged', 'Loaded', 'Unloaded', 'Closing', 'Closed',
    'KeyDown', 'KeyUp', 'PreviewKeyDown', 'PreviewKeyUp',
    'MouseDown', 'MouseUp', 'MouseMove',
    'PreviewMouseDown', 'PreviewMouseUp', 'PreviewMouseMove',
    'PreviewMouseLeftButtonDown', 'PreviewMouseLeftButtonUp',
    'PreviewMouseRightButtonDown', 'PreviewMouseRightButtonUp',
    'StateChanged', 'Drop', 'DragOver', 'DragEnter', 'DragLeave'
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
    'OnInitialized', 'OnContentRendered', 'OnSourceInitialized', 'OnPreviewDrop',
    'OnDrop', 'OnExit', 'OnStartup', 'OnClosed', 'OnClosing'
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

# Ordinary methods can collide across partial files just as lifecycle overrides
# can. Compare method name plus a canonical parameter type/modifier signature.
# Parameter identifiers/default values are intentionally ignored because they do
# not make a distinct C# member signature. This is still an early fail-fast guard,
# not a replacement for the compiler.
$methodPattern = '(?ms)^\s*(?:public|private|protected|internal)\s+(?:(?:static|async|override|virtual|sealed|new|unsafe)\s+)*[A-Za-z_][A-Za-z0-9_<>,\.\?\[\]]*\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\((?<params>.*?)\)\s*(?:where\s+[^\{=>]+\s*)?(?:\{|=>)'
$classMethods = @{}

foreach ($csFile in $csFiles) {
    $source = Get-Content -Raw -Path $csFile.FullName
    $classMatch = [regex]::Match($source, '\bpartial\s+class\s+(?<class>[A-Za-z_][A-Za-z0-9_]*)')
    if (-not $classMatch.Success) {
        continue
    }

    $className = $classMatch.Groups['class'].Value
    foreach ($methodMatch in [regex]::Matches($source, $methodPattern)) {
        $methodName = $methodMatch.Groups['name'].Value
        if ($lifecycleNames -contains $methodName) {
            continue
        }

        $parameters = Get-CanonicalParameterSignature $methodMatch.Groups['params'].Value
        $signature = "$className::$methodName($parameters)"
        if (-not $classMethods.ContainsKey($signature)) {
            $classMethods[$signature] = [System.Collections.Generic.List[string]]::new()
        }
        $classMethods[$signature].Add($csFile.Name)
    }
}

foreach ($entry in $classMethods.GetEnumerator()) {
    $checks++
    if ($entry.Value.Count -gt 1) {
        $failures.Add("Duplicate partial-class method signature $($entry.Key) found in: $($entry.Value -join ', ').")
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

Write-Host 'STATIC PREFLIGHT PASSED: project namespace isolation, XAML handlers, lifecycle overrides, and partial-class method signatures look consistent.' -ForegroundColor Green
exit 0
