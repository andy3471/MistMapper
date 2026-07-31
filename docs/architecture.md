# Architecture

```
Physical pad (SC / DualSense)
        |
   IControllerDriver
        |
     InputFrame
        |
   MappingEngine  (+ TrackpadSurfaceProcessor / GyroProcessor)
        |
   VIIPER Xbox 360  +  optional keyboard / mouse sinks
        ^
   BridgeService (multi-pad loop, Steam pause, session lock, profiles)
        ^
   HostCommandService  <---  named pipe IPC  /  Game Bar file IPC
        ^
   Game Bar widget (Win+G) / desktop companion
```

## Projects

| Project | Role |
|---------|------|
| `src/Shared` | Profiles, bindings, layouts, IPC DTOs, enums |
| `src/Host` | Tray host, drivers, mapping, VIIPER, bridge, IPC |
| `src/GameBarWidget` | UWP Xbox Game Bar remapper UI |
| `src/Installer` | `MistMapper-Setup.exe` |
| `tests/MistMapper.Tests` | Unit/integration tests with fakes |

## Profiles

Persisted in `%AppData%\MistMapper\profiles.json`.

- Active / per-game bindings via foreground exe
- Per-controller `ControllerSlot.ProfileId` override (null = shared/game resolve)
- Bridge mirrors that as `BridgeSlot.ProfileId`

## Extending controllers

Implement `IControllerDriver`, register via `DriverRegistry`, and expose
capabilities/layout metadata the widget already understands.
