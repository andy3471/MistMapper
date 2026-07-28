# FseHome (Path 3)

Thin launcher that starts `SteamControllerBridge.exe`, then optionally a handoff home app.

```powershell
SteamControllerBridge.FseHome.exe --launch "C:\Path\To\Launcher.exe"
```

Environment variable `SCB_FSE_HANDOFF` can also supply the handoff path.

To register as an Xbox FSE home app, package as a sideloaded MSIX with the community `gamingHome` capability (see `docs/setup.md`). Prefer Path 1 startup registration unless Path 1 fails on your device.
