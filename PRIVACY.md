# Privacy policy

PortDeck is a local-first Windows desktop application.

## Data collection

PortDeck does **not** collect telemetry, analytics, crash reports, account
information, or usage statistics. It does not upload ports, process names,
command lines, project paths, container names, WSL data, or logs.

## Network access

PortDeck does not contact the network during normal scanning.

The only network request is the optional update check:

- It runs only when the user enables “check for updates” or clicks “Check now”.
- It requests the latest release metadata from `api.github.com` and, if the
  user chooses to install an update, downloads the release executable and its
  SHA256 file from GitHub Releases.
- It sends no account token, machine identifier, or scan data.
- GitHub's own privacy policy applies to those requests.

## Local commands

Docker and WSL mapping invoke local `docker` / `wsl` commands only to read
listener information. Their output stays on the machine. No remote host is
scanned.

## Local storage

The following files stay in the user's profile:

```text
%APPDATA%\PortDeck\favorites.json
%APPDATA%\PortDeck\settings.json
%APPDATA%\PortDeck\history.jsonl
%LOCALAPPDATA%\PortDeck\logs\
```

Users can delete these files at any time. The application has no server-side
storage.

## Contact

Open an issue in the project's GitHub repository for privacy questions.
