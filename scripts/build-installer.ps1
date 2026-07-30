#Requires -Version 5.1
<#
.SYNOPSIS
  Build MistMapper-Setup.exe - host, Game Bar widget, and installer UI in one package.

.DESCRIPTION
  1) Publishes the host
  2) Builds / stages the Game Bar widget (unless -SkipWidget)
  3) Zips payload and embeds it into the installer
  4) Publishes a single-file MistMapper-Setup.exe to publish\Installer\
#>
[CmdletBinding()]
param(
    [switch]$SkipHost,
    [switch]$SkipWidget,
    [switch]$SkipPublish
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$installerProj = Join-Path $root 'src\Installer\MistMapper.Installer.csproj'
$assetsDir = Join-Path $root 'src\Installer\Assets'
$payloadZip = Join-Path $assetsDir 'payload.zip'
$stage = Join-Path $root 'publish\Installer\_payload'
$outDir = Join-Path $root 'publish\Installer'

New-Item -ItemType Directory -Force -Path $assetsDir | Out-Null
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

if (-not $SkipHost) {
    Write-Host 'Publishing host…'
    dotnet publish (Join-Path $root 'src\Host\MistMapper.Host.csproj') -c Release -o (Join-Path $root 'publish\Host')
    if ($LASTEXITCODE -ne 0) { throw 'Host publish failed' }
}

if (-not $SkipWidget) {
    Write-Host 'Building Game Bar widget…'
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'scripts\build-gamebar-widget.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'Game Bar widget build failed' }
}

$hostDir = Join-Path $root 'publish\Host'
$widgetDir = Join-Path $root 'publish\GameBarWidget'
if (-not (Test-Path (Join-Path $hostDir 'MistMapper.exe'))) {
    throw "Host missing at $hostDir - run without -SkipHost"
}
if (-not (Get-ChildItem $widgetDir -Filter '*.msix' -ErrorAction SilentlyContinue)) {
    throw "Game Bar MSIX missing under $widgetDir - run without -SkipWidget"
}

Write-Host 'Staging payload…'
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path (Join-Path $stage 'Host') | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $stage 'GameBarWidget') | Out-Null

Copy-Item (Join-Path $hostDir '*') (Join-Path $stage 'Host') -Recurse -Force

# Keep the widget package lean: MSIX, CER, Dependencies (needed for AddPackage).
Copy-Item (Join-Path $widgetDir '*.msix') (Join-Path $stage 'GameBarWidget') -Force
Copy-Item (Join-Path $widgetDir '*.cer') (Join-Path $stage 'GameBarWidget') -Force
$deps = Join-Path $widgetDir 'Dependencies'
if (Test-Path $deps) {
    Copy-Item $deps (Join-Path $stage 'GameBarWidget\Dependencies') -Recurse -Force
}

if (Test-Path $payloadZip) { Remove-Item $payloadZip -Force }
Write-Host 'Compressing payload.zip…'
Compress-Archive -Path (Join-Path $stage 'Host'), (Join-Path $stage 'GameBarWidget') `
    -DestinationPath $payloadZip -CompressionLevel Optimal

$sizeMb = [math]::Round((Get-Item $payloadZip).Length / 1MB, 1)
Write-Host "payload.zip = $sizeMb MB"

if (-not $SkipPublish) {
    Write-Host 'Publishing MistMapper-Setup.exe…'
    dotnet publish $installerProj -c Release -o $outDir `
        -p:PublishSingleFile=true `
        -p:SelfContained=true `
        -p:RuntimeIdentifier=win-x64 `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true
    if ($LASTEXITCODE -ne 0) { throw 'Installer publish failed' }
}

$setup = Join-Path $outDir 'MistMapper-Setup.exe'
if (-not (Test-Path $setup)) { throw "Missing $setup" }

# Also drop a copy of payload.zip beside the exe for debugging / alternate layout.
Copy-Item $payloadZip (Join-Path $outDir 'payload.zip') -Force

Write-Host ''
Write-Host "Installer ready: $setup"
Write-Host 'Run it elevated (UAC). It installs host + widget, downloads VIIPER/usbip, and enables auto-start.'
