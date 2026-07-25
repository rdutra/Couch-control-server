# CouchCTRL Support

Support page: https://couchctrl.app/support

Download page: https://couchctrl.app/download

Privacy policy: https://couchctrl.app/privacy

## Requirements

- iPhone running the CouchCTRL mobile app
- Windows gaming PC running the free CouchCTRL Windows Companion
- iPhone and PC connected to the same local network
- Steam installed on the Windows PC for Steam Big Picture launch

## Updating the Windows Companion

Download the newest Windows Companion from the CouchCTRL website and install it over the existing installation. Pairing tokens, configuration, snapshots, and logs are preserved.

## Resetting the Windows Companion

CouchCTRL stores its configuration and user data under `%LOCALAPPDATA%\CouchControl`, not in the Windows registry. The only CouchCTRL registry value is the optional **Start with Windows** entry.

To reset only the saved settings:

1. Exit CouchCTRL from its notification-area menu.
2. Open `%LOCALAPPDATA%\CouchControl` in File Explorer.
3. Rename `config.json` to `config.json.backup`.
4. Start CouchCTRL and configure it again.

For a complete factory reset, exit CouchCTRL and rename the entire `%LOCALAPPDATA%\CouchControl` folder to `CouchControl.backup`. This also resets paired devices, display snapshots, the operation journal, and logs.

To remove the optional startup entry, turn off **Start with Windows** in Settings or run:

```powershell
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v "CouchControl.Agent" /f
```

Do not delete the entire Windows `Run` registry key.

## Emergency display recovery

CouchCTRL cannot be tested against every combination of GPU, driver, monitor, TV, dock, receiver, cable, refresh rate, scaling setting, and Windows version. In rare cases, Windows may retain a broken display topology after an unsuccessful switch.

If monitors remain blank, unavailable, incorrectly scaled, or unusable after CouchCTRL exits:

1. Press **Windows+Ctrl+Shift+B** to reset the graphics driver.
2. Restart Windows and use **Windows+P** to select a usable display mode.
3. Disconnect nonessential displays, docks, adapters, and receivers, then reconnect them one at a time.
4. If necessary, start Windows in Safe Mode and repair or reinstall the display driver.

As a last resort, some systems may recover only after Windows' cached display topology is cleared from:

```text
HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\GraphicsDrivers\Configuration
HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\GraphicsDrivers\Connectivity
HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\GraphicsDrivers\ScaleFactors
```

**Registry warning:** These keys belong to Windows, require administrator access, and contain system-wide display configuration for every connected display. Deleting them causes Windows to rebuild its display cache and can remove saved monitor positions, modes, and scaling. Editing the wrong registry location can make Windows unstable or unbootable. Back up the `GraphicsDrivers` key or create a System Restore point first. If you are unsure, use Windows or GPU-vendor support.

After backing up, exit CouchCTRL, disconnect nonessential external displays, delete only the three subkeys listed above in Registry Editor, restart Windows, wait for the desktop to finish loading, and reconnect displays one at a time. Configure the display arrangement, resolution, refresh rate, scaling, and CouchCTRL desktop snapshot again.
