# Contributing to MistMapper

Thanks for helping. This project is source-available under [PolyForm Noncommercial 1.0.0](LICENSE.md) (not OSI open source). By contributing you agree your contributions are licensed under the same terms.

## Prerequisites

- Windows 10/11 x64
- .NET 8 SDK
- For the Game Bar widget: Visual Studio 2022 with the **Universal Windows Platform** workload + Windows SDK 10.0.19041+
- VIIPER + usbip-win2 to run the bridge end-to-end (see [docs/setup.md](docs/setup.md))

## Build and test

```powershell
dotnet restore MistMapper.sln
dotnet build MistMapper.sln -c Release
dotnet test MistMapper.sln -c Release
```

Host only:

```powershell
dotnet publish src\Host\MistMapper.Host.csproj -c Release -o publish\Host
.\publish\Host\MistMapper.exe --tray
```

Game Bar widget:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build-gamebar-widget.ps1
.\publish\GameBarWidget\Install-GameBarWidget.cmd
```

Full setup package + smoke check:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build-installer.ps1
powershell -ExecutionPolicy Bypass -File .\scripts\smoke-release.ps1 -RequireSetup
```

## Coding norms

- Prefer injectable seams (`IControllerDriver`, keyboard/mouse sinks, watchers) so behavior stays unit-testable.
- Do not dump large new features into a single mega-file. Prefer partials / small helpers next to existing splits (`BridgeService.*`, `WidgetPage.*`, mapping processors).
- Keep the input mapping loop cheap (no per-frame logging).
- Match existing naming and style in the area you touch.
- Add or update tests under `tests/MistMapper.Tests` for Host/Shared behavior.
- Do not commit `*.pfx`, certificates, `AppPackages/`, or `publish/` binaries.

## Versioning

Product version lives in [`Directory.Build.props`](Directory.Build.props). Keep [`src/GameBarWidget/Package.appxmanifest`](src/GameBarWidget/Package.appxmanifest) on the matching four-part version (`Major.Minor.Patch.0`).

## Pull requests

- One concern per PR when possible.
- Describe what changed and how you tested it.
- Link issues if relevant.
- CI must pass (`build-and-test`; installer job runs on PRs/tags).

## Architecture

See [docs/architecture.md](docs/architecture.md).
