Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$rootDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$cliProjectPath = Join-Path $rootDir "src/CouchControl.Cli/CouchControl.Cli.csproj"
$agentProjectPath = Join-Path $rootDir "src/CouchControl.Agent/CouchControl.Agent.csproj"
$outputRoot = Join-Path $rootDir "artifacts/publish"

$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
$env:DOTNET_NOLOGO = "1"

Write-Host "Publishing framework-dependent Windows CLI build..."
dotnet publish $cliProjectPath `
  -c Release `
  -r win-x64 `
  --self-contained false `
  -o (Join-Path $outputRoot "cli-win-x64-fdd")

Write-Host "Publishing self-contained single-file Windows CLI build..."
dotnet publish $cliProjectPath `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:EnableCompressionInSingleFile=true `
  -o (Join-Path $outputRoot "cli-win-x64-sc")

Write-Host "Publishing framework-dependent Windows agent build..."
dotnet publish $agentProjectPath `
  -c Release `
  -r win-x64 `
  --self-contained false `
  -o (Join-Path $outputRoot "agent-win-x64-fdd")

Write-Host "Publishing self-contained single-file Windows agent build..."
dotnet publish $agentProjectPath `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:EnableCompressionInSingleFile=true `
  -o (Join-Path $outputRoot "agent-win-x64-sc")

Write-Host ""
Write-Host "Done."
Write-Host "CLI framework-dependent output:   $(Join-Path $outputRoot 'cli-win-x64-fdd')"
Write-Host "CLI self-contained output:        $(Join-Path $outputRoot 'cli-win-x64-sc')"
Write-Host "Agent framework-dependent output: $(Join-Path $outputRoot 'agent-win-x64-fdd')"
Write-Host "Agent self-contained output:      $(Join-Path $outputRoot 'agent-win-x64-sc')"
