# Changelog

All notable changes to MistMapper are documented here.

## [0.1.6] - 2026-08-01

### Added

- Single SemVer source in `Directory.Build.props` (Host/Setup inherit; widget manifest `0.1.6.0`)
- Release checklist (`docs/release.md`) and `scripts/smoke-release.ps1`
- Host file logging under `%AppData%\MistMapper\logs\`
- Shared `HostCommandService` for remap/profile IPC
- Mapping processors (`TrackpadSurfaceProcessor`, `GyroProcessor`)
- BridgeService / WidgetPage partial splits for maintainability
- Contributor docs: CONTRIBUTING, CODE_OF_CONDUCT, SECURITY, architecture

### Changed

- CI: installer job on `windows-2022`; skipped on ordinary `main` pushes (still runs on PRs, tags, `workflow_dispatch`)
- README: known limits, source-available license clarity, profiles survive upgrades
- Setup docs: Path 1 FSE as supported; stronger troubleshooting

### Fixed

- Game Bar sideload scripts tracked under `scripts/gamebar-sideload/` (CI no longer depends on gitignored BundleArtifacts)
- Flaky multi-controller slot-override test
