---
name: deploy-and-run
description: >-
  Build, deploy, and run the MistMapper app locally. Use when the
  user asks to push, deploy, install, run, rebuild, or restart the bridge, host,
  or Game Bar widget.
---

# Deploy and Run MistMapper

## Quick Reference

| Component | Build command | Output |
|-----------|--------------|--------|
| Host + Shared | `dotnet publish src\Host\MistMapper.Host.csproj -c Release -o publish\Host` | `publish\Host\MistMapper.exe` |
| Game Bar Widget | `powershell -ExecutionPolicy Bypass -File scripts\build-gamebar-widget.ps1` | `publish\GameBarWidget\` |
| Widget installer | `powershell -ExecutionPolicy Bypass -File publish\GameBarWidget\Install-GameBarWidget.ps1` | Sideloaded UWP package |

## Full Deploy Steps

### 1. Stop the running host (if any)

```powershell
Stop-Process -Name MistMapper -Force -ErrorAction SilentlyContinue
Start-Sleep 1
```

### 2. Build and publish the Host

```powershell
dotnet publish src\Host\MistMapper.Host.csproj -c Release -o publish\Host
```

### 3. Build and stage the Game Bar widget

Requires Visual Studio 2022 with the **Universal Windows Platform** workload.

```powershell
powershell -ExecutionPolicy Bypass -File scripts\build-gamebar-widget.ps1
```

This creates a self-signed certificate (if needed), builds the UWP project via MSBuild, and stages the installer to `publish\GameBarWidget\`.

### 4. Install the Game Bar widget

Runs in an elevated (admin) PowerShell window automatically:

```powershell
powershell -ExecutionPolicy Bypass -File publish\GameBarWidget\Install-GameBarWidget.ps1
```

This self-elevates, trusts the certificate, removes any previous version, and sideloads the MSIX. Wait for the admin window to finish before proceeding.

### 5. Start the host

```powershell
Start-Process "publish\Host\MistMapper.exe" -ArgumentList "--tray"
```

### 6. Open the Game Bar widget

Tell the user: press **Win+G**, then pin **MistMapper** from the widget menu.

## Verify

```powershell
Get-AppxPackage -Name 'MistMapper.GameBar' | Select-Object PackageFullName, Version
Get-Process MistMapper -ErrorAction SilentlyContinue | Select-Object Id, ProcessName
```

## Notes

- The Game Bar widget install (`Install-GameBarWidget.ps1`) self-elevates to admin. It opens a new PowerShell window -- wait for it to complete before starting the host.
- The host must be running for the Game Bar widget to show live data.
- If only backend code changed (no widget UI changes), skip steps 3-4.
- All commands run from the repo root: `C:\Users\andy3\git\steam-contoller`.
