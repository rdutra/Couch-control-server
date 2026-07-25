# CouchControl

CouchControl is a Windows-focused companion agent for a gaming PC that can switch between a normal desktop monitor layout and a couch gaming setup. In desktop mode, the ultrawide monitor configuration remains active. In couch mode, the living-room TV becomes the only active display and Steam launches directly into Big Picture mode.

## Warning

Activating Couch Mode changes the active Windows display topology. It can disable your other monitors, move your primary desktop to the configured TV, and temporarily interrupt the local desktop session while Windows applies the new topology and mode. Save your normal desktop layout in the tray app before activating Couch Mode.

CouchCTRL cannot be tested against every combination of GPU, driver, monitor, TV, dock, receiver, cable, refresh rate, scaling setting, and Windows version. In rare cases, Windows may retain a broken display topology after an unsuccessful switch. Recovery can require resetting Windows' cached display configuration, including the `Configuration`, `Connectivity`, and `ScaleFactors` registry subkeys described in [Emergency display recovery](#emergency-display-recovery). This is a system-wide, last-resort operation—not a normal CouchCTRL configuration reset.

## Architecture

The solution is split into five projects:

- `src/CouchControl.Core`: platform-agnostic domain models, orchestration, result types, and interfaces.
- `src/CouchControl.Windows`: Windows-specific implementations for display switching, Steam launching, persistence, startup registration, and single-instance coordination.
- `src/CouchControl.Cli`: the console host that wires dependency injection and logging together.
- `src/CouchControl.Agent`: the WinForms tray agent for the interactive user session.
- `tests/CouchControl.Core.Tests`: xUnit tests for the core domain and result semantics.

The orchestration logic stays in `CouchControl.Core` so the mode-switching rules remain independent from the Windows APIs that execute them.

## Why This Runs In The Interactive Session

This agent is intended to manipulate display topology and launch a user-facing application. Those operations belong in the interactive Windows session, not in a traditional Session 0 Windows service.

Running in Session 0 would create the wrong execution model for this project:

- Session 0 services do not have the user desktop context needed for reliable display changes.
- Steam Big Picture is an interactive UI application and should launch in the signed-in user session.
- Modern Windows isolates services from the desktop, which makes user-session display state and shell interaction significantly harder and less reliable.

For that reason, CouchControl runs as a user-session companion agent rather than a background service detached from the active desktop.

## Quickstart

### 1. Install and launch CouchControl

Run `CouchControlSetup-win-x64.exe`. When installation finishes, launch CouchControl Agent. CouchControl runs in the Windows notification area; right-click its tray icon to open the menu.

On first launch, CouchControl opens the Settings window and explains the required setup.

### 2. Configure Couch Mode

Open **Settings** from the tray menu:

- In **Display**, select the couch TV and confirm its resolution and refresh rate.
- While your normal monitor layout is active, click **Save Current Desktop Snapshot**. This is the layout restored by Desktop Mode.
- In **Audio**, optionally select the playback devices for Couch Mode and Desktop Mode.
- In **Apps**, choose **None**, **Steam — Big Picture**, or **Heroic — Console Mode**. Leave the executable paths empty to use automatic detection.
- In **Network**, configure the mobile-app connection if needed. Firewall changes occur only when you click the corresponding firewall button.
- In **System**, optionally enable **Start with Windows**.
- Click **Save**.

If your normal monitor arrangement changes later, save a new desktop snapshot from **Settings**, **Status**, or the tray menu.

### 3. Switch modes

Use the tray menu:

- **Activate Couch Mode** switches to the configured TV, changes audio if configured, and optionally launches Steam in Big Picture or Heroic in Console Mode.
- **Restore Desktop Mode** restores the saved monitor layout and desktop audio device.

The tray menu also provides **Status**, **Network Diagnostics**, **Pair Device**, **Show Configuration Folder**, and **View Logs**.

The CLI is not required for setup or normal operation. It remains available for scripting, diagnostics, JSON output, and dry-run validation.

## Building and Packaging

### Build everything

From the solution root:

```bash
dotnet build CouchControl.sln
```

### Package the Windows installer

Create the Windows x64 package from the repository root:

```powershell
.\scripts\publish-win-x64.ps1
```

The package output is written to:

