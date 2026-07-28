# Setup guide — Steam Controller Bridge

## 1. usbip-win2

1. Download the latest release from https://github.com/vadimgrn/usbip-win2  
2. Install the signed driver package (admin).  
3. Confirm `usbip.exe` is on `PATH`.

## 2. VIIPER

1. Download https://github.com/Alia5/VIIPER/releases  
2. Start the server:

```powershell
viiper server
```

Default ports: USBIP `3241`, API `3242`. Localhost clients attach automatically when usbip-win2 is healthy.

Keep VIIPER running while you play, or install it as a Windows service if your VIIPER build supports that.

## 3. Host app

```powershell
dotnet publish src\Host\SteamControllerBridge.Host.csproj -c Release -r win-x64 --self-contained false -o publish\Host
.\publish\Host\SteamControllerBridge.exe --tray
```

Tray menu:

- **Open remapper** — full button/paddle/trackpad mapping UI  
- **Toggle bridge** — enable/disable Steamless bridging  
- **Start with Windows** — HKCU Run registration (Path 1 baseline)

Profiles live in `%AppData%\SteamControllerBridge\profiles.json`.

## 4. Xbox mode / FSE startup (Path 1 — default)

The host must start **inside** Xbox Full Screen Experience without opening Game Bar.

1. Enable **Start with Windows** in the tray (or run `scripts\install-startup.ps1`).  
2. Open **Settings → Apps → Startup → Steam Controller Bridge**.  
3. Set startup to **Start at log in** (not “Start when exiting to desktop”).  
4. Enter Xbox mode / FSE. The virtual pad should appear once VIIPER + controller are ready.

Game Bar / the Widget exe is **optional** and only needed to remap.

A helper script is also written to:

`%AppData%\SteamControllerBridge\enable-fse-startup.ps1`

### Path 2 — AnyFSE

If you use [AnyFSE](https://github.com/ashpynov/AnyFSE) as the FSE home app:

1. Set AnyFSE as home under Settings → Gaming → Full screen experience / Xbox mode.  
2. Configure your preferred launcher (Xbox, Playnite, …).  
3. Set **custom startup application** to `SteamControllerBridge.exe` (with `--tray`).

### Path 3 — FseHome wrapper (optional)

`SteamControllerBridge.FseHome.exe` starts the host, then optionally a handoff launcher:

```powershell
.\SteamControllerBridge.FseHome.exe --launch "C:\Path\To\Playnite.FullscreenApp.exe"
```

To appear as a selectable FSE home app you must package it as a sideloaded MSIX with the community `gamingHome` capability (same approach as AnyFSE). This is unofficial and can break on Windows updates — prefer Path 1/2 unless Path 1 fails on your device.

Stub package notes live beside the Widget manifest; full MSIX signing is left to your packaging pipeline.

## 5. Remapper / Game Bar widget

### Desktop companion (always available)

```powershell
.\publish\Widget\SteamControllerBridge.Widget.exe
```

Or use Host tray → **Open remapper**. Talks over named pipe `SteamControllerBridge.Ipc`.

### Game Bar widget (Win+G) — real UWP package

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

This creates a self-signed cert (`CN=SteamControllerBridge`), packs an MSIX/Appx, and stages:

`publish\GameBarWidget\`

**Install (elevated)**

```powershell
cd .\publish\GameBarWidget
.\Install-GameBarWidget.cmd
```

The installer:

1. Enables Developer Mode / sideload trust flags  
2. Imports the widget certificate into **Trusted People**  
3. Runs `Add-AppDevPackage.ps1 -Force`  
4. Restarts Game Bar processes  

**Use it**

1. Start `SteamControllerBridge.exe` (tray) — required for remaps  
2. Press **Win+G** → Widgets menu → pin **SC Bridge**  
3. Toggle bridge, switch profiles, remap L4/L5/R4/R5  

IPC: the widget writes requests into its package `LocalState`; the host watches `%LocalAppData%\Packages\SteamControllerBridge.GameBar_*\LocalState\`.

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

## 7. Troubleshooting

| Symptom | Check |
|---------|--------|
| `VIIPER unavailable` | `viiper server` running? usbip-win2 installed? port 3242 free? |
| Waiting for controller | USB/Puck connected? Steam fully exited? |
| Double input | Close Steam; only one bridge tool at a time |
| No pad in FSE | Startup set to **Start at log in**; try Path 2 |
| Lock screen no mouse | Ensure host restored lizard (status `PausedLocked`) |

## Architecture

```
Steam Controller HID ──► Host (map profiles) ──► VIIPER xbox360 ──► Games
                              ▲
                     named pipe IPC
                              │
              Widget / Remapper / (future Game Bar UWP)
```
