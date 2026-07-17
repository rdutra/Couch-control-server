param(
    [string] $InnoSetupCompiler = "iscc.exe"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$packageRoot = Join-Path $repoRoot "artifacts/win-x64/CouchControl"
$setupScript = Join-Path $PSScriptRoot "CouchControl.iss"
$setupExe = Join-Path $repoRoot "artifacts/win-x64/CouchControlSetup-win-x64.exe"

if (-not (Test-Path (Join-Path $packageRoot "agent/CouchControl.Agent.exe")) -or
    -not (Test-Path (Join-Path $packageRoot "cli/CouchControl.Cli.exe"))) {
    throw "Build the win-x64 package first with scripts/publish-win-x64.ps1."
}

Write-Host "Building CouchControl setup wizard with Inno Setup..."
& $InnoSetupCompiler $setupScript

if (-not (Test-Path $setupExe)) {
    throw "Expected setup executable was not created: $setupExe"
}

Write-Host ""
Write-Host "Windows x64 setup wizard created:"
Write-Host "  $setupExe"
