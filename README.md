# MistMapper

Use a **Steam Controller on Windows** without Steam Input — with a **Steam Input–like remapper** that lives in an **Xbox Game Bar** widget (Win+G).

MistMapper reads the physical Steam Controller, lets you map buttons / pads / sticks / gyro the way you’d expect from Steam’s controller UI, and exposes the result as a virtual **Xbox 360** pad (plus optional keyboard and mouse). That means games and Windows see a normal XInput controller even when Steam is closed.

## Status / disclaimer

This project is largely a **vibe-coded proof of concept**, built with **heavy AI assistance**. It works well enough to explore the idea, but the codebase is uneven: naming, structure, and polish still need a real pass.

Expect rough edges, incomplete features, and opportunistic design choices. **It will be cleaned up in the future** — refactors, tests, and clearer architecture — once the product shape settles. Treat it as experimental software, not a finished product.

## What it does

| Piece | Role |
|-------|------|
| **Host** (`MistMapper.exe`) | Tray app: Steam Controller → mapping → VIIPER virtual Xbox pad / keyboard / mouse |
| **Game Bar widget** | Steam Input–style view & edit UI (controller layout, remaps, sensitivity, profiles) |
| **Driver model** | Steam Controller is the first `IControllerDriver` (more can plug in later) |
| **FseHome** | Optional thin launcher for Xbox Full Screen Experience |

Steam’s own Steam Input stack is **not** used while Steam is closed. Remapping is owned by MistMapper and configured from the Game Bar widget.

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
dotnet build MistMapper.sln -c Release
```

## Install Game Bar widget (Win+G)

```powershell
# Needs VS Build Tools / IDE with UWP workload + Windows SDK 10.0.19041+
powershell -ExecutionPolicy Bypass -File .\scripts\build-gamebar-widget.ps1
cd .\publish\GameBarWidget
.\Install-GameBarWidget.cmd   # elevated
```

Then start the host, press **Win+G**, and pin **MistMapper**.

Full details: [docs/setup.md](docs/setup.md).

## Quick start

```powershell
viiper server
.\src\Host\bin\Release\net8.0-windows\MistMapper.exe --ui
```

## Profiles

- Stored in `%AppData%\MistMapper\profiles.json`
- Map any remappable input to **Xbox**, **keyboard** (optional modifiers), or **mouse button**
- **Official layouts** (Steam Input–style): Gamepad, Desktop, Mouse Joystick, Keyboard & Mouse, Racing — create from the Game Bar widget
- **New / Duplicate / Rename / Delete** profiles in the Game Bar widget
- **Per-game bindings** match the foreground process exe; use **Bind to game** in the widget
- Trackpad + gyro modes remain profile fields (Steam Controller driver)

## FSE / Xbox mode startup

See [docs/setup.md](docs/setup.md) for Path 1 (Start at log in), Path 2 (AnyFSE), Path 3 (FseHome), and logon/lock lizard mode.

## License

MIT — protocol details informed by community SC2026 HID research (e.g. SteamlessController).
