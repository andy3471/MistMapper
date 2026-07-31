#Requires -Version 5.1
<#
.SYNOPSIS
  Smoke-check a built MistMapper release payload (Host + Game Bar MSIX + optional Setup).

.DESCRIPTION
  Verifies publish\Host\MistMapper.exe and publish\GameBarWidget\*.msix exist after
  scripts\build-installer.ps1 (or a partial build). Exits non-zero on failure.
#>
[CmdletBinding()]
param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,
    [switch]$RequireSetup
)

$ErrorActionPreference = 'Stop'
$failed = $false

function Ok([string]$msg) { Write-Host "[OK]  $msg" -ForegroundColor Green }
function Bad([string]$msg) { Write-Host "[FAIL] $msg" -ForegroundColor Red; $script:failed = $true }

$hostExe = Join-Path $RepoRoot 'publish\Host\MistMapper.exe'
if (Test-Path $hostExe) { Ok "Host: $hostExe" } else { Bad "Missing Host executable: $hostExe" }

$widgetDir = Join-Path $RepoRoot 'publish\GameBarWidget'
$msix = @(Get-ChildItem $widgetDir -Filter '*.msix' -File -ErrorAction SilentlyContinue)
if ($msix.Count -ge 1) { Ok ("Game Bar MSIX: " + ($msix | Select-Object -First 1).Name) }
else { Bad "Missing Game Bar *.msix under $widgetDir" }

$cer = @(Get-ChildItem $widgetDir -Filter '*.cer' -File -ErrorAction SilentlyContinue)
if ($cer.Count -ge 1) { Ok ("Certificate: " + ($cer | Select-Object -First 1).Name) }
else { Bad "Missing Game Bar *.cer under $widgetDir" }

$setup = Join-Path $RepoRoot 'publish\Installer\MistMapper-Setup.exe'
if (Test-Path $setup) { Ok "Setup: $setup" }
elseif ($RequireSetup) { Bad "Missing Setup executable: $setup" }
else { Write-Host "[SKIP] Setup not present (pass -RequireSetup to require it)" -ForegroundColor Yellow }

if ($failed) {
    Write-Host ''
    Write-Host 'Smoke check failed.' -ForegroundColor Red
    exit 1
}

Write-Host ''
Write-Host 'Smoke check passed.' -ForegroundColor Green
exit 0
