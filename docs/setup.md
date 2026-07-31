# Setup guide — MistMapper

## 1. usbip-win2

1. Download the latest release from https://github.com/vadimgrn/usbip-win2  
   (e.g. `USBip-0.9.7.8-x64.exe`) and install it.  
2. Confirm `C:\Program Files\USBip\usbip.exe` exists.  
3. Ensure that folder is on your **PATH** (the installer usually does this; if VIIPER says `usbip: executable file not found`, add it manually and **restart VIIPER**).

```powershell
[Environment]::SetEnvironmentVariable(
  "Path",
  $env:Path + ";C:\Program Files\USBip",
  "User")
```

Open a **new** terminal afterward, or restart `viiper server`.

## 2. VIIPER (required)

VIIPER is **GPL-3.0** ([Alia5/VIIPER](https://github.com/Alia5/VIIPER)). This repo does **not** ship the binary; use the helper to download the official Windows build into `%LocalAppData%\VIIPER`:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\install-viiper.ps1 -Start -AddToUserPath
```

Or start manually:

```powershell
& "$env:LOCALAPPDATA\VIIPER\viiper.exe" server
# or, if on PATH:
viiper server
```

Default ports: USBIP `3241`, API `3242`.

The host probes `:3242` and, if down, attempts to launch the local install automatically. You still need **usbip-win2** for virtual pads to appear to games.

If setup fails, the host surfaces a red banner / tray error. Typical cases:

| Message | Fix |
|---------|-----|
| VIIPER unavailable / connection refused | `install-viiper.ps1 -Start` |
| `usbip: executable file not found` | Install usbip-win2, put `usbip.exe` on PATH, restart VIIPER |

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\install-usbip-win2.ps1 -LaunchInstaller
powershell -ExecutionPolicy Bypass -File .\scripts\install-viiper.ps1 -Start
```

Keep VIIPER running while you play (the install script starts it minimized).

## 3. Host app

```powershell
dotnet publish src\Host\MistMapper.Host.csproj -c Release -r win-x64 --self-contained false -o publish\Host
.\publish\Host\MistMapper.exe --tray
```

Tray menu:

- **Status…** — bridge status / VIIPER errors (no remapping UI)  
- **Remap in Game Bar (Win+G)** — opens help for the widget remapper  
- **Toggle bridge** / **Start with Windows**

All remapping (Xbox / keyboard / mouse, trackpads, gyro, per-game bind) is done in the **Game Bar widget**.

Profiles live in `%AppData%\MistMapper\profiles.json` and are **not** wiped by reinstalling or upgrading MistMapper (the Setup app replaces Host/widget binaries only).

### Drivers

The host uses a pluggable **driver** model. Current drivers: Steam Controller (SC1/SC2) and DualSense / DualSense Edge. The bridge can open multiple pads, map each `InputFrame` through the active (or per-slot) profile, and send output to VIIPER / keyboard / mouse sinks.

### Per-game profiles

1. Select a profile in the remapper or Game Bar.  
2. Focus the game window.  
3. Click **Bind profile to current game**.  

The host matches the foreground process **exe name** (optional path contains). Manual profile picks stay sticky until the foreground process changes.

### Keyboard mapping notes

Keyboard injection uses `SendInput`. Some elevated games may not receive injected keys unless the host runs elevated too. Virtual Xbox output via VIIPER does not have that limitation.

## 4. Xbox mode / FSE startup (Path 1 — supported default)

**Path 1 is the supported way** to run MistMapper inside Xbox Full Screen Experience. Prefer it over Path 2/3 unless Path 1 fails on your device.

The host must start **inside** Xbox Full Screen Experience without opening Game Bar.

1. Enable **Start with Windows** in the tray (or run `scripts\install-startup.ps1`).  
2. Open **Settings → Apps → Startup → MistMapper**.  
3. Set startup to **Start at log in** (not “Start when exiting to desktop”).  
4. Enter Xbox mode / FSE. The virtual pad should appear once VIIPER + controller are ready.

Game Bar / the Widget exe is **optional** and only needed to remap.

A helper script is also written to:

`%AppData%\MistMapper\enable-fse-startup.ps1`

### Path 2 — AnyFSE (advanced)

If you use [AnyFSE](https://github.com/ashpynov/AnyFSE) as the FSE home app:

1. Set AnyFSE as home under Settings → Gaming → Full screen experience / Xbox mode.  
2. Configure your preferred launcher (Xbox, Playnite, …).  
3. Set **custom startup application** to `MistMapper.exe` (with `--tray`).

### Path 3 — FseHome wrapper (advanced / optional)

`MistMapper.FseHome.exe` starts the host, then optionally a handoff launcher:

```powershell
.\MistMapper.FseHome.exe --launch "C:\Path\To\Playnite.FullscreenApp.exe"
```

To appear as a selectable FSE home app you must package it as a sideloaded MSIX with the community `gamingHome` capability (same approach as AnyFSE). This is unofficial and can break on Windows updates — prefer Path 1/2 unless Path 1 fails on your device.

## 5. Remapper / Game Bar widget

### Desktop companion (always available)

```powershell
.\publish\Widget\MistMapper.Widget.exe
```

Or use Host tray → **Open remapper**. Talks over named pipe `MistMapper.Ipc`.

### Game Bar widget (Win+G) — visual controller map

Project: [`src/GameBarWidget`](../src/GameBarWidget)  
Installer scripts: `scripts\build-gamebar-widget.ps1` + staged `Install-GameBarWidget.cmd`

**Build requirements**

- Visual Studio 2022 **Build Tools** or IDE with **Universal Windows Platform** workload
- Windows SDK **10.0.19041** or newer
- Host already built (`publish\Host`)

**Build + stage**

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build-gamebar-widget.ps1
```

This creates a self-signed cert (`CN=MistMapper`), packs an MSIX/Appx, and stages:

`publish\GameBarWidget\`

**Install (elevated)**

```powershell
cd .\publish\GameBarWidget
.\Install-GameBarWidget.cmd
```

**Use it**

1. Start `MistMapper.exe` (tray) — required for remaps  
2. Press **Win+G** → Widgets menu → pin **MistMapper**  
3. Use the visual map (tap controls), status chips, profile/game bind, trackpad/gyro modes  

IPC: the widget writes requests into its package `LocalState`; the host watches `%LocalAppData%\Packages\MistMapper.GameBar_*\LocalState\`.

**Trust note:** this uses a local self-signed cert (not Store-signed). Windows will treat it as sideloaded developer content.

## 6. Logon / lock screen (lizard mode)

| Situation | Behavior |
|-----------|----------|
| Before first unlock / cold login | Firmware **lizard mode** (keyboard/mouse HID) — no host yet |
| Host running, session unlocked | Lizard **off**, VIIPER Xbox pad active |
| Win+L / sleep / lock | Host **restores lizard mode**, pauses VIIPER |
| Unlock | Bridge resumes |
| Steam running | Bridge pauses and restores lizard mode |

Native Windows **gamepad PIN** login expects a real Xbox-class pad present at LogonUI. Our virtual pad is created after sign-in, so it cannot drive cold-boot gamepad PIN. For console-style boots, use Windows auto sign-in + FSE Path 1.

## 7. Troubleshooting / Fix this

| Symptom | Check |
|---------|--------|
| `VIIPER unavailable` / red banner | Run Setup again with VIIPER checked, or `scripts\install-viiper.ps1 -Start`. Is usbip-win2 installed? Port 3242 free? |
| Waiting for controller | USB/dongle connected? Steam fully exited? |
| Double input | Close Steam, or leave **Pause when Steam is open** enabled in Game Bar Settings |
| No pad in FSE | Startup set to **Start at log in** (Path 1 — supported). AnyFSE/FseHome are advanced fallbacks |
| Lock screen no mouse | Ensure host restored lizard (status `PausedLocked`) |
| Keyboard binds ignored in game | Prefer Xbox binds via VIIPER; elevating the host may help SendInput |
| Per-game profile not switching | Bind while the game is focused; ignored shells (Game Bar, etc.) |
| Profiles missing after update | They live in `%AppData%\MistMapper\profiles.json` (not under Program Files) |
| Widget missing after update | Re-run `MistMapper-Setup.exe` or `Install-GameBarWidget.cmd` elevated |

Host logs (when enabled): `%AppData%\MistMapper\logs\`

## Architecture

```
SteamControllerDriver ──► InputFrame ──► MappingEngine ──► VIIPER / keyboard / mouse
                              ▲
                     active profile (+ per-game rules)
                              ▲
                     named pipe / file IPC
                              │
              Remapper / Game Bar visual map
```

Future controllers add another `IControllerDriver`; mapping and UI consume driver capabilities + layout metadata.
