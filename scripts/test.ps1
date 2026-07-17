Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$solutionPath = Join-Path $repoRoot "CouchControl.sln"

$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
$env:DOTNET_NOLOGO = "1"

Write-Host "Running non-destructive tests..."
dotnet test $solutionPath `
  --configuration Release `
  --no-restore `
  --filter "Category!=NativeIntegration"
