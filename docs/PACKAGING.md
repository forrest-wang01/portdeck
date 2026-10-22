# Packaging and distribution

PortDeck publishes two self-contained Windows binaries from a tag:

```text
PortDeck-X.Y.Z-win-x64.exe
PortDeck-X.Y.Z-win-arm64.exe
```

Each release also contains a matching `.sha256` file. The x64 build is the
primary download; the ARM64 build is produced by CI and should be tested on
physical ARM64 hardware before being promoted widely.

## Generate manifests

After a release, generate both Scoop and winget manifests from the real
checksums:

```powershell
.\scripts\generate-package-manifests.ps1 `
  -Version 1.0.0 `
  -Repository forrest-wang01/portdeck `
  -Sha256 <x64-sha256> `
  -Arm64Sha256 <arm64-sha256>
```

The script writes `packaging/scoop/portdeck.json` and
`packaging/winget/PortDeck/*.yaml`. Replace the repository if you fork the
project.

## winget

The easiest path is `wingetcreate` after a GitHub Release exists:

```powershell
winget install Microsoft.WingetCreate
wingetcreate new https://github.com/forrest-wang01/portdeck/releases/download/v0.4.0/PortDeck-0.4.0-win-x64.exe
```

Use these values in the generated manifest:

| Field | Value |
| --- | --- |
| PackageIdentifier | `PortDeck.PortDeck` |
| Publisher | `PortDeck contributors` |
| License | `MIT` |
| ShortDescription | `Local TCP/UDP port and dev-service dashboard for Windows` |
| Moniker | `portdeck` |

Validate and submit:

```powershell
winget validate .\manifests\p\PortDeck\PortDeck
wingetcreate submit --token <github-token> .\manifests\p\PortDeck\PortDeck
```

Update the manifest for each release; `wingetcreate update` can consume a new
release URL and checksum.

## Scoop

A bucket manifest can use the versioned release URL, for example:

```json
{
  "version": "0.4.0",
  "description": "Local TCP/UDP port and dev-service dashboard for Windows",
  "homepage": "https://github.com/forrest-wang01/portdeck",
  "license": "MIT",
  "url": "https://github.com/forrest-wang01/portdeck/releases/download/v$version/PortDeck-$version-win-x64.exe",
  "hash": "<sha256-from-release>",
  "bin": "PortDeck-$version-win-x64.exe",
  "shortcuts": [
    ["PortDeck-$version-win-x64.exe", "PortDeck"]
  ],
  "checkver": {
    "github": "https://github.com/forrest-wang01/portdeck"
  },
  "autoupdate": {
    "url": "https://github.com/forrest-wang01/portdeck/releases/download/v$version/PortDeck-$version-win-x64.exe",
    "hash": {
      "url": "$url.sha256"
    }
  }
}
```

Scoop's `hash.url` expects a checksum file; PortDeck publishes
`PortDeck-X.Y.Z-win-x64.exe.sha256`, so the autoupdate hash URL should be
`https://github.com/forrest-wang01/portdeck/releases/download/v$version/PortDeck-$version-win-x64.exe.sha256`.

Install locally while developing a bucket:

```powershell
scoop install .\portdeck.json
```

## Chocolatey

A Chocolatey package can wrap the same installer or portable exe. Keep the
checksum in `chocolateyinstall.ps1` in sync with the release.

## Signing

Do not publish a package until the maintainer has decided whether releases are
signed. See [CODE_SIGNING.md](CODE_SIGNING.md). Store credentials only in
GitHub Actions secrets.

## Release checklist

- [ ] `vX.Y.Z` tag pushed and Release created.
- [ ] x64 and ARM64 assets uploaded.
- [ ] Both `.sha256` files uploaded.
- [ ] Package manifests point at the new version and checksum.
- [ ] `winget validate` passes.
- [ ] Scoop manifest installs and launches PortDeck.
- [ ] Signing status documented in the release notes.
