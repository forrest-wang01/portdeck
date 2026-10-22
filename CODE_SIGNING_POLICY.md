# Code signing policy

**Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by [SignPath Foundation](https://signpath.org).**

PortDeck is an MIT-licensed, local-first Windows development tool. It is not a
vulnerability scanner: it does not probe remote hosts, exploit services, or
report exploitable vulnerabilities. It displays listeners that already exist
on the local machine.

## Signed artifacts

Only Windows executables built by the GitHub Actions workflow in
[.github/workflows/release.yml](.github/workflows/release.yml) from this
repository are eligible for signing:

- `PortDeck-*-win-x64.exe`
- `PortDeck-*-win-arm64.exe`

The release workflow uploads the unsigned executables as a GitHub Actions
artifact, submits them to SignPath, verifies the returned Authenticode
signature, and publishes only the signed files.

## Team roles

> Replace the placeholders below with the actual GitHub accounts/teams before
> applying to SignPath Foundation.

- **Authors / committers**: the repository owner and maintainers with write
  access.
- **Reviewers**: every pull request from a non-committer is reviewed by a
  maintainer.
- **Approvers**: each signing request is manually approved by the repository
  owner.

Initial maintainer: `forrest-wang01`

## Security requirements

- All maintainers use multi-factor authentication for GitHub and SignPath.
- Builds and signing run only on GitHub-hosted runners.
- The private signing key stays in SignPath's HSM; no PFX is stored in GitHub.
- Every release is manually approved in SignPath.
- The project has no proprietary or commercially dual-licensed components.

## Privacy

See [PRIVACY.md](PRIVACY.md). In short: PortDeck does not include telemetry,
does not upload scan results, and does not contact the network unless the user
enables or explicitly requests an update check.

## Verification

Users can verify a downloaded release with:

```powershell
Get-AuthenticodeSignature .\PortDeck-1.0.0-win-x64.exe | Format-List *
```

Expected: `Status : Valid` and a SignPath Foundation certificate.
