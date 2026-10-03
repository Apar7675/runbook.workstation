[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ArtifactRoot,
    [string]$ExpectedApplication = "RunBook.Workstation",
    [string[]]$RequiredFiles = @(),
    [switch]$SkipRequiredFiles,
    [switch]$Quiet
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Get-RelativeArtifactPath {
    param([string]$Root, [string]$Path)

    $rootFull = [System.IO.Path]::GetFullPath($Root).TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
    $pathFull = [System.IO.Path]::GetFullPath($Path)
    $rootUri = [Uri]::new($rootFull)
    $pathUri = [Uri]::new($pathFull)
    return [Uri]::UnescapeDataString($rootUri.MakeRelativeUri($pathUri).ToString()).Replace('\', '/')
}

function Get-RedactedArtifactPath {
    param([string]$RelativePath)

    $segments = @($RelativePath -split '/')
    for ($i = 0; $i -lt $segments.Count; $i++) {
        if ($i -gt 0 -and [string]::Equals($segments[$i - 1], "shops", [StringComparison]::OrdinalIgnoreCase)) {
            $segments[$i] = "[REDACTED-SCOPE]"
        }
        elseif ($i -gt 0 -and $segments[$i - 1] -in @("avatars", "employees", "users")) {
            $segments[$i] = "[REDACTED-IDENTIFIER]$([System.IO.Path]::GetExtension($segments[$i]))"
        }
        elseif ($segments[$i] -match '^(?<identifier>[A-Fa-f0-9]{8}(?:-[A-Fa-f0-9]{4}){3}-[A-Fa-f0-9]{12})(?<extension>\.[^.]*)?$') {
            $segments[$i] = "[REDACTED-IDENTIFIER]$($Matches.extension)"
        }
        elseif ($segments[$i] -match '^(?<hash>[A-Fa-f0-9]{32,})(?<extension>\.[^.]*)?$') {
            $segments[$i] = "[REDACTED-HASH]$($Matches.extension)"
        }
    }

    return $segments -join '/'
}

$resolvedRoot = Resolve-Path -LiteralPath $ArtifactRoot -ErrorAction Stop
$rootItem = Get-Item -LiteralPath $resolvedRoot.Path
if (-not $rootItem.PSIsContainer) {
    throw "Artifact root must be a directory."
}

$findings = [System.Collections.Generic.List[object]]::new()
$findingKeys = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)

function Add-Finding {
    param([string]$Rule, [string]$RelativePath, [string]$Message)

    $safePath = Get-RedactedArtifactPath $RelativePath
    $key = "$Rule|$safePath"
    if ($findingKeys.Add($key)) {
        $findings.Add([pscustomobject]@{
            Rule = $Rule
            Path = $safePath
            Message = $Message
        }) | Out-Null
    }
}

$forbiddenDirectoryNames = @(
    "Data", "App", "_machine", "_logs", "logs", "_temp", "temp", "cache", "config", "diagnostics",
    "src", ".git", ".vs", ".vscode", ".nuget", ".dotnet", ".dotnet_cli", ".dotnet-cli", ".codex", ".codex-tmp",
    "bin", "obj", "artifacts", "buildverify", "tmp_build_verify", "Avatars"
)
$forbiddenExtensions = @(
    ".log", ".dat", ".db", ".sqlite", ".sqlite3", ".db-wal", ".db-shm",
    ".pdb", ".cs", ".csproj", ".sln", ".xaml", ".ps1", ".psm1",
    ".cmd", ".bat", ".user"
)
$allowedExtensions = @(".dll", ".exe", ".json")
$textExtensions = @(".json", ".config", ".xml", ".txt", ".ini", ".yaml", ".yml", ".toml")
$secretPatterns = @(
    '(?i)-----BEGIN(?: [A-Z]+)? PRIVATE KEY-----',
    '(?i)(password|secret|api[_-]?key|access[_-]?token|refresh[_-]?token)\s*["'']?\s*[:=]\s*["''][^"'']{8,}',
    '(?<![A-Za-z0-9_-])eyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}(?![A-Za-z0-9_-])',
    '(?i)(?:[A-Z]:[\\/](?:Users|RunBook(?:\.|[\\/]))|/(?:Users|home)/)'
)

$entries = @(Get-ChildItem -LiteralPath $resolvedRoot.Path -Recurse -Force -ErrorAction Stop)
$files = @($entries | Where-Object { -not $_.PSIsContainer })
if (-not $SkipRequiredFiles -and $files.Count -eq 0) {
    Add-Finding "empty-artifact" "." "Production artifact is empty."
}

foreach ($entry in $entries) {
    $relative = Get-RelativeArtifactPath $resolvedRoot.Path $entry.FullName
    if (($entry.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        Add-Finding "reparse-point" $relative "Production artifacts must not contain links or reparse points."
    }

    if ($entry.PSIsContainer -and
        ($forbiddenDirectoryNames -contains $entry.Name -or
         $entry.Name -like "temp-*" -or
         $entry.Name -like "temp_*" -or
         $entry.Name -like "*build-output*")) {
        Add-Finding "forbidden-path" $relative "Runtime state, source, diagnostics, or nested build output is present."
    }
}

foreach ($file in $files) {
    $relative = Get-RelativeArtifactPath $resolvedRoot.Path $file.FullName
    if (($file.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        continue
    }

    $extension = $file.Extension.ToLowerInvariant()
    if ($forbiddenExtensions -contains $extension) {
        Add-Finding ("forbidden-extension:" + $extension) $relative "Forbidden runtime-state, source, or developer file type is present."
    }
    elseif ($allowedExtensions -notcontains $extension) {
        Add-Finding "unapproved-file-type" $relative "File type is not allowlisted for this production package."
    }

    if ($extension -eq ".json" -and
        $file.Name -notin @("$ExpectedApplication.deps.json", "$ExpectedApplication.runtimeconfig.json")) {
        Add-Finding "unexpected-json" $relative "Only the expected application's dependency and runtime configuration JSON files are allowed."
    }

    if ($extension -notin @(".dll", ".exe") -and
        $file.Name -match '(?i)(session|registration|timeclock|employee-auth|runtime-access-token|device-token)') {
        Add-Finding "sensitive-state-name" $relative "File name indicates authentication, device, employee, or timeclock state."
    }

    if ($textExtensions -contains $extension) {
        $text = Get-Content -LiteralPath $file.FullName -Raw -ErrorAction Stop
        foreach ($pattern in $secretPatterns) {
            if ($text -match $pattern) {
                Add-Finding "secret-or-development-reference" $relative "Text contains credential material or a machine-local development path."
                break
            }
        }
    }
}

if (-not $SkipRequiredFiles) {
    $required = @(
        "$ExpectedApplication.exe",
        "$ExpectedApplication.dll",
        "$ExpectedApplication.deps.json",
        "$ExpectedApplication.runtimeconfig.json",
        "hostfxr.dll",
        "hostpolicy.dll",
        "coreclr.dll"
    ) + $RequiredFiles

    foreach ($requiredFile in $required) {
        if (-not (Test-Path -LiteralPath (Join-Path $resolvedRoot.Path $requiredFile) -PathType Leaf)) {
            Add-Finding "missing-required-file" $requiredFile "Required self-contained application file is missing."
        }
    }

    $runtimeConfigPath = Join-Path $resolvedRoot.Path "$ExpectedApplication.runtimeconfig.json"
    if (Test-Path -LiteralPath $runtimeConfigPath -PathType Leaf) {
        try {
            $runtimeConfig = Get-Content -LiteralPath $runtimeConfigPath -Raw | ConvertFrom-Json
            $runtimeOptionsProperty = $runtimeConfig.PSObject.Properties["runtimeOptions"]
            if ($null -eq $runtimeOptionsProperty) {
                throw "runtimeOptions is missing"
            }

            $runtimeOptions = $runtimeOptionsProperty.Value
            if ($null -ne $runtimeOptions.PSObject.Properties["framework"] -or
                $null -ne $runtimeOptions.PSObject.Properties["frameworks"]) {
                Add-Finding "framework-dependent-runtimeconfig" "$ExpectedApplication.runtimeconfig.json" "Runtime config requires a machine-global shared framework."
            }
        }
        catch {
            Add-Finding "invalid-runtimeconfig" "$ExpectedApplication.runtimeconfig.json" "Runtime config is not valid JSON."
        }
    }
}

if ($findings.Count -gt 0) {
    if (-not $Quiet) {
        Write-Host "FAIL: production artifact contains $($findings.Count) forbidden or missing item(s)."
        $findings | Sort-Object Rule, Path | Format-Table Rule, Path, Message -AutoSize
    }

    $summary = ($findings | Sort-Object Rule, Path | ForEach-Object { "$($_.Rule):$($_.Path)" }) -join "; "
    throw "Production artifact verification failed: $summary"
}

if (-not $Quiet) {
    Write-Host "PASS: production artifact is self-contained and contains no denied runtime state or developer residue."
    [pscustomobject]@{
        ArtifactRoot = $resolvedRoot.Path
        FileCount = $files.Count
        Passed = $true
    }
}
