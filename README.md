# Steam Controller Bridge

Turns the **2026 Steam Controller** into a virtual **Xbox 360** pad (plus optional keyboard/mouse) when Steam is closed, with per-game profiles and remapping from a tray remapper or **Xbox Game Bar** widget.

## What it does

| Piece | Role |
|-------|------|
| **Host** (`SteamControllerBridge.exe`) | Tray app: driver → map → VIIPER / keyboard / mouse (status only — remap in Game Bar) |
| **Driver model** | Steam Controller is the first `IControllerDriver` (more can plug in later) |
| **Desktop Widget** | Lightweight companion; remapping is in Game Bar |
| **Game Bar Widget** | UWP Win+G visual controller map + remapper (`src/GameBarWidget`) |
| **FseHome** | Optional Path 3 thin launcher for Xbox FSE |

Steam Input is **not** used while Steam is closed. Remapping is app-owned.

## Prerequisites

1. **Windows 10/11** x64  
2. **[.NET 8 runtime](https://dotnet.microsoft.com/download/dotnet/8.0)** (or use self-contained publish)  
3. **[usbip-win2](https://github.com/vadimgrn/usbip-win2)** (signed USBIP driver)  
4. **VIIPER** — install/start with the helper (GPL-3.0, downloaded separately, not vendored in this repo):

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\install-viiper.ps1 -Start -AddToUserPath
```

   Or run `viiper server` yourself (API `:3242`). The host will also try to auto-start `%LocalAppData%\VIIPER\viiper.exe` if the API is down.  
5. Steam Controller 2026 (VID `0x28DE`, PID `0x1302` / `0x1303` / `0x1304`)  
6. **Steam closed** while bridging (host auto-pauses when `steam.exe` is running)

If VIIPER is not running, the host shows an **error** (tray balloon, remapper banner, Game Bar banner) instead of silently waiting.

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

## Profiles

- Stored in `%AppData%\SteamControllerBridge\profiles.json`
- Map any remappable input to **Xbox**, **keyboard** (optional modifiers), or **mouse button**
- **Official layouts** (Steam Input–style): Gamepad, Desktop, Mouse Joystick, Keyboard & Mouse, Racing — create from the Game Bar widget
- **New / Duplicate / Rename / Delete** profiles in the Game Bar widget
- **Per-game bindings** match the foreground process exe; use **Bind to game** in the widget
- Trackpad + gyro modes remain profile fields (Steam Controller driver)

## FSE / Xbox mode startup

See [docs/setup.md](docs/setup.md) for Path 1 (Start at log in), Path 2 (AnyFSE), Path 3 (FseHome), and logon/lock lizard mode.

## Future rename

Product branding may move away from “Steam Controller Bridge” when more drivers land. Checklist: assembly names, Appx identity/cert, AppData folder migration, pipe name, docs. Architecture already uses neutral driver ids (`steam-controller`).

## License

MIT — protocol details informed by community SC2026 HID research (e.g. SteamlessController).
