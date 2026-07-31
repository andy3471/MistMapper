# MistMapper

**Steam Input for Windows, without Steam.**

Use any supported controller (Steam Controller, DualSense, etc.) on Windows without Steam Input. Remapping lives in an Xbox Game Bar widget (Win+G), including stuff Xbox mode normally can't use well: Steam Controller touchpads, gyro, grips, and so on. That works in Xbox Game Pass games and Xbox Full Screen Experience (FSE).

MistMapper reads the physical pad, applies Steam-like bindings (buttons, pads, gyro, multi-commands, long press), and outputs a virtual Xbox 360 pad plus optional keyboard/mouse. Games and Windows see a normal XInput controller. Keep Steam closed while the bridge is running (or let MistMapper pause itself when Steam opens).

## Why this exists

Steam Input is great until you leave Steam:

| Situation | Steam Input | MistMapper |
|-----------|-------------|------------|
| Steam library games | Yes | Yes (or pause MistMapper and use Steam) |
| Xbox Game Pass / Microsoft Store | No | Yes (virtual Xbox pad) |
| Xbox mode / Full Screen Experience | No | Yes (starts with Windows / at logon) |
| Desktop / launcher without Steam | No | Yes |
| Remap UI while playing | Steam overlay | Win+G Game Bar widget |

If you want a Steam Controller or DualSense on Game Pass, in Xbox mode, or anywhere that expects a gamepad, this is the bridge.

## Features

### Steam Input style remapping

- Buttons, grips, triggers, D-pad, sticks to Xbox, keyboard, or mouse
- Multi-command bindings: up to two outputs on the same press (Steam's "extra command")
- Long press (~400 ms) that replaces the regular bind while held
- Add command / Add sub command / Add long press in the Game Bar UI
- Steam / PS button locked to Xbox Guide

### Trackpads

| Mode | What it does |
|------|----------------|
| As Mouse | Relative OS mouse, optional trackball coast |
| As Mouse Joystick | Pad motion to virtual stick (FPS aim on gamepad-only games) |
| As Left / Right Stick | Absolute pad to stick |
| As D-pad | Quadrants to D-pad |
| Flick Stick | Flick arc on lift to mouse yaw |
| Scroll Wheel | Vertical finger motion to real mouse wheel |
| Button Pad | Quadrants to face buttons |

Per-pad feel: trackball, friction, smoothing, rotation, flick sensitivity, and mouse haptic ticks (Off / Low / Medium / High) while sliding as mouse or mouse joystick.

### Gyro

- Off, As Right Stick, As Mouse, As Mouse Joystick
- Always on, or Hold to enable / Hold to suppress / Toggle on chosen buttons
- Combine rules (Any / All), per-axis sensitivity and invert, dots-per-360 calibration

### Profiles and layouts

- Templates: Gamepad, Desktop, Mouse Joystick, Keyboard & Mouse, Racing
- Your layouts: New / Duplicate / Rename / Delete / Save As
- Per-game binding to the foreground exe (switches when you focus that game)
- Multiple Steam Controllers and DualSense pads at once, each with its own virtual Xbox pad
- Shared bindings, or a per-controller override

### Xbox mode / Game Pass

- Virtual Xbox 360 pad via [VIIPER](https://github.com/Alia5/VIIPER) + usbip-win2
- Start with Windows / at logon so the bridge is up inside FSE
- Optional FseHome / AnyFSE hooks (see [docs/setup.md](docs/setup.md))
- Auto-pause when Steam is open (avoids double input)
- While Game Bar is open, buttons fall back to stock Xbox for navigation; trackpad/gyro stay active

### Game Bar remapper (Win+G)

- View live layout on a controller outline (SC1 / SC2 / DualSense)
- Edit by category: Buttons, Grips, Triggers, D-Pad, Sticks, Trackpads, Gyro, Menu
- Layout browser, sensitivity, advanced pad/gyro settings
- Status for VIIPER / controller / dependencies
- Identify pad (rumble), rumble on/off per slot, bridge toggle

### Other

- Keyboard with Ctrl / Alt / Shift / Win modifiers
- Mouse buttons and scroll
- Game rumble forwarded to the physical pad
- Tray host, optional desktop remapper companion
- Pluggable drivers (`IControllerDriver`)

## Supported controllers

| Controller | Notes |
|------------|--------|
| Steam Controller 2026 (SC2) | Full surface set (pads, gyro, L4/R5, stick touch, etc.) |
| Steam Controller 2015 (SC1) | Same stack; fewer grips / no RS / no stick-touch where the hardware doesn't have them |
| DualSense / DualSense Edge | Touchpad + gyro; Edge paddles as L4/R4 |

More controllers can plug in via the driver interface.

## How it works

```
Physical pad (SC / DualSense)
        |
   MistMapper Host (tray)
        |
   MappingEngine  <-  active profile (game bind / per-slot override)
        |
   VIIPER virtual Xbox 360  +  optional keyboard / mouse
        ^
   Game Bar widget (Win+G) configures layouts
```

## Get MistMapper

Download the latest `MistMapper-Setup.exe` from
[Releases](https://github.com/andy3471/MistMapper/releases)
and run it. That installs the host, Game Bar widget, and can pull in VIIPER / usbip-win2.

Then Win+G, pin MistMapper, edit a layout, play.

For Xbox mode / FSE: enable Start with Windows in the installer (or tray), and set MistMapper to Start at log in. More detail in [docs/setup.md](docs/setup.md).

### Prerequisites (manual install)

Windows 10/11 x64, a supported controller, [usbip-win2](https://github.com/vadimgrn/usbip-win2), and [VIIPER](https://github.com/Alia5/VIIPER) (GPL-3.0, not shipped in this repo). See [docs/setup.md](docs/setup.md).

## Develop from source

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

## Status

I wanted this app to exist. C# isn't my primary language, and I didn't have time to hand-build a full proof of concept, so I used a lot of AI help to get something real running.

It turned out well enough that I want to grow it into a properly maintained app: cleaner architecture, better tests, more polish. Expect rough edges for now.

## License

[PolyForm Noncommercial 1.0.0](LICENSE.md). Free to use and modify for personal / noncommercial purposes. Commercial use (including selling MistMapper) needs a separate license.

Protocol details informed by community Steam Controller HID research (e.g. SteamlessController).

[VIIPER](https://github.com/Alia5/VIIPER) is GPL-3.0 and downloaded separately; it is not part of this repository.
