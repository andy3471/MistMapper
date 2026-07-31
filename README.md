# MistMapper

**Steam Input for Windows — without Steam.**

Use **any supported controller** — such as a Steam Controller or DualSense — on Windows without Steam Input, with a Steam Input–like remapper in an **Xbox Game Bar** widget (Win+G). That includes surfaces Steam games take for granted but Xbox mode usually can’t: the Steam Controller **touchpads**, **gyro**, grips, and the rest — usable in **Xbox Game Pass** titles and **Xbox Full Screen Experience (FSE)**.

MistMapper works where Steam Input can’t follow. It reads the physical pad, applies Steam-like bindings (buttons, pads, gyro, multi-commands, long press), and presents the result as a virtual **Xbox 360** pad — plus optional keyboard and mouse — so Game Pass, FSE, and any XInput-only game see a normal controller.

Steam stays closed. Games and Windows see a real XInput controller.

---

## Why this exists

Steam Input is excellent — until you leave Steam:

| Situation | Steam Input | MistMapper |
|-----------|-------------|------------|
| Steam library games | ✅ | ✅ (or leave Steam running and pause MistMapper) |
| **Xbox Game Pass / Microsoft Store** | ❌ | ✅ virtual Xbox pad |
| **Xbox mode / Full Screen Experience** | ❌ | ✅ starts with Windows / at logon |
| Desktop / launcher without Steam | ❌ | ✅ |
| Remap UI while playing | Steam overlay | **Win+G Game Bar** widget |

If you want any supported controller on Game Pass titles, in Xbox mode, or anywhere that expects a gamepad — MistMapper is the bridge.

---

## Features

### Steam Input–style remapping

- **Buttons, grips, triggers, D-pad, sticks** → Xbox, keyboard, or mouse
- **Multi-command bindings** — up to two outputs on the same press (Steam’s “extra command”)
- **Long press** activator (~400 ms) that replaces the regular bind while held
- **Add command / Add sub command / Add long press** flow in the Game Bar UI (command rows, then the familiar Gamepad / Keyboard / Mouse picker)
- **Steam / PS button** locked to **Xbox Guide**

### Trackpads

Full per-pad modes:

| Mode | What it does |
|------|----------------|
| **As Mouse** | Relative OS mouse, optional trackball coast |
| **As Mouse Joystick** | Pad motion → virtual stick (FPS aim on gamepad-only games) |
| **As Left / Right Stick** | Absolute pad → stick |
| **As D-pad** | Quadrants → D-pad |
| **Flick Stick** | Flick arc on lift → mouse yaw |
| **Scroll Wheel** | Vertical finger motion → real mouse wheel |
| **Button Pad** | Quadrants → face buttons |

Per-surface feel: trackball on/off, friction, vertical friction, smoothing, pad rotation, **flick sensitivity**, and **mouse haptic ticks** (Off / Low / Medium / High) while sliding as mouse or mouse joystick.

### Gyro

- Modes: **Off**, **As Right Stick**, **As Mouse**, **As Mouse Joystick**
- Activation: always on, or Hold to enable / Hold to suppress / Toggle on chosen buttons
- Combine rules (Any / All), per-axis sensitivity & invert, Steam-style **dots per 360°** calibration

### Profiles & layouts

- **Official templates:** Gamepad, Desktop, Mouse Joystick, Keyboard & Mouse, Racing
- Your layouts: New / Duplicate / Rename / Delete / Save As
- **Per-game binding** — bind the active layout to the foreground exe; switches automatically when you focus that game
- **Multi-controller:** several Steam Controllers and DualSense pads at once, each with its own virtual Xbox pad
- Shared bindings vs **per-controller override** (edit for everyone or just this pad)

### Xbox mode / Game Pass ready

