# CouchControl

CouchControl is a Windows-focused companion agent for a gaming PC that can switch between a normal desktop monitor layout and a couch gaming setup. In desktop mode, the ultrawide monitor configuration remains active. In couch mode, the living-room TV becomes the only active display and Steam launches directly into Big Picture mode.

## Warning

The current couch-mode stage changes the active Windows display topology. Running `CouchControl.Cli couch` can disable your other monitors, move your primary desktop to the configured TV, and temporarily interrupt the local desktop session while Windows applies the new topology and mode.

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

## Planned Stages

1. Foundation: solution structure, domain models, interfaces, orchestration boundaries, and tests.
2. Windows display discovery: enumerate displays, capture snapshots, and model display paths accurately.
3. Windows mode switching: activate the configured couch display as the only active output and restore the prior desktop snapshot.
4. Steam integration: detect installation state, launch Big Picture mode, and handle already-running Steam cases.
5. Persistence and operations: store configuration and snapshots, improve CLI commands, and add diagnostics and logging for real deployments.

## Quickstart

### 1. Build everything

From the solution root:

```bash
dotnet build CouchControl.sln
```

### 2. Configure the target TV and preferred mode with the CLI

List displays:

```bash
CouchControl.Cli configure list-displays
```

Save the TV:

```bash
CouchControl.Cli configure set-tv --display-id "f52d3a7e"
```

Set the preferred mode:

```bash
CouchControl.Cli configure set-mode --width 3840 --height 2160 --refresh-rate 60
```

Optionally control Steam auto-launch:

```bash
CouchControl.Cli configure set-steam --enabled true
```

Optionally configure audio devices:

```bash
CouchControl.Cli configure list-audio-devices
CouchControl.Cli configure set-couch-audio-device --device-id "your-tv-audio-device-id"
CouchControl.Cli configure set-desktop-audio-device --device-id "your-desktop-audio-device-id"
```

Inspect the saved configuration:

```bash
CouchControl.Cli configure show
```

### 3. Capture the desktop snapshot manually

Before the first `couch` run, save the desktop layout that `desktop` should restore later:

```bash
CouchControl.Cli snapshot capture
```

`couch` now requires an existing saved desktop snapshot and does not overwrite it automatically.

### 4. Validate couch mode once from the CLI

Run a dry run before the first real switch on a machine or TV setup:

```bash
CouchControl.Cli couch --dry-run
```

Then perform the real switch:

```bash
CouchControl.Cli couch
```

Restore desktop mode on demand:

```bash
CouchControl.Cli desktop
```

### 5. Run the tray agent for day-to-day use

After the initial configuration is in place, start `CouchControl.Agent.exe` in the logged-in user's Windows session. The tray menu provides:

- `Activate Couch Mode`
- `Restore Desktop Mode`
- `Status`
- `Settings`
- `Show Configuration Folder`
- `View Logs`
- `Start with Windows`

### 6. Package the Windows installer

Create the Windows x64 MVP package from the repository root:

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
printf '1.1.0\n' > artifacts/win-x64/CouchControl/VERSION
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

Use this sequence for the first live test on the target PC:

#### 1. Build the solution

From the solution root:

```bash
dotnet build CouchControl.sln
```

The runnable binaries will be produced under:

```text
src\CouchControl.Cli\bin\Debug\net10.0\
src\CouchControl.Agent\bin\Debug\net10.0-windows\
```

On Windows, the CLI and tray agent executables are:

```powershell
.\src\CouchControl.Cli\bin\Debug\net10.0\CouchControl.Cli.exe
.\src\CouchControl.Agent\bin\Debug\net10.0-windows\CouchControl.Agent.exe
```

#### 2. Confirm the TV is visible to Windows

List all connected displays:

```powershell
.\src\CouchControl.Cli\bin\Debug\net10.0\CouchControl.Cli.exe configure list-displays
```

Confirm that:

- The TV appears in the list.
- The friendly name matches the TV you expect.
- The device path and stable ID stay consistent across repeated runs.

#### 3. Save the TV as the couch target

