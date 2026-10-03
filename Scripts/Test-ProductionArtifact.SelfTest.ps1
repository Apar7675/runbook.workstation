[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$verifier = Join-Path $PSScriptRoot "Test-ProductionArtifact.ps1"
$fixtures = Join-Path (Split-Path $PSScriptRoot -Parent) "tests\ArtifactVerifierFixtures"
$safeFixture = Join-Path $fixtures "safe"
$unsafeFixture = Join-Path $fixtures "unsafe"

& $verifier -ArtifactRoot $safeFixture -ExpectedApplication "Synthetic" -SkipRequiredFiles -Quiet | Out-Null

$failedClosed = $false
try {
    & $verifier -ArtifactRoot $unsafeFixture -ExpectedApplication "Synthetic" -SkipRequiredFiles -Quiet | Out-Null
}
catch {
    $failedClosed = $_.Exception.Message -match 'forbidden-extension:\.dat' -and
        $_.Exception.Message -match 'session\.dat' -and
        $_.Exception.Message -match 'forbidden-extension:\.pdb' -and
        $_.Exception.Message -match 'Synthetic\.pdb'
}

if (-not $failedClosed) {
    throw "Artifact verifier self-test failed: synthetic session or symbol residue was not rejected."
}

Write-Host "PASS: artifact verifier accepts the safe fixture and rejects synthetic session and symbol residue."
