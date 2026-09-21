# Volicraft Launcher

Source repository: https://github.com/lameimp/volicraft-server

The first launcher milestone is a portable Windows Forms application for an existing
WoW 3.3.5a installation.

It can:

- Detect and validate a folder containing `Wow.exe`.
- Check the configured auth and world ports.
- Verify Volicraft-owned custom archives with SHA-256 hashes.
- Download and install verified custom archives from the latest GitHub Release.
- Back up and write `Data\enUS\realmlist.wtf` or `Data\enGB\realmlist.wtf`.
- Launch `Wow.exe` using the selected client folder.
- Remember the client folder and server address under the user's AppData folder.

It does not redistribute the WoW client, install Tailscale, expose server credentials,
or modify firewall rules. Tailscale installation and authorization remain explicit user
steps.

## Build

```powershell
dotnet build .\VolicraftLauncher.csproj --configuration Release
```

The executable is produced under:

```text
bin\Release\net7.0-windows\
```

## Use

1. Start the launcher.
2. Select an existing WoW 3.3.5a folder containing `Wow.exe`.
3. Enter the server hostname or private IP address.
4. Click **Configure Client**.
5. After connectivity checks pass, click **Launch WoW**.

Every realm-list change creates a timestamped `.volicraft-backup-*` file beside the
original realm list.

The current manifest covers only these Volicraft custom archives:

- `Data\enUS\patch-enUS-4.MPQ`
- `Data\enUS\patch-enUS-5.MPQ`

The launcher does not verify or replace Blizzard-owned client files. Future downloads
should be accepted only after their manifest SHA-256 check succeeds.

## Release assets

The repository intentionally does not contain WoW client files or large MPQ files.
For a release, attach the following files to a GitHub Release:

- `patch-enUS-4.MPQ`
- `patch-enUS-5.MPQ`
- `volicraft-manifest.json`

The two MPQ files must be built from Volicraft-owned assets and their SHA-256 values
must match the manifest. Do not upload `Wow.exe`, Blizzard MPQs, `Data.zip`, server
databases, credentials, Tailscale keys, or full client archives.

The GitHub Actions workflow builds the launcher and publishes it as a workflow
artifact. A later updater feature can download release assets over HTTPS after
validating the manifest hashes.

The **Update Custom Files** button uses the latest release at:

`https://github.com/lameimp/volicraft-server/releases/latest`

Before using it, create a GitHub Release with these exact assets:

- `patch-enUS-4.MPQ`
- `patch-enUS-5.MPQ`
- `volicraft-manifest.json`

The updater downloads each asset to a temporary folder, verifies its SHA-256 hash,
backs up the existing archive, and then installs the verified file. It accepts only
the two expected `Data\enUS` patch paths.

## Tailscale

The server should be signed in to Tailscale before friends connect. Give friends
only their own non-GM game accounts and the server's Tailscale address. Do not
forward ports from the home router and do not share MySQL or remote-console
credentials.

The server-side configuration should expose only:

- `3724` for authentication
- `8085` for the world server

MySQL (`3306`) should remain bound to `127.0.0.1`, and remote administration
should remain disabled. Each friend must install Tailscale through the official
installer, join the same tailnet with explicit consent, and then enter the
server's Tailscale IP in this launcher.
