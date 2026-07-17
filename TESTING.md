# CouchControl MVP Testing

Use the Windows x64 package from `artifacts\win-x64\CouchControl-win-x64.zip` on a Windows x64 PC with the target TV connected.

## Package Install

- Extract the package.
- Run `.\install.ps1`.
- Confirm `CouchControl Agent` and `CouchControl CLI` appear in the current user's Start Menu.
- Optional: rerun install with `.\install.ps1 -StartAtLogin` and confirm `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\CouchControl.Agent` is present.
- Confirm the agent and CLI are installed under `%LOCALAPPDATA%\Programs\CouchControl`.
- Confirm configuration and logs are under `%LOCALAPPDATA%\CouchControl`.

## Manual MVP Checklist

- Display enumeration: run `CouchControl.Cli.exe displays` and confirm all expected monitors and the TV are listed.
- TV selection: run `CouchControl.Cli.exe configure set-tv --display-id "<id>"`, then `configure show`, and confirm the selected TV is saved.
- Snapshot capture: run `CouchControl.Cli.exe snapshot capture`, then `snapshot show`, and confirm the desktop baseline is saved.
- Couch Mode: run `CouchControl.Cli.exe couch` or use the tray menu and confirm only the configured TV remains active.
- Desktop restoration: run `CouchControl.Cli.exe desktop` or use the tray menu and confirm the original desktop layout returns.
- Steam closed: close Steam, activate Couch Mode, and confirm Steam launches into Big Picture mode when enabled.
- Steam already running: start Steam first, activate Couch Mode, and confirm CouchControl reuses the running Steam process without failure.
- TV powered off: power off the TV, activate Couch Mode, and confirm the operation fails safely or succeeds after the configured TV preparation command.
- HDMI disconnected: unplug HDMI, activate Couch Mode, and confirm the current desktop remains usable and an error is reported.
- Agent restart: exit and restart the tray agent, then confirm status, pairing, saved TV, and saved snapshot remain available.
- Windows restart: restart Windows, sign in, start the agent manually or through start-at-login, and confirm status is correct.
- Concurrent API requests: send two Couch/Desktop API activation requests at the same time and confirm one is accepted while the other receives a conflict.
- Invalid authentication token: call a protected API endpoint with a bad bearer token and confirm HTTP 401.
- Pairing and revocation: pair a client, confirm it can call protected endpoints, revoke it, and confirm the same token no longer works.
- Crash recovery: terminate the agent during or immediately after a Couch Mode attempt, restart it, and confirm the recovery prompt or recovery flow can restore the desktop.

## Repeatability

Run at least 25 successful Couch/Desktop cycles on the target Windows PC.

For each cycle:

1. Start from the normal desktop layout.
2. Activate Couch Mode.
3. Confirm the TV-only layout and expected Steam behavior.
4. Restore Desktop Mode.
5. Confirm the original monitor layout and audio state are restored.

Record failures with the cycle number, visible display state, command or API response, and the latest log file from `%LOCALAPPDATA%\CouchControl\logs`.

## Uninstall

- Run `%LOCALAPPDATA%\Programs\CouchControl\uninstall.ps1`.
- Confirm application files and Start Menu shortcuts are removed.
- Confirm `%LOCALAPPDATA%\CouchControl` still exists with configuration and logs.
- Run uninstall again with `-RemoveUserData` only when intentionally validating data removal.

## Deferred

Automatic software updating is intentionally not part of this MVP.
