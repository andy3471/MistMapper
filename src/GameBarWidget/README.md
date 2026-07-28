# Game Bar widget

UWP Xbox Game Bar remapper for Steam Controller Bridge.

## Build & install

From repo root (requires VS UWP workload):

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build-gamebar-widget.ps1
.\publish\GameBarWidget\Install-GameBarWidget.cmd
```

Keep `SteamControllerBridge.exe` running, then **Win+G** → pin **SC Bridge**.
