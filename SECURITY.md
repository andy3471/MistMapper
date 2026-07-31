# Security Policy

## Supported versions

Security fixes are applied on a best-effort basis to the latest release on
[GitHub Releases](https://github.com/andy3471/MistMapper/releases).

## Reporting a vulnerability

Please **do not** open a public issue for security-sensitive reports.

Prefer a private GitHub security advisory on the repository, or contact the
maintainer privately via the GitHub profile linked to [andy3471/MistMapper](https://github.com/andy3471/MistMapper).

Include:

- Affected version / commit
- Impact (local privilege, input injection, path issues, etc.)
- Reproduction steps

## Notes on this project

- The Game Bar widget is distributed as a **self-signed sideload** MSIX for
  development/community installs. It is not Microsoft Store–signed.
- The host injects keyboard/mouse via `SendInput` and talks to VIIPER for a
  virtual Xbox pad. Treat untrusted profiles/layouts carefully.
- Logs may appear under `%AppData%\MistMapper\logs\` and can contain local
  path / game names — do not paste full logs into public issues if they
  include sensitive paths.
