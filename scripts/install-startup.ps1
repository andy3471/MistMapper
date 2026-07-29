# Registers MistMapper for logon startup (FSE Path 1 baseline).
# Run after publishing the Host. Then set Settings > Apps > Startup to "Start at log in".

param(
    [string]$HostExe = ""
)

$ErrorActionPreference = 'Stop'

if (-not $HostExe) {
    $candidates = @(
        Join-Path $PSScriptRoot '..\publish\Host\MistMapper.exe'
        Join-Path $PSScriptRoot '..\src\Host\bin\Release\net8.0-windows\MistMapper.exe'
    )
    $HostExe = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}

if (-not $HostExe -or -not (Test-Path $HostExe)) {
    Write-Error 'MistMapper.exe not found. Pass -HostExe or publish first.'
}

$full = (Resolve-Path $HostExe).Path
$run = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
New-Item -Path $run -Force | Out-Null
Set-ItemProperty -Path $run -Name 'MistMapper' -Value ('"{0}" --tray' -f $full)

$appData = Join-Path $env:APPDATA 'MistMapper'
New-Item -ItemType Directory -Force -Path $appData | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'enable-fse-startup.ps1') (Join-Path $appData 'enable-fse-startup.ps1') -Force -ErrorAction SilentlyContinue

Write-Host "Registered: $full"
Write-Host ""
Write-Host "Next: Settings > Apps > Startup > MistMapper > Start at log in"
Write-Host "Also ensure 'viiper server' is running before / at login."