Use the stable ID from `configure list-displays`:

```powershell
.\src\CouchControl.Cli\bin\Debug\net10.0\CouchControl.Cli.exe configure set-tv --display-id "f52d3a7e"
```

#### 4. Set the preferred couch mode

Example for a 4K 60 Hz TV:

```powershell
.\src\CouchControl.Cli\bin\Debug\net10.0\CouchControl.Cli.exe configure set-mode --width 3840 --height 2160 --refresh-rate 60
```

#### 5. Inspect the stored configuration

Before switching displays, verify the saved target and preferred mode:

```powershell
.\src\CouchControl.Cli\bin\Debug\net10.0\CouchControl.Cli.exe configure show
```

#### 6. Capture the desktop snapshot manually

Save the desktop layout that `desktop` should restore later:

```powershell
.\src\CouchControl.Cli\bin\Debug\net10.0\CouchControl.Cli.exe snapshot capture
```

Important:

- `couch` requires this saved snapshot to exist.
- `couch` does not overwrite the saved desktop snapshot.
- If you want a different desktop baseline later, run `snapshot capture` again yourself.

#### 7. Run a dry run first

This validates the configured TV match and the native display mode selection without applying `SetDisplayConfig`:

```powershell
.\src\CouchControl.Cli\bin\Debug\net10.0\CouchControl.Cli.exe couch --dry-run
```

Use the dry run before every first attempt on a new TV, GPU driver version, cable path, or dock/receiver configuration.

#### 8. Run the real switch

Once dry run succeeds:

```powershell
.\src\CouchControl.Cli\bin\Debug\net10.0\CouchControl.Cli.exe couch
```

Expected behavior:

- The saved manual desktop snapshot remains unchanged.
- The current topology is captured only as a temporary rollback snapshot for the active `couch` run.
- Windows is switched to TV-only mode using native `SetDisplayConfig`.
- The configured resolution and refresh rate are applied when a safe supported mode exists.
- The topology is re-queried and verified after the switch.
- If switching or verification fails, CouchControl attempts to restore the temporary rollback snapshot captured for that run.

#### 9. Verify the result live

After the command returns:

- The TV should be the only active display.
- The desktop should be on the TV.
- Other monitors should be inactive.
- If Steam auto-launch is enabled, Big Picture should start after the switch succeeds.

You can also re-run display enumeration:

```powershell
.\src\CouchControl.Cli\bin\Debug\net10.0\CouchControl.Cli.exe displays
```

#### 10. Check the saved desktop snapshot

The manually saved desktop snapshot can be inspected with:

```powershell
.\src\CouchControl.Cli\bin\Debug\net10.0\CouchControl.Cli.exe snapshot show
```

This is useful when diagnosing why later `desktop` restore logic did or did not match the intended baseline.

#### 11. Start the tray agent

Once the CLI configuration is correct, start the tray agent for normal usage:

```powershell
.\src\CouchControl.Agent\bin\Debug\net10.0-windows\CouchControl.Agent.exe
```

Expected behavior:

- A notification-area icon appears for `Couch Control`.
- Closing the status or settings window does not terminate the agent.
- Starting a second instance reuses the existing tray process instead of creating another icon or host.
- `Start with Windows` registers the agent for the current user without requiring elevation.

### Current Limitations For Live Testing

- TV selection and preferred display mode are still configured through the CLI.
- Couch and desktop audio devices can be selected from the tray settings window or through the CLI.
- `desktop` restores only the snapshot you saved manually with `snapshot capture`; Couch mode no longer refreshes that baseline automatically.
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


### Configuration Management

CouchControl supports persistent configuration storage in `%LocalAppData%\CouchControl\config.json`. The following commands are available:

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

The desktop restore baseline is managed manually.

#### 1. Capture Desktop Snapshot
Saves the current desktop topology as the baseline that `desktop` should restore later:
```bash
CouchControl.Cli snapshot capture
```

#### 2. Show Saved Desktop Snapshot
Displays the currently saved desktop baseline:
```bash
CouchControl.Cli snapshot show
```
