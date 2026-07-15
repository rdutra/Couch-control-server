Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$rootDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectPath = Join-Path $rootDir "src/CouchControl.Cli/CouchControl.Cli.csproj"
$outputRoot = Join-Path $rootDir "artifacts/publish"

$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
$env:DOTNET_NOLOGO = "1"

Write-Host "Publishing framework-dependent Windows build..."
dotnet publish $projectPath `
  -c Release `
  -r win-x64 `
  --self-contained false `
  -o (Join-Path $outputRoot "win-x64-fdd")

Write-Host "Publishing self-contained single-file Windows build..."
dotnet publish $projectPath `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:EnableCompressionInSingleFile=true `
  -o (Join-Path $outputRoot "win-x64-sc")

Write-Host ""
Write-Host "Done."
Write-Host "Framework-dependent output: $(Join-Path $outputRoot 'win-x64-fdd')"
Write-Host "Self-contained output:     $(Join-Path $outputRoot 'win-x64-sc')"
