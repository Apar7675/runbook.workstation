[CmdletBinding()]
param(
    [string]$StageRoot = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$stagingBase = [System.IO.Path]::GetFullPath((Join-Path $repoRoot "artifacts\staging"))
if ([string]::IsNullOrWhiteSpace($StageRoot)) {
    $uniqueName = "workstation-{0}-{1}" -f (Get-Date).ToUniversalTime().ToString("yyyyMMddTHHmmssfffZ"), ([Guid]::NewGuid().ToString("N"))
    $StageRoot = Join-Path $stagingBase $uniqueName
}

$stageFull = [System.IO.Path]::GetFullPath($StageRoot)
$stagingPrefix = $stagingBase.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
if (-not $stageFull.StartsWith($stagingPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "StageRoot must be a new child of $stagingBase."
}
if (Test-Path -LiteralPath $stageFull) {
    throw "StageRoot already exists. Fresh production staging never cleans or reuses an existing directory."
}

$publishRoot = Join-Path $stageFull "publish"
$msbuildRoot = Join-Path $stageFull "msbuild"
$project = Join-Path $repoRoot "src\RunBook.Workstation\RunBook.Workstation.csproj"
New-Item -ItemType Directory -Path $stageFull -ErrorAction Stop | Out-Null

$arguments = @(
    "publish", $project,
    "--configuration", "Release",
    "--runtime", "win-x64",
    "--self-contained", "true",
    "--artifacts-path", $msbuildRoot,
    "--output", $publishRoot,
    "--nologo",
    "-p:PublishProfile=Production-win-x64-self-contained",
    "-p:PublishSingleFile=false",
    "-p:PublishReadyToRun=false",
    "-p:PublishTrimmed=false",
    "-p:RunBookIncludeInternalDiagnostics=false"
)

& dotnet @arguments
if ($LASTEXITCODE -ne 0) {
    throw "Workstation production publish failed with exit code $LASTEXITCODE. Staging was retained for diagnosis."
}

& (Join-Path $PSScriptRoot "Test-ProductionArtifact.ps1") `
    -ArtifactRoot $publishRoot `
    -ExpectedApplication "RunBook.Workstation" | Out-Null

Write-Host "PASS: fresh verified Workstation publish created at $publishRoot"
[pscustomobject]@{
    StageRoot = $stageFull
    PublishRoot = $publishRoot
}
