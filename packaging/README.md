# Package manifests

These files are generated for PortDeck v1.0.0 from
`forrest-wang01/portdeck`. The hashes match the v1.0.0 artifacts built from
this repository; rebuild and regenerate after any source change.

Regenerate with real checksums:

```powershell
.\scripts\generate-package-manifests.ps1 `
  -Version 1.0.0 `
  -Repository forrest-wang01/portdeck `
  -Sha256 <PortDeck-1.0.0-win-x64.exe sha256> `
  -Arm64Sha256 <PortDeck-1.0.0-win-arm64.exe sha256>
```

Validation:

```powershell
winget validate .\packaging\winget\PortDeck
```
