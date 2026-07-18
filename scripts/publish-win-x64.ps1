param(
    [switch] $ReadyToRun,
    [string] $NuGetSource = "https://api.nuget.org/v3/index.json"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$agentProjectPath = Join-Path $repoRoot "src/CouchControl.Agent/CouchControl.Agent.csproj"
$cliProjectPath = Join-Path $repoRoot "src/CouchControl.Cli/CouchControl.Cli.csproj"
$outputRoot = Join-Path $repoRoot "artifacts/win-x64"
$packageRoot = Join-Path $outputRoot "CouchControl"
$agentOutput = Join-Path $packageRoot "agent"
$cliOutput = Join-Path $packageRoot "cli"
$packageZip = Join-Path $outputRoot "CouchControl-win-x64.zip"
$installerSource = Join-Path $repoRoot "packaging/windows/install.ps1"
$uninstallerSource = Join-Path $repoRoot "packaging/windows/uninstall.ps1"
$installReadmeSource = Join-Path $repoRoot "packaging/windows/README-INSTALL.md"
$privacySource = Join-Path $repoRoot "docs/PRIVACY.md"
$supportSource = Join-Path $repoRoot "docs/SUPPORT.md"

$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
$env:DOTNET_NOLOGO = "1"

if (Test-Path $packageRoot) {
    Remove-Item $packageRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $agentOutput -Force | Out-Null
New-Item -ItemType Directory -Path $cliOutput -Force | Out-Null

$publishProperties = @(
    "-p:PublishSingleFile=false",
    "-p:PublishTrimmed=false",
    "-p:SelfContained=true"
)

if ($ReadyToRun) {
    $publishProperties += "-p:PublishReadyToRun=true"
}

Write-Host "Publishing CouchControl tray agent for win-x64..."
dotnet publish $agentProjectPath `
  --configuration Release `
  --runtime win-x64 `
  --self-contained true `
  --source $NuGetSource `
  @publishProperties `
  --output $agentOutput

Write-Host "Publishing CouchControl CLI for win-x64..."
dotnet publish $cliProjectPath `
  --configuration Release `
  --runtime win-x64 `
  --self-contained true `
  --source $NuGetSource `
  @publishProperties `
  --output $cliOutput

Get-ChildItem -Path $packageRoot -Filter "createdump.exe" -Recurse | Remove-Item -Force

Copy-Item $installerSource (Join-Path $packageRoot "install.ps1")
Copy-Item $uninstallerSource (Join-Path $packageRoot "uninstall.ps1")
Copy-Item $installReadmeSource (Join-Path $packageRoot "README-INSTALL.md")
Copy-Item $privacySource (Join-Path $packageRoot "PRIVACY.md")
Copy-Item $supportSource (Join-Path $packageRoot "SUPPORT.md")

$version = (Get-Item (Join-Path $agentOutput "CouchControl.Agent.exe")).VersionInfo.ProductVersion
Set-Content -Path (Join-Path $packageRoot "VERSION") -Value $version -Encoding UTF8

if (Test-Path $packageZip) {
    Remove-Item $packageZip -Force
}

Compress-Archive -Path (Join-Path $packageRoot "*") -DestinationPath $packageZip -Force

Write-Host ""
Write-Host "Windows x64 package created:"
Write-Host "  $packageRoot"
Write-Host "  $packageZip"
