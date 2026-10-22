# Contributing to PortDeck

Thanks for taking a look. PortDeck is intentionally a small, local-first Windows utility, so the best contributions are focused fixes and small quality-of-life improvements.

## Development

Requirements:

- Windows 10 1809+ (x64)
- .NET 10 SDK

```powershell
dotnet build
dotnet run
```

The app starts with a 5-second refresh timer. A tiny fixture server is available for manual testing:

```powershell
node tests/fixtures/dev-server.js 34567
```

## Before opening a PR

- `dotnet build -c Release` passes.
- `dotnet test tests\PortDeck.Tests\PortDeck.Tests.csproj -c Release` passes.
- The dashboard still scans ports and opens the detail drawer.
- Killing the fixture server (port 34567) works through the confirmation dialog.
- No new network calls or telemetry are introduced.
- New UI copy is added to both `Localization/Strings.zh-CN.json` and
  `Localization/Strings.en-US.json`.

## Code layout

| Path | Purpose |
| --- | --- |
| `Services/PortScanner.cs` | Native TCP table reader, WMI process lookup, project inference |
| `Services/ProcessWorkingDirectory.cs` | Reads another process's PEB working directory |
| `Services/FavoritesStore.cs` | Local favorites JSON |
| `Services/AppSettings.cs` | Settings model and local JSON store |
| `Services/SingleInstanceService.cs` | Mutex plus named-pipe activation |
| `Services/ProcessActions.cs` | Open URL / reveal file / kill process tree |
| `Models/PortEntry.cs` | UI model and computed display values |
| `Localization/` | JSON resources, service, XAML markup extension |
| `Services/SearchMatcher.cs` | Query syntax for port/pid/project/process/cwd/addr |
| `tests/` | xUnit tests for search, process protection, and cwd reading |
| `MainWindow.xaml` | Dashboard, detail drawer, settings |
| `Styles/Theme.xaml` | Dark theme and control styles |
| `scripts/make-icon.py` | Regenerates the icon assets |
| `scripts/sign.ps1` | Signs and verifies a local build with a PFX |
| `scripts/generate-package-manifests.ps1` | Generates Scoop and winget manifests |
| `docs/CODE_SIGNING.md` | CI signing setup and certificate options |

## Release and signing

Releases are created by pushing a `v*` tag. The workflow is in
`.github/workflows/release.yml`; SignPath setup is documented in
`docs/CODE_SIGNING.md`. Never commit signing tokens, PFX files, or their
base64 representations.

## Commit style

Short, imperative messages are preferred, for example:

```text
Add UDP listener support
Fix taskbar overlap when maximized
```