```text
artifacts\win-x64\CouchControl\
artifacts\win-x64\CouchControl-win-x64.zip
```

The package script publishes self-contained `win-x64` builds for the tray agent and CLI, copies the installer support files, removes the generated .NET dump helper executable, and creates the zip package.

To build the setup wizard on Windows, install Inno Setup and run:

```powershell
.\packaging\windows\build-setup-exe.ps1
```

To build the same setup wizard from macOS, install NSIS and run:

```bash
brew install nsis
./packaging/windows/build-nsis-setup.sh
```

Both setup wizard paths produce:

```text
artifacts\win-x64\CouchControlSetup-win-x64.exe
```

If you need a completely fresh installer, remove the generated output first and rebuild:

```powershell
Remove-Item artifacts\win-x64 -Recurse -Force
.\scripts\publish-win-x64.ps1
.\packaging\windows\build-setup-exe.ps1
```

Or from macOS:

```bash
rm -rf artifacts/win-x64
dotnet publish src/CouchControl.Agent/CouchControl.Agent.csproj --configuration Release --runtime win-x64 --self-contained true -p:PublishSingleFile=false -p:PublishTrimmed=false --output artifacts/win-x64/CouchControl/agent
dotnet publish src/CouchControl.Cli/CouchControl.Cli.csproj --configuration Release --runtime win-x64 --self-contained true -p:PublishSingleFile=false -p:PublishTrimmed=false --output artifacts/win-x64/CouchControl/cli
find artifacts/win-x64/CouchControl -name createdump.exe -delete
cp packaging/windows/install.ps1 artifacts/win-x64/CouchControl/install.ps1
cp packaging/windows/uninstall.ps1 artifacts/win-x64/CouchControl/uninstall.ps1
cp packaging/windows/README-INSTALL.md artifacts/win-x64/CouchControl/README-INSTALL.md
cp docs/PRIVACY.md artifacts/win-x64/CouchControl/PRIVACY.md
cp docs/SUPPORT.md artifacts/win-x64/CouchControl/SUPPORT.md
printf '1.1.1\n' > artifacts/win-x64/CouchControl/VERSION
cd artifacts/win-x64/CouchControl && zip -qry ../CouchControl-win-x64.zip . && cd ../../..
./packaging/windows/build-nsis-setup.sh
```

On the target Windows PC, run the setup wizard, or extract the zip and run:

```powershell
.\install.ps1
```

## CLI Usage

### Display Enumeration

To list all connected and active displays, run:

```bash
CouchControl.Cli displays
```

Example human-readable output:

```text
Connected displays:

[1] Samsung TV
    Active: Yes
    Primary: No
    Resolution: 3840x2160
    Refresh rate: 60 Hz
    Device path: \\?\DISPLAY#SAM0F8C#4&2d364fa6&0&UID8388608

[2] Alienware AW3423DWF
    Active: Yes
    Primary: Yes
    Resolution: 3440x1440
    Refresh rate: 165 Hz
    Device path: \\?\DISPLAY#DEL41A8#4&2d364fa6&0&UID8388609
```

### JSON Output

To retrieve display information in a machine-readable JSON format, use the `--json` option:

```bash
CouchControl.Cli displays --json
```

### Couch Mode Activation

After configuring the target TV and preferred mode, activate couch mode with:

```bash
CouchControl.Cli couch
```

`couch` requires a saved desktop snapshot from `CouchControl.Cli snapshot capture`. The command captures a temporary rollback snapshot for the current run, but it does not replace the saved desktop baseline.

CouchControl also prunes stale snapshot files automatically. On disk it keeps the saved desktop baseline and, while a couch switch is in progress, at most one temporary rollback snapshot for recovery.

If a couch audio device is configured, Couch mode switches Windows to that playback device after the display switch succeeds. If a desktop audio device is configured, Desktop mode switches Windows back after the desktop display restore succeeds.

If the target TV is currently inactive, `couch` first tries `DisplaySwitch.exe /extend` to wake that HDMI path before disabling the current monitor. If a TV preparation command is configured, CouchControl retries that command and attempts the display switch once more before giving up. If the TV still does not become active, CouchControl aborts the switch and leaves the current desktop display in place.

