# SignPath Foundation application

Application URL: <https://signpath.org/apply>

Repository: <https://github.com/forrest-wang01/portdeck>

Release: <https://github.com/forrest-wang01/portdeck/releases/tag/v1.1.0>

## Copy-paste project summary

PortDeck is a local-first Windows desktop dashboard for developers. It shows
TCP/UDP listeners, owning processes and projects, exposure, health checks,
activity history, and Docker/WSL context. It does not scan remote hosts, does
not exploit services, and does not report vulnerabilities. It is MIT licensed,
built entirely with C#/.NET/WPF, and released as self-contained x64 and ARM64
executables.

## Application fields

| Field | Value |
| --- | --- |
| Project name | `PortDeck` |
| Repository | `https://github.com/forrest-wang01/portdeck` |
| Download / releases | `https://github.com/forrest-wang01/portdeck/releases` |
| License | MIT |
| Build system | GitHub Actions |
| Signing policy | `https://github.com/forrest-wang01/portdeck/blob/main/CODE_SIGNING_POLICY.md` |
| Privacy policy | `https://github.com/forrest-wang01/portdeck/blob/main/PRIVACY.md` |
| Maintainer | `forrest-wang01` |
| MFA | Enabled on GitHub (required before applying) |

## Eligibility notes

- The project is public and has a real v1.1.0 release.
- It uses the MIT license and contains no proprietary components.
- It is maintained and documented in the README.
- It is a developer productivity tool, not a hacking or vulnerability scanner.
- Signing is limited to the two executables built by GitHub Actions from this
  repository.
- The private key will remain in SignPath's HSM; no PFX is stored in GitHub.

## After approval

1. Install the [SignPath GitHub App](https://github.com/apps/signpath).
2. Create the SignPath project `portdeck`.
3. Link the **GitHub.com** trusted build system.
4. Create the signing policy `release-signing`.
5. Create the artifact configuration `portdeck-windows` from
   [`signpath/artifact-configuration.xml`](../signpath/artifact-configuration.xml).
6. Create an API token with submitter permission.
7. Add the GitHub secret and variables documented in
   [`CODE_SIGNING.md`](CODE_SIGNING.md).
8. Push a new tag, then approve the signing request in SignPath.
