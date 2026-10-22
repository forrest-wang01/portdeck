# PortDeck v1.1.0

This release focuses on the desktop experience: themes, table interaction, and
less visual noise.

## Highlights

- Dark and light themes with immediate switching (no application restart)
- 100% / 125% interface scale
- Draggable table columns with persisted widths
- Sortable Port / Process / Project / Address / PID headers
- Collapsible project groups with persisted expansion state
- Resizable detail drawer, plus a bottom-sheet layout on narrow windows
- Row actions consolidated into a compact “…” menu; right-click remains
- Search-hit highlighting in process, project, and address text
- Real executable icons, loaded asynchronously with a neutral fallback
- System processes hidden by default
- Settings grouped into collapsible sections

## Compatibility

- Windows 10/11 x64 and ARM64
- Self-contained single-file executables
- Local-only scanning; no telemetry

## Assets

- `PortDeck-1.1.0-win-x64.exe`
- `PortDeck-1.1.0-win-arm64.exe`
- Matching `.sha256` files

## Signing

The release workflow is wired to SignPath Foundation. Until the SignPath
organization, signing policy, and repository variables are configured, these
binaries are unsigned.

## Known limitations

- Collapsed group state is keyed by the localized group name; switching UI
  language may not map the saved state to the new label.
- Bottom-sheet details activate below 1120px; the main window minimum remains
  1080px to keep the dashboard toolbar usable.
- HTTP health checks only request `/`.
- UDP listeners cannot be health-checked.
