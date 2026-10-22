# Changelog

## 1.0.0 — 2026-10-21

PortDeck's first stable release.

- Simplified Chinese / English UI with live switching and persisted choice.
- TCP and UDP listener discovery, process/project/exposure metadata.
- TCP and HTTP health checks with latency and status code.
- Activity history with up/down events and favorite-port tray notifications.
- Docker published-port and best-effort WSL listener mapping.
- JSON/CSV export, details JSON, terminal shortcut, protected-process guard.
- Search syntax, sorting, grouping, keyboard navigation, and favorites.
- Opt-in update checks with checksum-verified download, exe replacement, and restart.
- Self-contained x64 and ARM64 builds, checksums, optional PFX signing.
- Scoop / winget manifest generator and packaging documentation.
- Diagnostic logs under `%LOCALAPPDATA%\PortDeck\logs`.
- 42 automated tests and CI build/test jobs.

### Known external setup

- Replace `RepositoryUrl` before using update checks.
- Configure signing secrets before calling a release "signed".
- Run the manifest generator with the real GitHub owner before submitting
  to winget or a Scoop bucket.
- WSL mapping has parser coverage but still needs a real WSL smoke test.

## 0.7.0 — 2026-10-21

- Automatic updates now download the architecture-specific release asset,
  verify its SHA256, replace the running exe through a small updater script,
  and restart PortDeck.
- Added write-permission and packaged-exe checks before installing an update.
- Added update tests for asset selection, tag parsing, and checksums.
- Version updated to 0.7.0.

## 0.6.0 — 2026-10-21

- Added best-effort WSL listener mapping for running distributions.
- Added opt-in update checks against the configured GitHub repository,
  with a release-page prompt and a Settings control.
- Added a generator for Scoop and winget manifests using real release
  checksums (x64 and ARM64).
- Added update-version and WSL parser tests; test suite is now 40 tests.
- Version updated to 0.6.0.

## 0.5.0 — 2026-10-21

- HTTP health checks now report status codes alongside TCP latency.
- Added an Activity view backed by a local JSONL history of listener
  up/down events, plus tray notifications for favorited ports.
- Added Docker published-port mapping, surfaced as a container badge in
  service details.
- Added winget / Scoop / Chocolatey packaging documentation.
- Added Docker and HTTP health tests; test suite is now 32 tests.
- Version updated to 0.5.0.

## 0.4.0 — 2026-10-21

- Added UDP listener discovery alongside TCP, with per-protocol conflict detection.
- Added on-demand TCP health checks with latency and reachability status.
- Added file logging under `%LOCALAPPDATA%\PortDeck\logs` and an in-app log folder shortcut.
- Added an ARM64 publish job to CI.
- Version updated to 0.4.0.

## 0.3.0 — 2026-10-21

- Simplified Chinese / English interface with a live language switch and
  a persisted language preference.
- Localized tray menu, dialogs, toasts, validation messages, and computed
  service metadata.
- Search syntax: `port:3000`, `pid:1234`, `project:demo`, `process:node`,
  `cwd:path`, and `addr:127.0.0.1`.
- Sort by port, process, or project, with ascending/descending toggle.
- Quick hide/show system processes from the dashboard.
- Protected system processes cannot be killed from the UI.
- Export the current list as JSON or CSV.
- Copy service details as JSON and open a terminal in the working directory.
- Enter opens the selected service; the list has keyboard focus styling.
- Added xUnit tests for search matching, process protection, and working
  directory reading; CI now runs the test suite.
- Versioned release workflow remains tag-driven and optionally signed.

## 0.2.0 — 2026-10-21

- Window size, position, maximized state, refresh interval, auto-refresh,
  system-process visibility, sorting, and project grouping now persist locally.
- Closing the window can minimize to the tray instead of quitting.
- A second launch now activates the existing window through a named pipe
  instead of showing a blocking message box.
- Project detection reads the target process working directory from its PEB,
  so relative commands such as `node server.js` resolve correctly.
- Node project names are read from `package.json`.
- The port list can be grouped by project, using a virtualized `ListBox`.
- Added a tag-driven GitHub Release workflow with optional PFX code signing.
- Added English README and code-signing documentation.

## 0.1.0 — 2026-10-21

First public MVP.

- Real-time TCP listener scan via the Windows IP Helper API.
- Process name, PID, executable path, command line, and start time.
- Best-effort project detection from command-line paths.
- Local / LAN / all-interfaces exposure classification.
- Search, development-service filter, favorites, and conflict filter.
- Detail drawer with copy address, open in browser, and reveal path.
- Kill process tree with a confirmation dialog.
- System tray icon, configurable refresh interval, and hide-system-processes option.
- Self-contained, single-file Windows x64 build.
- Local-only operation: no telemetry and no network requests.
