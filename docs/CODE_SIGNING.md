# Code signing with SignPath Foundation

PortDeck uses [SignPath Foundation](https://signpath.org/) for free code
signing of eligible open-source projects. The private signing key stays inside
SignPath; GitHub Actions only submits an artifact and downloads the signed
result.

The workflow is in `.github/workflows/release.yml`. If
`SIGNPATH_API_TOKEN` is not configured, the release still publishes, but the
binaries are unsigned.

## How it works

1. The tag workflow builds the x64 and ARM64 single-file executables.
2. Both exes are staged in `signing-input/` and uploaded as one GitHub
   Actions artifact (a ZIP).
3. SignPath validates the GitHub workflow, origin, artifact, and policy.
4. SignPath signs both PE files using the artifact configuration in
   [`signpath/artifact-configuration.xml`](../signpath/artifact-configuration.xml).
5. The workflow copies the signed files back into `dist/` and
   `dist-arm64/`, verifies the Authenticode signature with
   `Get-AuthenticodeSignature`, then generates checksums and publishes the
   GitHub Release.

The artifact configuration expects a ZIP whose root contains:

```text
PortDeck-<version>-win-x64.exe
PortDeck-<version>-win-arm64.exe
```

## Required GitHub configuration

### Secret

| Name | Value |
| --- | --- |
| `SIGNPATH_API_TOKEN` | API token for a SignPath user with submitter permission |

Create it in SignPath under the user's API tokens. Do not commit it.

### Repository variables

| Variable | Example | Meaning |
| --- | --- | --- |
| `SIGNPATH_ORGANIZATION_ID` | `00000000-0000-0000-0000-000000000000` | SignPath organization ID |
| `SIGNPATH_PROJECT_SLUG` | `portdeck` | SignPath project slug |
| `SIGNPATH_SIGNING_POLICY_SLUG` | `release-signing` | Signing policy slug |
| `SIGNPATH_ARTIFACT_CONFIGURATION_SLUG` | `portdeck-windows` | Artifact configuration slug |

Set them with GitHub CLI:

```powershell
gh secret set SIGNPATH_API_TOKEN
gh variable set SIGNPATH_ORGANIZATION_ID --body "<organization-id>"
gh variable set SIGNPATH_PROJECT_SLUG --body "portdeck"
gh variable set SIGNPATH_SIGNING_POLICY_SLUG --body "release-signing"
gh variable set SIGNPATH_ARTIFACT_CONFIGURATION_SLUG --body "portdeck-windows"
```

`SIGNPATH_API_TOKEN` can also be configured as an organization secret.

The release workflow runs a preflight step and fails with a clear list if the
token exists but any of the four variables are missing.

## How to apply

Apply at **<https://signpath.org/apply>**. A prepared summary is in
[`SIGNPATH_APPLICATION.md`](SIGNPATH_APPLICATION.md). Before submitting, make
sure the project already satisfies the Foundation conditions:

- Public GitHub repository with an OSI-approved license (PortDeck uses MIT).
- Already released in the form to be signed (publish a `v1.0.0` release first).
- Documented functionality and download page (README + Releases).
- Code signing policy on the project home page. PortDeck has this in
  [`CODE_SIGNING_POLICY.md`](../CODE_SIGNING_POLICY.md) and the README.
- Privacy policy. PortDeck has this in [`PRIVACY.md`](../PRIVACY.md).
- At least one maintainer with MFA on GitHub and SignPath.
- Clear author/reviewer/approver roles. The initial maintainer is
  `forrest-wang01` in `CODE_SIGNING_POLICY.md`.
- The project must not be a vulnerability scanner or include hacking tools.
  PortDeck is a local development dashboard; describe it that way in the form.

### Suggested application answers

| Field | Suggested answer |
| --- | --- |
| Project name | `PortDeck` |
| Repository | `https://github.com/forrest-wang01/portdeck` |
| Download / releases | `https://github.com/forrest-wang01/portdeck/releases` |
| License | MIT |
| Build system | GitHub Actions |
| Description | Local-first Windows TCP/UDP service dashboard for developers; process/project metadata, health checks, history, Docker/WSL context; no remote scanning or vulnerability detection |
| Signing policy | Link to `CODE_SIGNING_POLICY.md` |
| Privacy policy | Link to `PRIVACY.md` |
| Maintainers | Your GitHub account and any co-maintainers |

SignPath evaluates project reputation and control. A brand-new repository with
no release history is harder to approve; publish the repository and a real
release before applying. Do not manufacture stars or downloads.

### What happens after approval

1. You get access to the SignPath organization.
2. Install the [SignPath GitHub App](https://github.com/apps/signpath) and
   allow access to the repository.
3. Create the project, trusted build system, signing policy, and artifact
   configuration as described below.
4. Create an API token with submitter permission.
5. Configure the GitHub secret and variables.
6. Push a release tag; each signing request is manually approved in SignPath.
   The workflow waits up to 30 minutes for that approval.

If the application is rejected, the practical fallbacks are Azure Trusted
Signing or a traditional OV certificate with a cloud-signing option. Do not use
a self-signed certificate for public releases.

## SignPath onboarding

1. Apply to [SignPath Foundation](https://signpath.org/) and wait for approval.
2. Install the [SignPath GitHub App](https://github.com/apps/signpath) and
   grant it access to the PortDeck repository.
3. In SignPath, create a project for PortDeck and note its slug.
4. Add the predefined **GitHub.com** trusted build system to the organization
   and link it to the project.
5. Create a signing policy for release tags (for example `v*`). Note its slug.
6. Create an artifact configuration from
   [`signpath/artifact-configuration.xml`](../signpath/artifact-configuration.xml)
   and note its slug. The root must be `zip-file` because
   `actions/upload-artifact` creates a ZIP.
7. Create an API token for a user with submitter permission on that project and
   add it as `SIGNPATH_API_TOKEN`.
8. Push a release tag. The workflow uploads the unsigned ZIP, waits for
   SignPath, downloads the signed ZIP, replaces both exes, and verifies them.

SignPath Foundation requires all jobs up to the signing request to run on
GitHub-hosted runners. The current release workflow satisfies that.

## Verification

The workflow fails if either signed file reports a status other than `Valid`:

```powershell
Get-AuthenticodeSignature .\dist\PortDeck-1.0.0-win-x64.exe | Format-List *
```

Expected:

```text
Status        : Valid
StatusMessage : Signature verified.
SignerCertificate : CN=...
```

You can also verify with the Windows SDK:

```powershell
signtool verify /pa /v .\dist\PortDeck-1.0.0-win-x64.exe
```

## Fallback: unsigned release

If `SIGNPATH_API_TOKEN` is absent, the SignPath steps are skipped and the
release is published unsigned. That is intentional so a fork can build and
release without a SignPath account.

To make signing mandatory, remove the `if: env.HAS_SIGNPATH == 'true'`
conditions from the SignPath steps and let the workflow fail when the secret is
missing.

## Local PFX signing (development only)

The repository still has `scripts/sign.ps1` for testing a PFX flow locally:

```powershell
$env:PORTDECK_SIGNING_PASSWORD = "password"
.\scripts\sign.ps1 `
  -CertificatePath C:\certs\portdeck.pfx `
  -FilePath .\dist\PortDeck-1.0.0-win-x64.exe
```

This is not the release path. For production, use SignPath so the private key
is not stored in GitHub.

## SmartScreen reputation

A valid signature proves the publisher identity, but SmartScreen reputation
also depends on download volume and certificate age. A new certificate can
still produce a warning initially. Do not promise users that signing removes
every warning immediately.

## Troubleshooting

- **Artifact configuration mismatch**: confirm the uploaded ZIP has both exes
  at its root. The workflow stages them in `signing-input/` for that reason.
- **Policy rejected**: verify the tag matches the SignPath signing policy and
  all jobs used GitHub-hosted runners.
- **401/403**: regenerate the API token and check submitter permission.
- **Only one file signed**: confirm both `pe-file` entries exist in the
  artifact configuration.
- **Status UnknownError locally**: the SignPath certificate chain may not be
  available yet on that machine; run `signtool verify /pa /v`.