On most PCs, CouchControl itself cannot generate Nintendo Switch-style HDMI-CEC power-on behavior through the GPU alone. To wake the TV or force the correct input, configure a TV preparation command that calls a utility or integration that your hardware actually supports.

To validate the configured TV selection and mode resolution without calling `SetDisplayConfig`, use:

```bash
CouchControl.Cli couch --dry-run
```

Expected high-level console output:

```text
TV detected: Samsung TV
Switching to TV-only topology
Applying 3840x2160 at 60 Hz
Verifying display configuration
Couch display active
```

### Live Testing On Windows

For a first live test:

1. Arrange your desktop monitors exactly as you want Desktop Mode to restore them.
2. Start `CouchControl.Agent.exe`. The first-run setup opens automatically if no configuration exists.
3. In **Settings > Display**, select the TV, confirm the preferred mode, and click **Save Current Desktop Snapshot**.
4. Optionally configure audio, Steam, networking, pairing, and startup behavior in the other Settings tabs.
5. Click **Save**, then choose **Activate Couch Mode** from the tray menu.
6. Confirm that the TV is the only active display and that configured audio and Steam behavior were applied.
7. Choose **Restore Desktop Mode** and confirm that the saved desktop monitor layout returns.
8. Use **Status**, **Network Diagnostics**, and **View Logs** from the tray menu if either operation fails.

Closing the Settings or Status window does not stop the agent. Starting a second instance reuses the existing tray process.

### Resetting CouchCTRL

CouchCTRL stores its configuration, paired-device tokens, display snapshots, and logs under:

```text
%LOCALAPPDATA%\CouchControl
```

It does not store application configuration in the Windows registry. The only CouchCTRL registry value is the optional **Start with Windows** entry.

To reset only the saved settings:

1. Exit CouchCTRL from its notification-area menu.
2. Open `%LOCALAPPDATA%\CouchControl` in File Explorer.
3. Rename `config.json` to `config.json.backup`.
4. Start CouchCTRL and configure it again.

To perform a complete factory reset, exit CouchCTRL and rename the entire `%LOCALAPPDATA%\CouchControl` folder to `CouchControl.backup`. This also resets pairing, saved display snapshots, the operation journal, and local logs. Keep the backup until the new configuration is working.

To remove the optional current-user startup entry, turn off **Start with Windows** in Settings or run:

```powershell
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v "CouchControl.Agent" /f
```

Do not delete the entire Windows `Run` registry key; other applications use it.

### Emergency display recovery

If a failed display switch leaves monitors blank, unavailable, incorrectly scaled, or unusable after CouchCTRL exits, try these safer Windows recovery steps first:

1. Press <kbd>Windows</kbd>+<kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>B</kbd> to reset the graphics driver.
2. Restart Windows and use <kbd>Windows</kbd>+<kbd>P</kbd> to select a usable display mode.
3. Disconnect nonessential displays, docks, adapters, and receivers, then reconnect them one at a time.
4. If necessary, start Windows in Safe Mode and repair or reinstall the display driver.

As a last resort, some systems may recover only after Windows' cached display topology is cleared from:

```text
HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\GraphicsDrivers\Configuration
HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\GraphicsDrivers\Connectivity
HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\GraphicsDrivers\ScaleFactors
```

> **Registry warning:** These keys belong to Windows, require administrator access, and contain system-wide display configuration for every connected display—not CouchCTRL settings. Deleting them causes Windows to rebuild its display cache and can remove saved monitor positions, modes, and scaling. Editing the wrong registry location can make Windows unstable or unbootable. Back up the `GraphicsDrivers` key or create a System Restore point first. If you are unsure, stop and use Windows or GPU-vendor support.

After backing up, exit CouchCTRL, disconnect nonessential external displays, delete only the three subkeys listed above in Registry Editor, restart Windows, wait for the desktop to finish loading, and reconnect displays one at a time. You will need to configure display arrangement, resolution, refresh rate, scaling, and the CouchCTRL desktop snapshot again.

### Current Limitations

- Desktop Mode restores the snapshot explicitly saved in the tray app; Couch Mode does not silently replace that baseline.
- Old rollback snapshot files are pruned automatically; only the saved desktop baseline and the current in-progress rollback snapshot are retained.
- Rollback is attempted automatically only when the switch or post-switch verification fails during the `couch` operation.
- If the configured TV is off or HDMI-CEC/input switching does not wake it, `couch` retries the configured TV preparation command once more and then aborts before detaching the active desktop monitor.
- Native integration tests are intentionally skipped by default because they would change active displays during a normal test run.

