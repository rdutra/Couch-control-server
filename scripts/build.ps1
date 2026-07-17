param(
    [string] $NuGetSource = "https://api.nuget.org/v3/index.json"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$solutionPath = Join-Path $repoRoot "CouchControl.sln"
$publishScript = Join-Path $PSScriptRoot "publish-win-x64.ps1"

$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
$env:DOTNET_NOLOGO = "1"

Write-Host "Restoring dependencies..."
dotnet restore $solutionPath --source $NuGetSource

Write-Host "Building Release..."
dotnet build $solutionPath --configuration Release --no-restore

& (Join-Path $PSScriptRoot "test.ps1")

Write-Host "Publishing Windows x64 release package..."
& $publishScript -NuGetSource $NuGetSource
