# Steam Controller Bridge

Turns the **2026 Steam Controller** into a virtual **Xbox 360** pad when Steam is closed, with profile remapping from a tray remapper and an optional **Xbox Game Bar** widget.

## What it does

| Piece | Role |
|-------|------|
| **Host** (`SteamControllerBridge.exe`) | Tray app: HID lizard-mode control → map → VIIPER Xbox 360 |
| **Desktop Widget** | WinForms remapper over named-pipe IPC |
| **Game Bar Widget** | UWP Win+G remapper (`src/GameBarWidget`) |
| **FseHome** | Optional Path 3 thin launcher for Xbox FSE |

Steam Input is **not** used while Steam is closed. Remapping is app-owned.

## Prerequisites

1. **Windows 10/11** x64  
2. **[.NET 8 runtime](https://dotnet.microsoft.com/download/dotnet/8.0)** (or use self-contained publish)  
3. **[usbip-win2](https://github.com/vadimgrn/usbip-win2)** (signed USBIP driver)  
4. **[VIIPER](https://github.com/Alia5/VIIPER/releases)** — run `viiper server` (API `:3242`, USBIP `:3241`)  
5. Steam Controller 2026 (VID `0x28DE`, PID `0x1302` / `0x1303` / `0x1304`)  
6. **Steam closed** while bridging (host auto-pauses when `steam.exe` is running)

## Build (desktop)

```powershell
dotnet build SteamControllerBridge.sln -c Release
```

## Install Game Bar widget (Win+G)

```powershell
# Needs VS Build Tools / IDE with UWP workload + Windows SDK 10.0.19041+
powershell -ExecutionPolicy Bypass -File .\scripts\build-gamebar-widget.ps1
cd .\publish\GameBarWidget
.\Install-GameBarWidget.cmd   # elevated
```

Then start the host, press **Win+G**, and pin **SC Bridge**.

Full details: [docs/setup.md](docs/setup.md).

## Quick start

```powershell
viiper server
.\src\Host\bin\Release\net8.0-windows\SteamControllerBridge.exe --ui
```

## FSE / Xbox mode startup

See [docs/setup.md](docs/setup.md) for Path 1 (Start at log in), Path 2 (AnyFSE), Path 3 (FseHome), and logon/lock lizard mode.

## License

MIT — protocol details informed by community SC2026 HID research (e.g. SteamlessController).
