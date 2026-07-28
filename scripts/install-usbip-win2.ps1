#Requires -Version 5.1
<#
.SYNOPSIS
  Download the usbip-win2 installer (signed USBIP driver required by VIIPER on Windows).

.DESCRIPTION
  VIIPER needs usbip.exe + the VHCI driver from https://github.com/vadimgrn/usbip-win2.
  This script downloads the x64 installer. Driver install usually needs an elevated GUI/UAC.
#>
[CmdletBinding()]
param(
    [string]$Version = "v.0.9.7.8",
    [switch]$LaunchInstaller
)

$ErrorActionPreference = "Stop"
$asset = "USBip-0.9.7.8-x64.exe"
if ($Version -ne "v.0.9.7.8") {
    # Best-effort naming; override URL if needed.
    $asset = "USBip-$($Version.TrimStart('v.').TrimStart('.'))-x64.exe"
    if ($Version -eq "v.0.9.7.8") { $asset = "USBip-0.9.7.8-x64.exe" }
}
$url = "https://github.com/vadimgrn/usbip-win2/releases/download/$Version/USBip-0.9.7.8-x64.exe"
$destDir = Join-Path $env:LOCALAPPDATA "usbip-win2"
$installer = Join-Path $destDir "USBip-0.9.7.8-x64.exe"
New-Item -ItemType Directory -Force -Path $destDir | Out-Null

if (-not (Test-Path $installer)) {
    Write-Host "Downloading $url ..."
    Invoke-WebRequest -Uri $url -OutFile $installer -UseBasicParsing
}

Write-Host "Installer: $installer"
Write-Host "After install, confirm: where.exe usbip"
Write-Host "Then restart VIIPER: powershell -ExecutionPolicy Bypass -File .\scripts\install-viiper.ps1 -Start"

if ($LaunchInstaller) {
    Start-Process -FilePath $installer -Verb RunAs
}
