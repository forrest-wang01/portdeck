# PortDeck v1.0.0

PortDeck is a local-first Windows dashboard for TCP/UDP services: ports,
processes, projects, exposure, health, history, and container/WSL context.

## Highlights

- Simplified Chinese / English UI with live switching
- TCP and UDP listener discovery
- Process, command line, working directory, and project inference
- TCP / HTTP health checks with latency and status code
- Activity history with up/down notifications for favorites
- Docker published-port mapping and best-effort WSL mapping
- Search syntax: `port:3000`, `pid:1234`, `project:demo`, `process:node`,
  `cwd:path`, `addr:127.0.0.1`
- Sorting, grouping, keyboard navigation, favorites, JSON/CSV export
- Protected system-process guard
- Opt-in update checks with checksum-verified download, replacement, restart
- Self-contained x64 and ARM64 builds
- Diagnostic logs in `%LOCALAPPDATA%\PortDeck\logs`
- 42 automated tests; CI builds x64 and ARM64 and runs tests

## Assets

| Asset | SHA256 |
| --- | --- |
| `PortDeck-1.0.0-win-x64.exe` | `77e1452b0a8af876772eee3b9a7fe88b5f2b871dcad37a75a69f7e0d410ba938` |
| `PortDeck-1.0.0-win-arm64.exe` | `50a78505e82c8f67b924cf15b302cb0d54ce77f56d439a3a59a125fa76b98b3d` |

Each asset has a matching `.sha256` file. The ARM64 build is produced by CI
cross-compilation and has not yet been tested on physical ARM64 hardware.

## Required maintainer setup

- Verify `RepositoryUrl` points to the real repository
  (`forrest-wang01/portdeck`) before enabling update checks or submitting
  package manifests.
- Configure SignPath (`SIGNPATH_API_TOKEN` plus the four SignPath repository
  variables) for signed releases. Without them the binaries are unsigned.
- Re-generate `packaging/` manifests with the real GitHub owner and checksums.
- Run a real WSL smoke test before advertising WSL support as fully verified.

## Known limitations

- HTTP health checks only request `/`; they do not validate APIs or WebSockets.
- UDP listeners cannot be health-checked.
- Automatic updates require a writable install directory.
- WSL/Docker mapping is best effort and depends on local tools.