- Virtual **Xbox 360** pad via [VIIPER](https://github.com/Alia5/VIIPER) + usbip-win2 — XInput-compatible for Game Pass and most PC games
- **Start with Windows** / start at logon so the bridge is up inside **Xbox Full Screen Experience**
- Optional FseHome launcher and AnyFSE integration — see [docs/setup.md](docs/setup.md)
- Auto-**pause when Steam is open** so Steam Input can take over without double input
- While Game Bar is open, button map falls back to stock Xbox for navigation; trackpad/gyro modes stay active

### Game Bar remapper (Win+G)

- **View** live layout on a controller outline (SC1 / SC2 / DualSense)
- **Edit** by category: Buttons, Grips, Triggers, D-Pad, Sticks, Trackpads, Gyro, Menu
- Layout browser (templates + yours), sensitivity & invert, advanced pad/gyro settings
- Status banners for VIIPER / controller / dependencies
- Identify pad (rumble), rumble on/off per slot, bridge toggle

### Other

- Keyboard output with **Ctrl / Alt / Shift / Win** modifiers
- Mouse buttons + scroll up/down
- Game rumble forwarded to the physical controller
- Desktop tray host + optional desktop remapper companion
- Pluggable **driver** model (`IControllerDriver`)

---

## Supported controllers

| Controller | Notes |
|------------|--------|
| **Steam Controller 2026 (SC2)** | Full surface set (pads, gyro, L4–R5, stick touch, …) |
| **Steam Controller 2015 (SC1)** | Same stack; fewer grips / no RS / no stick-touch where hardware lacks them |
| **DualSense / DualSense Edge** | Touchpad + gyro; Edge paddles as L4/R4 |

More controllers can plug in via the driver interface.

---

## How it works

```
Physical pad (SC / DualSense)
        ↓
   MistMapper Host (tray)
        ↓
   MappingEngine  ←  active profile (game bind / per-slot override)
        ↓
   VIIPER virtual Xbox 360  +  optional keyboard / mouse
        ↑
   Game Bar widget (Win+G)  — configure layouts
```

Steam’s remapper is not used while the bridge is active. Close Steam (or let MistMapper pause itself) and own the mapping end-to-end.

---

## Get MistMapper

**Recommended:** download the latest **`MistMapper-Setup.exe`** from the
[Releases](https://github.com/andy3471/gamebar-controller-remapper/releases)
page and run it. The setup app installs the host, Game Bar widget, and can fetch VIIPER / usbip-win2 for you.

Then **Win+G** → pin **MistMapper** → Edit a layout → play.

For **Xbox mode / FSE**, enable **Start with Windows** in the installer (or tray) and set MistMapper to **Start at log in** — details in **[docs/setup.md](docs/setup.md)**.

### Prerequisites (if installing pieces yourself)

Windows 10/11 x64, a supported controller, [usbip-win2](https://github.com/vadimgrn/usbip-win2), and [VIIPER](https://github.com/Alia5/VIIPER) (GPL-3.0, not vendored here). Full walkthrough: **[docs/setup.md](docs/setup.md)**.

---

## Develop from source

For local builds and contribution:

```powershell
# VIIPER (if not already installed)
powershell -ExecutionPolicy Bypass -File .\scripts\install-viiper.ps1 -Start -AddToUserPath

# Host
dotnet publish src\Host\MistMapper.Host.csproj -c Release -o publish\Host
.\publish\Host\MistMapper.exe --tray

# Game Bar widget (needs VS UWP workload + Windows SDK)
powershell -ExecutionPolicy Bypass -File .\scripts\build-gamebar-widget.ps1
.\publish\GameBarWidget\Install-GameBarWidget.cmd   # elevated

# Or build the full setup package
powershell -ExecutionPolicy Bypass -File .\scripts\build-installer.ps1
```

---

## Status

MistMapper started as a personal project: I wanted this app to exist, C# isn’t my primary language, and I didn’t have time to hand-build a full proof of concept — so I leaned heavily on AI assistance to get something real running.

It worked better than expected. The goal now is to grow it into a properly maintained, professional app — cleaner architecture, better tests, and polish — while keeping it usable along the way. Expect rough edges; contributions and patience welcome.

---

## License

**[PolyForm Noncommercial 1.0.0](LICENSE.md)** — free to use and modify for personal / noncommercial purposes; commercial use (including selling MistMapper) requires a separate license.

Protocol details informed by community Steam Controller HID research (e.g. SteamlessController).

[VIIPER](https://github.com/Alia5/VIIPER) is **GPL-3.0** and downloaded separately; it is not part of this repository.
