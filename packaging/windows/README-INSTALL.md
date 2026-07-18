# CouchCTRL Windows Companion x64 Package

This package installs the CouchCTRL Windows Companion tray agent and CLI for the current Windows user.

If you received `CouchControlSetup-win-x64.exe`, run it and follow the wizard.

If you received `CouchControl-win-x64.zip`, extract it and use the PowerShell installer below.

## Install

From the extracted package root:

```powershell
.\install.ps1
```

To also start the tray agent at login for the current Windows user:

```powershell
.\install.ps1 -StartAtLogin
```

The default install location is:

```text
%LOCALAPPDATA%\Programs\CouchControl
```

## Update

To update, install the newer CouchCTRL Windows Companion package over the existing installation. User configuration, pairing tokens, snapshots, and logs are kept under `%LOCALAPPDATA%\CouchControl`.

For downloads, support, and privacy details:

- https://couchctrl.app/download
- https://couchctrl.app/support
- https://couchctrl.app/privacy

The installer creates Start Menu shortcuts under the current user's Start Menu.

## Uninstall

```powershell
%LOCALAPPDATA%\Programs\CouchControl\uninstall.ps1
```

By default, uninstall removes application files, Start Menu shortcuts, and the current-user startup entry. It preserves user configuration, pairing tokens, snapshots, and logs under:

```text
%LOCALAPPDATA%\CouchControl
```

To remove user data explicitly:

```powershell
%LOCALAPPDATA%\Programs\CouchControl\uninstall.ps1 -RemoveUserData
```

## Build the setup wizard

The `.exe` wizard is built with Inno Setup on Windows after the package payload exists:

```powershell
.\scripts\publish-win-x64.ps1
.\packaging\windows\build-setup-exe.ps1
```

The output is:

```text
artifacts\win-x64\CouchControlSetup-win-x64.exe
```
