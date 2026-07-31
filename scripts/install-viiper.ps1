#Requires -Version 5.1
<#
.SYNOPSIS
  Download VIIPER (GPL-3.0) into %LocalAppData%\VIIPER and optionally start the server.

.DESCRIPTION
  VIIPER is a separate project (https://github.com/Alia5/VIIPER). We do not vendor its
  binaries in this repo (GPL-3.0). This script fetches the official Windows release and
  can keep "viiper server" running for MistMapper.

  Still required separately: usbip-win2 (signed USBIP driver).
#>
[CmdletBinding()]
param(
    [string]$Version = "v0.7.0",
    [switch]$Start,
    [switch]$Stop,
    [switch]$AddToUserPath
)

$ErrorActionPreference = "Stop"
$dest = Join-Path $env:LOCALAPPDATA "VIIPER"
$exe = Join-Path $dest "viiper.exe"
$asset = "viiper-windows-amd64.zip"
$url = "https://github.com/Alia5/VIIPER/releases/download/$Version/$asset"

function Test-ViiperApi {
    try {
        $c = [System.Net.Sockets.TcpClient]::new()
        $c.Connect("127.0.0.1", 3242)
        $c.Close()
        return $true
    } catch {
        return $false
    }
}

if ($Stop) {
    Get-Process -Name "viiper" -ErrorAction SilentlyContinue | Stop-Process -Force
    Write-Host "Stopped viiper processes."
    return
}

New-Item -ItemType Directory -Force -Path $dest | Out-Null

$marker = Join-Path $dest '.mistmapper-viiper-version'
$installedTag = if (Test-Path $marker) { (Get-Content $marker -Raw).Trim() } else { '' }
$needsDownload = -not (Test-Path $exe) -or ($installedTag -ne $Version)

if ($needsDownload) {
    if (Test-Path $exe) {
        Write-Host "Upgrading VIIPER $installedTag → $Version ..."
        Get-Process -Name "viiper" -ErrorAction SilentlyContinue | Stop-Process -Force
        Start-Sleep -Milliseconds 500
    }
    $zip = Join-Path $env:TEMP $asset
    Write-Host "Downloading $url ..."
    Invoke-WebRequest -Uri $url -OutFile $zip -UseBasicParsing
    Write-Host "Extracting to $dest ..."
    Expand-Archive -Path $zip -DestinationPath $dest -Force
    Remove-Item $zip -Force -ErrorAction SilentlyContinue
}

if (-not (Test-Path $exe)) {
    throw "viiper.exe not found after extract under $dest"
}

# Marker used by MistMapper-Setup to skip re-download when already on this tag.
Set-Content -Path (Join-Path $dest '.mistmapper-viiper-version') -Value $Version -NoNewline

Write-Host "VIIPER installed at: $exe ($Version)"
Write-Host "License: GPL-3.0 - see licenses.txt in that folder and https://github.com/Alia5/VIIPER"

if ($AddToUserPath) {
    $userPath = [Environment]::GetEnvironmentVariable("Path", "User")
    if ($null -eq $userPath) { $userPath = "" }
    if ($userPath -notlike ("*" + $dest + "*")) {
        $newPath = if ([string]::IsNullOrWhiteSpace($userPath)) { $dest } else { $userPath.TrimEnd([char]';') + ";" + $dest }
        [Environment]::SetEnvironmentVariable("Path", $newPath, "User")
        $env:Path = $env:Path + ";" + $dest
        Write-Host "Added $dest to user PATH (new terminals pick this up)."
    }
}

$wantStart = $Start -or -not (Test-ViiperApi)
if ($wantStart) {
    if ((Test-ViiperApi) -and -not $Start) {
        Write-Host "VIIPER API already listening on 127.0.0.1:3242"
        return
    }

    if ($Start -and (Test-ViiperApi)) {
        Get-Process -Name "viiper" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 400
    }

    Write-Host ("Starting: {0} server" -f $exe)

    # Ensure usbip-win2 is visible to VIIPER even if PATH wasn't refreshed in this session.
    $usbipDirs = @(
        "C:\Program Files\USBip",
        "C:\Program Files (x86)\USBip"
    )
    $pathPrefix = ($usbipDirs | Where-Object { Test-Path (Join-Path $_ "usbip.exe") }) -join ";"
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $exe
    $psi.Arguments = "server --api.auto-attach-local-client=false"
    $psi.WorkingDirectory = $dest
    $psi.UseShellExecute = $false
    $psi.WindowStyle = [System.Diagnostics.ProcessWindowStyle]::Minimized
    $psi.CreateNoWindow = $true
    if ($pathPrefix) {
        $psi.Environment["Path"] = $pathPrefix + ";" + $env:Path
        Write-Host "Using usbip from: $pathPrefix"
    } else {
        Write-Warning "usbip.exe not found under Program Files\USBip. Virtual pads need usbip-win2 VHCI."
    }
    Write-Host "Note: auto-attach disabled (VHCI issues). Host will feed VIIPER; attach pad via usbip if VHCI works."
    [Diagnostics.Process]::Start($psi) | Out-Null

    for ($i = 0; $i -lt 20; $i++) {
        Start-Sleep -Milliseconds 250
        if (Test-ViiperApi) {
            Write-Host "VIIPER server is up on :3242"
            Get-Process -Name "viiper" -ErrorAction SilentlyContinue | Select-Object Id, Path | Format-Table -AutoSize
            return
        }
    }
    Write-Warning "Started viiper but :3242 is not accepting connections yet. Check usbip-win2 / window output."
} else {
    Write-Host "API already up. Re-run with -Start to restart from this install, or -Stop first."
}