When JSON output is requested, any diagnostic logging is redirected to standard error (`stderr`), keeping standard output (`stdout`) pure and directly parseable:

```json
[
  {
    "Identifier": {
      "Value": "\\\\?\\DISPLAY#SAM0F8C#4&2d364fa6&0&UID8388608"
    },
    "FriendlyName": "Samsung TV",
    "IsActive": true,
    "IsPrimary": false,
    "CurrentMode": {
      "Width": 3840,
      "Height": 2160,
      "RefreshRateHz": 60,
      "IsValid": true
    },
    "DevicePath": "\\\\?\\DISPLAY#SAM0F8C#4&2d364fa6&0&UID8388608",
    "AdapterLuid": "00000000:00010c28",
    "SourceId": 0,
    "TargetId": 4120,
    "OutputTechnology": "HDMI"
  }
]
```


### Optional CLI Configuration

Normal configuration is available through the tray app's Settings window. For scripting or troubleshooting, the CLI can manage the same configuration stored in `%LocalAppData%\CouchControl\config.json`.

#### 1. List Displays with Stable Short IDs
Lists connected displays along with their 8-character stable deterministic short IDs derived from their device paths:
```bash
CouchControl.Cli configure list-displays
```

Example output:
```text
Connected displays:

[f52d3a7e] Samsung TV
    Active: Yes
    Primary: No
    Resolution: 3840x2160
    Refresh rate: 60 Hz
    Device path: \\?\DISPLAY#SAM0F8C#4&2d364fa6&0&UID8388608
```

#### 2. List Playback Audio Devices
Lists active playback devices together with their full device IDs:
```bash
CouchControl.Cli configure list-audio-devices
```

#### 3. Set Couch Display (TV)
Configures the couch display by passing its 8-character stable short ID or full device path. It validates that the display exists, parses its hardware manufacturer/product identity, and saves it:
```bash
CouchControl.Cli configure set-tv --display-id "f52d3a7e"
```

#### 4. Set Preferred Couch Display Mode
Configures the preferred resolution and refresh rate for couch mode:
```bash
CouchControl.Cli configure set-mode --width 3840 --height 2160 --refresh-rate 60
```

#### 5. Configure Steam Auto-Launch
Configures whether Steam Big Picture should launch automatically upon entering couch mode:
```bash
CouchControl.Cli configure set-steam --enabled true
```

To choose any supported launcher explicitly:
```bash
CouchControl.Cli configure set-launcher --launcher heroic
```
Valid values are `none`, `steam`, and `heroic`.

#### 6. Set Couch Audio Device
Configures the playback device that should become default in Couch mode:
```bash
CouchControl.Cli configure set-couch-audio-device --device-id "your-tv-audio-device-id"
```

#### 7. Set Desktop Audio Device
Configures the playback device that should become default in Desktop mode:
```bash
CouchControl.Cli configure set-desktop-audio-device --device-id "your-desktop-audio-device-id"
```

#### 8. Show Current Configuration
Prints the current configuration state:
```bash
CouchControl.Cli configure show
```

#### 9. Optional: Configure Couch Audio Command Fallback
Configures a command that should run after Couch mode becomes active if you prefer an external utility instead of direct device selection:
```bash
CouchControl.Cli configure set-couch-audio --command "your-audio-switch-command-for-tv"
```

#### 10. Optional: Configure Desktop Audio Command Fallback
Configures a command that should run after Desktop mode is restored if you prefer an external utility instead of direct device selection:
```bash
CouchControl.Cli configure set-desktop-audio --command "your-audio-switch-command-for-desktop"
```

### Snapshot Management

The desktop restore baseline can be saved or cleared from the tray menu, Settings window, or Status window. Equivalent CLI commands are available for scripting and troubleshooting.

#### 1. Capture Desktop Snapshot
Saves the current desktop topology as the baseline that Desktop Mode restores later:
```bash
CouchControl.Cli snapshot capture
```

#### 2. Show Saved Desktop Snapshot
Displays the currently saved desktop baseline:
```bash
CouchControl.Cli snapshot show
```
