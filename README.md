# CouchControl

CouchControl is a Windows-focused companion agent for a gaming PC that can switch between a normal desktop monitor layout and a couch gaming setup. In desktop mode, the ultrawide monitor configuration remains active. In couch mode, the living-room TV becomes the only active display and Steam launches directly into Big Picture mode.

## Warning

The current couch-mode stage changes the active Windows display topology. Running `CouchControl.Cli couch` can disable your other monitors, move your primary desktop to the configured TV, and temporarily interrupt the local desktop session while Windows applies the new topology and mode.

## Architecture

The solution is split into four projects:

- `src/CouchControl.Core`: platform-agnostic domain models, orchestration, result types, and interfaces.
- `src/CouchControl.Windows`: future Windows-specific implementations for display switching, Steam launching, and persistence.
- `src/CouchControl.Cli`: the console host that wires dependency injection and logging together.
- `tests/CouchControl.Core.Tests`: xUnit tests for the core domain and result semantics.

The current foundation keeps the orchestration logic in `CouchControl.Core` so the rules around mode activation stay independent from the Windows APIs that will eventually execute those decisions.

## Why This Runs In The Interactive Session

This agent is intended to manipulate display topology and launch a user-facing application. Those operations belong in the interactive Windows session, not in a traditional Session 0 Windows service.

Running in Session 0 would create the wrong execution model for this project:

- Session 0 services do not have the user desktop context needed for reliable display changes.
- Steam Big Picture is an interactive UI application and should launch in the signed-in user session.
- Modern Windows isolates services from the desktop, which makes user-session display state and shell interaction significantly harder and less reliable.

For that reason, CouchControl is planned as a user-session companion agent rather than a background service detached from the active desktop.

## Planned Stages

1. Foundation: solution structure, domain models, interfaces, orchestration boundaries, and tests.
2. Windows display discovery: enumerate displays, capture snapshots, and model display paths accurately.
3. Windows mode switching: activate the configured couch display as the only active output and restore the prior desktop snapshot.
4. Steam integration: detect installation state, launch Big Picture mode, and handle already-running Steam cases.
5. Persistence and operations: store configuration and snapshots, improve CLI commands, and add diagnostics and logging for real deployments.

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

To validate the configured TV selection and mode resolution without calling `SetDisplayConfig`, use:

```bash
CouchControl.Cli couch --dry-run
```

Expected high-level console output:

```text
TV detected: Samsung TV
Desktop configuration saved
Switching to TV-only topology
Applying 3840x2160 at 60 Hz
Verifying display configuration
Couch display active
```

### Live Testing On Windows

Use this sequence for the first live test on the target PC:

#### 1. Build the CLI

From the solution root:

```bash
dotnet build CouchControl.sln
```

The runnable CLI will be produced under:

```text
src\CouchControl.Cli\bin\Debug\net10.0\
```

On Windows, the command you will run is:

```powershell
.\src\CouchControl.Cli\bin\Debug\net10.0\CouchControl.Cli.exe couch
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

#### 6. Run a dry run first

This validates the configured TV match and the native display mode selection without applying `SetDisplayConfig`:

```powershell
.\src\CouchControl.Cli\bin\Debug\net10.0\CouchControl.Cli.exe couch --dry-run
```

Use the dry run before every first attempt on a new TV, GPU driver version, cable path, or dock/receiver configuration.

#### 7. Run the real switch

Once dry run succeeds:

```powershell
.\src\CouchControl.Cli\bin\Debug\net10.0\CouchControl.Cli.exe couch
```

Expected behavior:

- The current desktop topology is captured and saved first.
- Windows is switched to TV-only mode using native `SetDisplayConfig`.
- The configured resolution and refresh rate are applied when a safe supported mode exists.
- The topology is re-queried and verified after the switch.
- If switching or verification fails, CouchControl attempts to restore the saved desktop snapshot.

#### 8. Verify the result live

After the command returns:

- The TV should be the only active display.
- The desktop should be on the TV.
- Other monitors should be inactive.
- If Steam auto-launch is enabled, Big Picture should start after the switch succeeds.

You can also re-run display enumeration:

```powershell
.\src\CouchControl.Cli\bin\Debug\net10.0\CouchControl.Cli.exe displays
```

#### 9. Check the saved desktop snapshot

The last captured desktop snapshot can be inspected with:

```powershell
.\src\CouchControl.Cli\bin\Debug\net10.0\CouchControl.Cli.exe snapshot show
```

This is useful when diagnosing why rollback or later desktop restore logic did or did not match the previous topology.

### Current Limitations For Live Testing

- This stage implements `couch` activation only. There is not yet a CLI command that restores desktop mode on demand.
- Rollback is attempted automatically only when the switch or post-switch verification fails during the `couch` operation.
- If the command succeeds but you want to return to your normal monitor layout, use normal Windows display settings for now.
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
