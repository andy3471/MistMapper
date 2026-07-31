# Sideload the MistMapper Game Bar widget (run elevated).

[CmdletBinding()]
param([switch]$Force)

$ErrorActionPreference = 'Stop'

function Ensure-Elevated {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    $p = [Security.Principal.WindowsPrincipal]::new($id)
    if ($p.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { return }
    $args = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $PSCommandPath)
    if ($Force) { $args += '-Force' }
    Start-Process powershell.exe -ArgumentList $args -Verb RunAs | Out-Null
    exit 0
}

Ensure-Elevated

$root = Split-Path -Parent $PSCommandPath
$addScript = Get-ChildItem $root -Filter 'Add-AppDevPackage.ps1' -File | Select-Object -First 1
if (-not $addScript) {
    throw "Add-AppDevPackage.ps1 not found under $root. Build the widget first (scripts\build-gamebar-widget.ps1)."
}

# Add-AppDevPackage refuses to run if more than one .cer sits beside it.
Get-ChildItem $root -Filter '*.cer' -File | Sort-Object LastWriteTime -Descending | Select-Object -Skip 1 |
    ForEach-Object { Remove-Item $_.FullName -Force }
$cer = Get-ChildItem $root -Filter '*.cer' -File | Select-Object -First 1

# Enable sideloading / developer mode flags
$keyPath = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock'
if (-not (Test-Path $keyPath)) { New-Item -Path $keyPath -Force | Out-Null }
Set-ItemProperty -Path $keyPath -Name 'AllowDevelopmentWithoutDevLicense' -Value 1 -Type DWord -ErrorAction SilentlyContinue
Set-ItemProperty -Path $keyPath -Name 'AllowAllTrustedApps' -Value 1 -Type DWord -ErrorAction SilentlyContinue

if ($cer) {
    $certificate = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($cer.FullName)
    $exists = Get-ChildItem Cert:\CurrentUser\TrustedPeople -ErrorAction SilentlyContinue |
        Where-Object { $_.Thumbprint -eq $certificate.Thumbprint }
    if (-not $exists) {
        Import-Certificate -FilePath $cer.FullName -CertStoreLocation Cert:\CurrentUser\TrustedPeople | Out-Null
        Write-Host "Trusted certificate $($certificate.Thumbprint)"
    }
}

$oldNames = @('MistMapper.GameBar', 'SteamControllerBridge.GameBar')
foreach ($name in $oldNames) {
    $old = Get-AppxPackage -Name $name -ErrorAction SilentlyContinue
    if ($old) {
        Write-Host "Removing previous package $($old.PackageFullName)"
        Remove-AppxPackage -Package $old.PackageFullName
    }
}

Write-Host "Installing via $($addScript.FullName)"
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $addScript.FullName -Force
if ($LASTEXITCODE -ne 0) { throw "Add-AppDevPackage failed with $LASTEXITCODE" }

$pkg = Get-AppxPackage -Name 'MistMapper.GameBar' -ErrorAction SilentlyContinue
if (-not $pkg) { throw 'Package not registered after install.' }

Get-Process GameBar, GameBarFTServer, XboxGameBarWidgets -ErrorAction SilentlyContinue | Stop-Process -Force
Write-Host ''
Write-Host "Installed: $($pkg.PackageFullName)"
Write-Host '1) Start MistMapper.exe (tray host)'
Write-Host '2) Press Win+G → Widget menu → pin "MistMapper"'
