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

### 6. Publish Windows executables

Both publish scripts now generate separate CLI and tray-agent outputs:

- `artifacts/publish/cli-win-x64-fdd`
- `artifacts/publish/cli-win-x64-sc`
- `artifacts/publish/agent-win-x64-fdd`
- `artifacts/publish/agent-win-x64-sc`

Use either script from the repository root:

```powershell
./publish-windows.ps1
```

```bash
./publish-windows.sh
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

- TV selection and preferred display mode are still configured through the CLI; the tray settings window currently exposes status, Steam auto-launch, and start-with-Windows behavior rather than full display selection.
- `desktop` restores only the snapshot you saved manually with `snapshot capture`; Couch mode no longer refreshes that baseline automatically.
- Rollback is attempted automatically only when the switch or post-switch verification fails during the `couch` operation.
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

#### 2. Set Couch Display (TV)
Configures the couch display by passing its 8-character stable short ID or full device path. It validates that the display exists, parses its hardware manufacturer/product identity, and saves it:
```bash
CouchControl.Cli configure set-tv --display-id "f52d3a7e"
```

#### 3. Set Preferred Couch Display Mode
Configures the preferred resolution and refresh rate for couch mode:
```bash
CouchControl.Cli configure set-mode --width 3840 --height 2160 --refresh-rate 60
```

#### 4. Configure Steam Auto-Launch
Configures whether Steam Big Picture should launch automatically upon entering couch mode:
```bash
CouchControl.Cli configure set-steam --enabled true
```

#### 5. Show Current Configuration
Prints the current configuration state:
```bash
CouchControl.Cli configure show
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
