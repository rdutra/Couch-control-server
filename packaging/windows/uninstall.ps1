param(
    [switch] $RemoveUserData,
    [string] $InstallRoot = (Join-Path $env:LOCALAPPDATA "Programs\CouchControl")
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$startMenuFolder = Join-Path ([Environment]::GetFolderPath("StartMenu")) "Programs\CouchControl"
$runKeyPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
$startupValueName = "CouchControl.Agent"
$userDataRoot = Join-Path $env:LOCALAPPDATA "CouchControl"

if (Test-Path $runKeyPath) {
    Remove-ItemProperty -Path $runKeyPath -Name $startupValueName -ErrorAction SilentlyContinue
}

if (Test-Path $startMenuFolder) {
    Remove-Item $startMenuFolder -Recurse -Force
}

if (Test-Path $InstallRoot) {
    Remove-Item $InstallRoot -Recurse -Force
}

if ($RemoveUserData -and (Test-Path $userDataRoot)) {
    Remove-Item $userDataRoot -Recurse -Force
}

Write-Host "CouchControl application files and shortcuts were removed."
if ($RemoveUserData) {
    Write-Host "User configuration, tokens, snapshots, and logs were removed."
} else {
    Write-Host "User configuration, tokens, snapshots, and logs were preserved at: $userDataRoot"
}
