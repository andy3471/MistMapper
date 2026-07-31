# Release checklist

Use this when cutting a GitHub release (`v*` tag).

## Before tagging

1. Version is aligned in [`Directory.Build.props`](../Directory.Build.props) and [`src/GameBarWidget/Package.appxmanifest`](../src/GameBarWidget/Package.appxmanifest) (four-part `Major.Minor.Patch.0`).
2. `dotnet test MistMapper.sln -c Release` passes locally.
3. README / CHANGELOG mention user-facing changes if any.

## Cut the release

```powershell
git tag -a v0.1.6 -m "v0.1.6"
git push origin v0.1.6
```

## Verify CI

1. Open [Actions](https://github.com/andy3471/MistMapper/actions) for the tag run.
2. Confirm **build-and-test** and **build-installer** are green.
3. Confirm the **release** job uploaded `MistMapper-Setup.exe`.

## Smoke (optional local)

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build-installer.ps1
powershell -ExecutionPolicy Bypass -File .\scripts\smoke-release.ps1 -RequireSetup
```

## Clean-machine install

On a PC (or VM) that has never seen this repo:

1. Download `MistMapper-Setup.exe` from [Releases](https://github.com/andy3471/MistMapper/releases).
2. Run elevated; leave Host + Game Bar widget + VIIPER / usbip checked.
3. Confirm tray host starts, Win+G shows MistMapper, controller remaps.
4. Confirm `%AppData%\MistMapper\profiles.json` exists after first run (survives upgrades).

## After release

- Attach notes in the GitHub release if auto-notes are incomplete.
- If the install failed, check `%AppData%\MistMapper\logs` on the host (once logging is enabled).
