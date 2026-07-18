param(
    [switch] $StartAtLogin,
    [string] $InstallRoot = (Join-Path $env:LOCALAPPDATA "Programs\CouchControl")
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$packageRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$agentSource = Join-Path $packageRoot "agent"
$cliSource = Join-Path $packageRoot "cli"
$agentTarget = Join-Path $InstallRoot "agent"
$cliTarget = Join-Path $InstallRoot "cli"
$agentExe = Join-Path $agentTarget "CouchControl.Agent.exe"
$cliExe = Join-Path $cliTarget "CouchControl.Cli.exe"
$startMenuFolder = Join-Path ([Environment]::GetFolderPath("StartMenu")) "Programs\CouchCTRL"
$legacyStartMenuFolder = Join-Path ([Environment]::GetFolderPath("StartMenu")) "Programs\CouchControl"
$startupValueName = "CouchControl.Agent"

if (-not (Test-Path $agentSource) -or -not (Test-Path $cliSource)) {
    throw "Run this installer from the extracted CouchControl package root."
}

New-Item -ItemType Directory -Path $agentTarget -Force | Out-Null
New-Item -ItemType Directory -Path $cliTarget -Force | Out-Null
if (Test-Path $legacyStartMenuFolder) {
    Remove-Item $legacyStartMenuFolder -Recurse -Force
}
New-Item -ItemType Directory -Path $startMenuFolder -Force | Out-Null

Copy-Item (Join-Path $agentSource "*") $agentTarget -Recurse -Force
Copy-Item (Join-Path $cliSource "*") $cliTarget -Recurse -Force
Copy-Item (Join-Path $packageRoot "uninstall.ps1") (Join-Path $InstallRoot "uninstall.ps1") -Force
Copy-Item (Join-Path $packageRoot "PRIVACY.md") (Join-Path $InstallRoot "PRIVACY.md") -Force
Copy-Item (Join-Path $packageRoot "SUPPORT.md") (Join-Path $InstallRoot "SUPPORT.md") -Force
if (Test-Path (Join-Path $packageRoot "VERSION")) {
    Copy-Item (Join-Path $packageRoot "VERSION") (Join-Path $InstallRoot "VERSION") -Force
}

$shell = New-Object -ComObject WScript.Shell

$agentShortcut = $shell.CreateShortcut((Join-Path $startMenuFolder "CouchCTRL Agent.lnk"))
$agentShortcut.TargetPath = $agentExe
$agentShortcut.WorkingDirectory = $agentTarget
$agentShortcut.IconLocation = "$agentExe,0"
$agentShortcut.Save()

$cliShortcut = $shell.CreateShortcut((Join-Path $startMenuFolder "CouchCTRL CLI.lnk"))
$cliShortcut.TargetPath = "$env:ComSpec"
$cliShortcut.Arguments = "/k `"$cliExe`""
$cliShortcut.WorkingDirectory = $cliTarget
$cliShortcut.IconLocation = "$cliExe,0"
$cliShortcut.Save()

if ($StartAtLogin) {
    $runKeyPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
    New-Item -Path $runKeyPath -Force | Out-Null
    Set-ItemProperty -Path $runKeyPath -Name $startupValueName -Value "`"$agentExe`""
}

Write-Host "CouchCTRL Windows Companion installed for the current user."
Write-Host "Install root: $InstallRoot"
Write-Host "Start Menu folder: $startMenuFolder"
if ($StartAtLogin) {
    Write-Host "Start at login: enabled"
} else {
    Write-Host "Start at login: not changed"
}
