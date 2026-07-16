using CouchControl.Core.Models;
using CouchControl.Windows;
using CouchControl.Windows.Displays.Interop;
using Microsoft.Extensions.Logging.Abstractions;

namespace CouchControl.Core.Tests;

public sealed class WindowsDisplayManagerRestoreTests
{
    [Fact]
    public async Task ActivateOnlyAsync_UsesExplicitDeviceSettingsActivation()
    {
        var adapterId = new LUID { HighPart = 1, LowPart = 1 };
        var ultrawidePath = @"\\?\DISPLAY#GBT3406#5&371a1502&0&UID33024#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";
        var tvPath = @"\\?\DISPLAY#SAM735A#5&371a1502&0&UID33029#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";

        var currentTopology = QueryState.Create(
            CreateDisplay(adapterId, 0, 33024, ultrawidePath, "GS34WQC", isActive: true, width: 3440, height: 1440, positionX: 0, positionY: 0, outputTechnology: DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EXTERNAL),
            CreateDisplay(adapterId, 1, 33029, tvPath, "SAMSUNG", isActive: false, width: 3840, height: 2160, positionX: 3440, positionY: 0, outputTechnology: DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_HDMI));

        var activatedTopology = QueryState.Create(
            CreateDisplay(adapterId, 1, 33029, tvPath, "SAMSUNG", isActive: true, width: 1920, height: 1080, positionX: 0, positionY: 0, outputTechnology: DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_HDMI));

        var displaySystem = new FakeWindowsDisplaySystem(
            currentTopology,
            activatedTopology,
            supportedModes: new Dictionary<string, IReadOnlyList<DisplayMode>>(StringComparer.OrdinalIgnoreCase)
            {
                ["DISPLAY1"] = [new DisplayMode(3440, 1440, 100)],
                ["DISPLAY2"] = [new DisplayMode(1920, 1080, 60), new DisplayMode(3840, 2160, 60)]
            },
            sourceNames: new Dictionary<(uint AdapterLowPart, uint SourceId), string>
            {
                [(adapterId.LowPart, 0)] = "DISPLAY1",
                [(adapterId.LowPart, 1)] = "DISPLAY2"
            });

        var manager = new WindowsDisplayManager(displaySystem, NullLogger<WindowsDisplayManager>.Instance, skipPlatformCheck: true);

        var result = await manager.ActivateOnlyAsync(new DisplayIdentifier(tvPath), new DisplayMode(1920, 1080, 60));

        Assert.True(result.Succeeded);
        Assert.Equal("single_display_device_settings", result.Outcome);
        Assert.Contains("Attempting explicit single-display activation", result.Details);
        Assert.Contains("Detaching GS34WQC", result.Details);
        Assert.Contains("Configuring SAMSUNG as primary display", result.Details);
        Assert.Empty(displaySystem.SetDisplayConfigCalls);
        Assert.Equal(2, displaySystem.ChangeDisplaySettingsExCalls.Count);
        Assert.Equal("DISPLAY1", displaySystem.ChangeDisplaySettingsExCalls[0].DeviceName);
        Assert.Equal("DISPLAY2", displaySystem.ChangeDisplaySettingsExCalls[1].DeviceName);
        Assert.Equal((uint)1920, displaySystem.ChangeDisplaySettingsExCalls[1].Width);
        Assert.Equal((uint)1080, displaySystem.ChangeDisplaySettingsExCalls[1].Height);
        Assert.Equal(1, displaySystem.CommitDisplaySettingsCallCount);
    }

    [Fact]
    public async Task ActivateOnlyAsync_DryRun_DoesNotCommitDeviceSettings()
    {
        var adapterId = new LUID { HighPart = 1, LowPart = 1 };
        var ultrawidePath = @"\\?\DISPLAY#GBT3406#5&371a1502&0&UID33024#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";
        var tvPath = @"\\?\DISPLAY#SAM735A#5&371a1502&0&UID33029#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";

        var currentTopology = QueryState.Create(
            CreateDisplay(adapterId, 0, 33024, ultrawidePath, "GS34WQC", isActive: true, width: 3440, height: 1440, positionX: 0, positionY: 0, outputTechnology: DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EXTERNAL),
            CreateDisplay(adapterId, 1, 33029, tvPath, "SAMSUNG", isActive: false, width: 3840, height: 2160, positionX: 3440, positionY: 0, outputTechnology: DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_HDMI));

        var displaySystem = new FakeWindowsDisplaySystem(
            currentTopology,
            currentTopology,
            supportedModes: new Dictionary<string, IReadOnlyList<DisplayMode>>(StringComparer.OrdinalIgnoreCase)
            {
                ["DISPLAY1"] = [new DisplayMode(3440, 1440, 100)],
                ["DISPLAY2"] = [new DisplayMode(1920, 1080, 60)]
            },
            sourceNames: new Dictionary<(uint AdapterLowPart, uint SourceId), string>
            {
                [(adapterId.LowPart, 0)] = "DISPLAY1",
                [(adapterId.LowPart, 1)] = "DISPLAY2"
            });

        var manager = new WindowsDisplayManager(displaySystem, NullLogger<WindowsDisplayManager>.Instance, skipPlatformCheck: true);

        var result = await manager.ActivateOnlyAsync(new DisplayIdentifier(tvPath), new DisplayMode(1920, 1080, 60), dryRun: true);

        Assert.True(result.Succeeded);
        Assert.Equal("single_display_device_settings", result.Outcome);
        Assert.Contains("Dry run planned explicit single-display restore", result.Details);
        Assert.Equal(2, displaySystem.ChangeDisplaySettingsExCalls.Count);
        Assert.Equal(0, displaySystem.CommitDisplaySettingsCallCount);
    }

    [Fact]
    public async Task ActivateOnlyAsync_RetriesAfterExtendFallbackWhenVerificationFails()
    {
        var adapterId = new LUID { HighPart = 1, LowPart = 1 };
        var ultrawidePath = @"\\?\DISPLAY#GBT3406#5&371a1502&0&UID33024#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";
        var tvPath = @"\\?\DISPLAY#SAM735A#5&371a1502&0&UID33029#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";

        var currentTopology = QueryState.Create(
            CreateDisplay(adapterId, 0, 33024, ultrawidePath, "GS34WQC", isActive: true, width: 3440, height: 1440, positionX: 0, positionY: 0, outputTechnology: DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EXTERNAL),
            CreateDisplay(adapterId, 1, 33029, tvPath, "SAMSUNG", isActive: false, width: 3840, height: 2160, positionX: 3440, positionY: 0, outputTechnology: DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_HDMI));

        var failedActivatedTopology = QueryState.Create(
            CreateDisplay(adapterId, 1, 33029, tvPath, "SAMSUNG", isActive: false, width: 1920, height: 1080, positionX: 0, positionY: 0, outputTechnology: DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_HDMI));

        var extendedTopology = QueryState.Create(
            CreateDisplay(adapterId, 0, 33024, ultrawidePath, "GS34WQC", isActive: true, width: 3440, height: 1440, positionX: 0, positionY: 0, outputTechnology: DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EXTERNAL),
            CreateDisplay(adapterId, 1, 33029, tvPath, "SAMSUNG", isActive: true, width: 3840, height: 2160, positionX: 3440, positionY: 0, outputTechnology: DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_HDMI));

        var finalActivatedTopology = QueryState.Create(
            CreateDisplay(adapterId, 1, 33029, tvPath, "SAMSUNG", isActive: true, width: 1920, height: 1080, positionX: 0, positionY: 0, outputTechnology: DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_HDMI));

        var displaySystem = new FakeWindowsDisplaySystem(
            currentTopology,
            failedActivatedTopology,
            supportedModes: new Dictionary<string, IReadOnlyList<DisplayMode>>(StringComparer.OrdinalIgnoreCase)
            {
                ["DISPLAY1"] = [new DisplayMode(3440, 1440, 100)],
                ["DISPLAY2"] = [new DisplayMode(1920, 1080, 60), new DisplayMode(3840, 2160, 60)]
            },
            sourceNames: new Dictionary<(uint AdapterLowPart, uint SourceId), string>
            {
                [(adapterId.LowPart, 0)] = "DISPLAY1",
                [(adapterId.LowPart, 1)] = "DISPLAY2"
            })
        {
            ExtendedState = extendedTopology,
            CommitStates = new Queue<QueryState>(new[] { failedActivatedTopology, finalActivatedTopology })
        };

        var manager = new WindowsDisplayManager(displaySystem, NullLogger<WindowsDisplayManager>.Instance, skipPlatformCheck: true);

        var result = await manager.ActivateOnlyAsync(new DisplayIdentifier(tvPath), new DisplayMode(1920, 1080, 60));

        Assert.True(result.Succeeded);
        Assert.Equal("single_display_device_settings_after_extend_fallback", result.Outcome);
        Assert.Contains("Using activation fallback: DisplaySwitch.exe /extend", result.Details);
        Assert.Contains("Attempting explicit single-display activation after extend fallback", result.Details);
        Assert.Equal(1, displaySystem.DisplaySwitchExtendCallCount);
        Assert.Equal(4, displaySystem.ChangeDisplaySettingsExCalls.Count);
        Assert.Equal(2, displaySystem.CommitDisplaySettingsCallCount);
    }

    [Fact]
    public async Task RestoreSnapshotAsync_SingleDisplaySnapshot_UsesExplicitDeviceSettingsRestore()
    {
        var adapterId = new LUID { HighPart = 1, LowPart = 1 };
        var ultrawidePath = @"\\?\DISPLAY#GBT3406#5&371a1502&0&UID33024#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";
        var tvPath = @"\\?\DISPLAY#SAM735A#5&371a1502&0&UID33029#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";

        var currentTopology = QueryState.Create(
            CreateDisplay(adapterId, 0, 33024, ultrawidePath, "GS34WQC", isActive: false, width: 3440, height: 1440, positionX: 0, positionY: 0, outputTechnology: DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EXTERNAL),
            CreateDisplay(adapterId, 1, 33029, tvPath, "SAMSUNG", isActive: true, width: 3840, height: 2160, positionX: 0, positionY: 0, outputTechnology: DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_HDMI));

        var restoredTopology = QueryState.Create(
            CreateDisplay(adapterId, 0, 33024, ultrawidePath, "GS34WQC", isActive: true, width: 3440, height: 1440, positionX: 0, positionY: 0, outputTechnology: DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EXTERNAL));

        var displaySystem = new FakeWindowsDisplaySystem(
            currentTopology,
            restoredTopology,
            supportedModes: new Dictionary<string, IReadOnlyList<DisplayMode>>(StringComparer.OrdinalIgnoreCase)
            {
                ["DISPLAY1"] = [new DisplayMode(3440, 1440, 100)],
                ["DISPLAY2"] = [new DisplayMode(3840, 2160, 60)]
            },
            sourceNames: new Dictionary<(uint AdapterLowPart, uint SourceId), string>
            {
                [(adapterId.LowPart, 0)] = "DISPLAY1",
                [(adapterId.LowPart, 1)] = "DISPLAY2"
            });

        var manager = new WindowsDisplayManager(displaySystem, NullLogger<WindowsDisplayManager>.Instance, skipPlatformCheck: true);
        var snapshot = CreateSnapshot(ultrawidePath, "GS34WQC", adapterId.ToString(), 0, 33024);

        var result = await manager.RestoreSnapshotAsync(snapshot);

        Assert.True(result.Succeeded);
        Assert.Equal("single_display_device_settings", result.Outcome);
        Assert.Contains("Attempting explicit single-display restore", result.Details);
        Assert.Contains("Detaching SAMSUNG", result.Details);
        Assert.Contains("Configuring GS34WQC as primary display", result.Details);
        Assert.Empty(displaySystem.SetDisplayConfigCalls);
        Assert.Equal(2, displaySystem.ChangeDisplaySettingsExCalls.Count);
        Assert.Equal("DISPLAY2", displaySystem.ChangeDisplaySettingsExCalls[0].DeviceName);
        Assert.Equal("DISPLAY1", displaySystem.ChangeDisplaySettingsExCalls[1].DeviceName);
        Assert.Equal(1, displaySystem.CommitDisplaySettingsCallCount);
        Assert.Equal(0, displaySystem.DisplaySwitchExtendCallCount);
    }

    [Fact]
    public async Task RestoreSnapshotAsync_SingleDisplaySnapshot_UsesExplicitRecoveryAfterExtendFallback()
    {
        var adapterId = new LUID { HighPart = 1, LowPart = 1 };
        var ultrawidePath = @"\\?\DISPLAY#GBT3406#5&371a1502&0&UID33024#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";
        var tvPath = @"\\?\DISPLAY#SAM735A#5&371a1502&0&UID33029#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";

        var initialTvOnlyState = QueryState.Create(
            CreateDisplay(adapterId, 0, 33024, ultrawidePath, "GS34WQC", isActive: false, width: 3440, height: 1440, positionX: 0, positionY: 0, outputTechnology: DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EXTERNAL),
            CreateDisplay(adapterId, 1, 33029, tvPath, "SAMSUNG", isActive: true, width: 3840, height: 2160, positionX: 0, positionY: 0, outputTechnology: DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_HDMI));
        var extendedState = QueryState.Create(
            CreateDisplay(adapterId, 0, 33024, ultrawidePath, "GS34WQC", isActive: true, width: 3440, height: 1440, positionX: 0, positionY: 0, outputTechnology: DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EXTERNAL),
            CreateDisplay(adapterId, 1, 33029, tvPath, "SAMSUNG", isActive: true, width: 3840, height: 2160, positionX: 3440, positionY: 0, outputTechnology: DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_HDMI));
        var restoredState = QueryState.Create(
            CreateDisplay(adapterId, 0, 33024, ultrawidePath, "GS34WQC", isActive: true, width: 3440, height: 1440, positionX: 0, positionY: 0, outputTechnology: DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EXTERNAL));

        var displaySystem = new FakeWindowsDisplaySystem(
            initialTvOnlyState,
            restoredState,
            supportedModes: new Dictionary<string, IReadOnlyList<DisplayMode>>(StringComparer.OrdinalIgnoreCase)
            {
                ["DISPLAY1"] = [new DisplayMode(3440, 1440, 100)],
                ["DISPLAY2"] = [new DisplayMode(3840, 2160, 60)]
            },
            sourceNames: new Dictionary<(uint AdapterLowPart, uint SourceId), string>
            {
                [(adapterId.LowPart, 0)] = "DISPLAY1",
                [(adapterId.LowPart, 1)] = "DISPLAY2"
            })
        {
            ExtendedState = extendedState,
            ChangeDisplaySettingsResults = new Queue<int>([-1, 0, 0])
        };

        var manager = new WindowsDisplayManager(displaySystem, NullLogger<WindowsDisplayManager>.Instance, skipPlatformCheck: true);
        var snapshot = CreateSnapshot(ultrawidePath, "GS34WQC", adapterId.ToString(), 0, 33024);

        var result = await manager.RestoreSnapshotAsync(snapshot);

        Assert.True(result.Succeeded);
        Assert.Equal("single_display_device_settings", result.Outcome);
        Assert.Contains("Attempting explicit single-display restoration after emergency fallback", result.Details);
        Assert.Equal(1, displaySystem.DisplaySwitchExtendCallCount);
        Assert.Empty(displaySystem.SetDisplayConfigCalls);
        Assert.Equal(3, displaySystem.ChangeDisplaySettingsExCalls.Count);
        Assert.Equal(1, displaySystem.CommitDisplaySettingsCallCount);
    }

    private static DisplaySnapshot CreateSnapshot(
        string devicePath,
        string friendlyName,
        string adapterLuid,
        uint sourceId,
        uint targetId) =>
        new(
            "snapshot-1",
            DateTimeOffset.UtcNow,
            [
                new DisplayDevice(
                    new DisplayIdentifier(devicePath),
                    friendlyName,
                    true,
                    true,
                    new DisplayMode(3440, 1440, 100),
                    devicePath,
                    adapterLuid,
                    sourceId,
                    targetId,
                    "DisplayPort")
            ],
            [
                new DisplayPathSnapshot(
                    new DisplayIdentifier(devicePath),
                    adapterLuid,
                    sourceId,
                    targetId,
                    true,
                    true,
                    new DisplayPoint(0, 0),
                    3440,
                    1440,
                    "32Bpp",
                    new DisplayRefreshRate(100, 1),
                    "Identity",
                    "Identity",
                    "DisplayPort",
                    new DisplaySourceModeSnapshot(3440, 1440, "32Bpp", new DisplayPoint(0, 0)),
                    new DisplayTargetModeSnapshot(new DisplayRefreshRate(100, 1), 3440, 1440, "Progressive"))
            ]);

    private static DisplayDefinition CreateDisplay(
        LUID adapterId,
        uint sourceId,
        uint targetId,
        string devicePath,
        string friendlyName,
        bool isActive,
        uint width,
        uint height,
        int positionX,
        int positionY,
        DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY outputTechnology) =>
        new(adapterId, sourceId, targetId, devicePath, friendlyName, isActive, width, height, positionX, positionY, outputTechnology);

    private sealed record DisplayDefinition(
        LUID AdapterId,
        uint SourceId,
        uint TargetId,
        string DevicePath,
        string FriendlyName,
        bool IsActive,
        uint Width,
        uint Height,
        int PositionX,
        int PositionY,
        DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY OutputTechnology);

    private sealed record QueryState(
        DISPLAYCONFIG_PATH_INFO[] Paths,
        DISPLAYCONFIG_MODE_INFO[] Modes,
        IReadOnlyDictionary<(uint AdapterLowPart, uint TargetId), (string FriendlyName, string DevicePath)> TargetDetails)
    {
        public static QueryState Create(params DisplayDefinition[] definitions)
        {
            var paths = new DISPLAYCONFIG_PATH_INFO[definitions.Length];
            var modes = new DISPLAYCONFIG_MODE_INFO[definitions.Length * 2];

            for (var i = 0; i < definitions.Length; i++)
            {
                var definition = definitions[i];
                int sourceIndex = i * 2;
                int targetIndex = sourceIndex + 1;

                paths[i] = new DISPLAYCONFIG_PATH_INFO
                {
                    flags = definition.IsActive ? NativeMethods.DISPLAYCONFIG_PATH_ACTIVE : 0,
                    sourceInfo = new DISPLAYCONFIG_PATH_SOURCE_INFO
                    {
                        adapterId = definition.AdapterId,
                        id = definition.SourceId,
                        modeInfoIdx = (uint)sourceIndex
                    },
                    targetInfo = new DISPLAYCONFIG_PATH_TARGET_INFO
                    {
                        adapterId = definition.AdapterId,
                        id = definition.TargetId,
                        modeInfoIdx = (uint)targetIndex,
                        outputTechnology = definition.OutputTechnology,
                        rotation = DISPLAYCONFIG_ROTATION.DISPLAYCONFIG_ROTATION_IDENTITY,
                        scaling = DISPLAYCONFIG_SCALING.DISPLAYCONFIG_SCALING_IDENTITY,
                        refreshRate = new DISPLAYCONFIG_RATIONAL(100, 1),
                        scanLineOrdering = DISPLAYCONFIG_SCANLINE_ORDERING.DISPLAYCONFIG_SCANLINE_ORDERING_PROGRESSIVE,
                        targetAvailable = 1
                    }
                };

                modes[sourceIndex] = new DISPLAYCONFIG_MODE_INFO
                {
                    infoType = DISPLAYCONFIG_MODE_INFO_TYPE.DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE,
                    adapterId = definition.AdapterId,
                    id = definition.SourceId,
                    sourceMode = new DISPLAYCONFIG_SOURCE_MODE
                    {
                        width = definition.Width,
                        height = definition.Height,
                        pixelFormat = DISPLAYCONFIG_PIXELFORMAT.DISPLAYCONFIG_PIXELFORMAT_32BPP,
                        position = new POINTL { x = definition.PositionX, y = definition.PositionY }
                    }
                };

                modes[targetIndex] = new DISPLAYCONFIG_MODE_INFO
                {
                    infoType = DISPLAYCONFIG_MODE_INFO_TYPE.DISPLAYCONFIG_MODE_INFO_TYPE_TARGET,
                    adapterId = definition.AdapterId,
                    id = definition.TargetId,
                    targetMode = new DISPLAYCONFIG_TARGET_MODE
                    {
                        targetVideoSignalInfo = new DISPLAYCONFIG_VIDEO_SIGNAL_INFO
                        {
                            activeSize = new DISPLAYCONFIG_2DREGION { cx = definition.Width, cy = definition.Height },
                            totalSize = new DISPLAYCONFIG_2DREGION { cx = definition.Width, cy = definition.Height },
                            vSyncFreq = new DISPLAYCONFIG_RATIONAL(100, 1),
                            scanLineOrdering = DISPLAYCONFIG_SCANLINE_ORDERING.DISPLAYCONFIG_SCANLINE_ORDERING_PROGRESSIVE
                        }
                    }
                };
            }

            return new QueryState(
                paths,
                modes,
                definitions.ToDictionary(
                    static definition => (definition.AdapterId.LowPart, definition.TargetId),
                    static definition => (definition.FriendlyName, definition.DevicePath)));
        }
    }

    private sealed class FakeWindowsDisplaySystem(
        QueryState currentState,
        QueryState restoredState,
        IReadOnlyDictionary<string, IReadOnlyList<DisplayMode>> supportedModes,
        IReadOnlyDictionary<(uint AdapterLowPart, uint SourceId), string> sourceNames) : WindowsDisplayManager.IWindowsDisplaySystem
    {
        private QueryState currentState = currentState;

        public List<SetDisplayConfigCall> SetDisplayConfigCalls { get; } = [];

        public Queue<int> ValidateResults { get; init; } = [];

        public Queue<int> ApplyResults { get; init; } = [];

        public Queue<int> ChangeDisplaySettingsResults { get; init; } = [];

        public int DisplaySwitchExtendCallCount { get; private set; }

        public int CommitDisplaySettingsCallCount { get; private set; }

        public QueryState? ExtendedState { get; init; }

        public Queue<QueryState>? CommitStates { get; init; }

        public List<ChangeDisplaySettingsExCall> ChangeDisplaySettingsExCalls { get; } = [];

        public int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements)
        {
            numPathArrayElements = (uint)currentState.Paths.Length;
            numModeInfoArrayElements = (uint)currentState.Modes.Length;
            return 0;
        }

        public int QueryDisplayConfig(
            uint flags,
            ref uint numPathArrayElements,
            DISPLAYCONFIG_PATH_INFO[] pathArray,
            ref uint numModeInfoArrayElements,
            DISPLAYCONFIG_MODE_INFO[] modeInfoArray,
            IntPtr currentTopologyId)
        {
            Array.Copy(currentState.Paths, pathArray, currentState.Paths.Length);
            Array.Copy(currentState.Modes, modeInfoArray, currentState.Modes.Length);
            numPathArrayElements = (uint)currentState.Paths.Length;
            numModeInfoArrayElements = (uint)currentState.Modes.Length;
            return 0;
        }

        public int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_TARGET_DEVICE_NAME requestPacket)
        {
            if (currentState.TargetDetails.TryGetValue((requestPacket.header.adapterId.LowPart, requestPacket.header.id), out var value))
            {
                requestPacket.monitorFriendlyDeviceName = value.FriendlyName;
                requestPacket.monitorDevicePath = value.DevicePath;
                return 0;
            }

            return 1;
        }

        public int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_SOURCE_DEVICE_NAME requestPacket)
        {
            if (sourceNames.TryGetValue((requestPacket.header.adapterId.LowPart, requestPacket.header.id), out var sourceName))
            {
                requestPacket.viewGdiDeviceName = sourceName;
                return 0;
            }

            return 1;
        }

        public int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_TARGET_PREFERRED_MODE requestPacket) => 1;

        public int SetDisplayConfig(
            uint numPathArrayElements,
            DISPLAYCONFIG_PATH_INFO[] pathArray,
            uint numModeInfoArrayElements,
            DISPLAYCONFIG_MODE_INFO[] modeInfoArray,
            uint flags)
        {
            SetDisplayConfigCalls.Add(new SetDisplayConfigCall(numPathArrayElements, numModeInfoArrayElements, flags));

            if ((flags & NativeMethods.SDC_VALIDATE) != 0)
            {
                return ValidateResults.Count > 0 ? ValidateResults.Dequeue() : 0;
            }

            if ((flags & NativeMethods.SDC_APPLY) != 0)
            {
                var result = ApplyResults.Count > 0 ? ApplyResults.Dequeue() : 0;
                if (result != 0)
                {
                    return result;
                }

                currentState = restoredState;
            }

            return 0;
        }

        public bool EnumDisplaySettingsEx(string lpszDeviceName, uint iModeNum, ref DEVMODE lpDevMode, uint dwFlags)
        {
            if (!supportedModes.TryGetValue(lpszDeviceName, out var modes))
            {
                return false;
            }

            if (iModeNum == NativeMethods.ENUM_CURRENT_SETTINGS)
            {
                var current = modes[0];
                lpDevMode.dmPelsWidth = (uint)current.Width;
                lpDevMode.dmPelsHeight = (uint)current.Height;
                lpDevMode.dmDisplayFrequency = (uint)current.RefreshRateHz;
                return true;
            }

            if (iModeNum >= modes.Count)
            {
                return false;
            }

            var mode = modes[(int)iModeNum];
            lpDevMode.dmPelsWidth = (uint)mode.Width;
            lpDevMode.dmPelsHeight = (uint)mode.Height;
            lpDevMode.dmDisplayFrequency = (uint)mode.RefreshRateHz;
            return true;
        }

        public int ChangeDisplaySettingsEx(string lpszDeviceName, DEVMODE lpDevMode, uint dwFlags)
        {
            ChangeDisplaySettingsExCalls.Add(new ChangeDisplaySettingsExCall(lpszDeviceName, lpDevMode.dmPelsWidth, lpDevMode.dmPelsHeight, lpDevMode.dmPositionX, lpDevMode.dmPositionY, dwFlags));
            return ChangeDisplaySettingsResults.Count > 0
                ? ChangeDisplaySettingsResults.Dequeue()
                : NativeMethods.DISP_CHANGE_SUCCESSFUL;
        }

        public int CommitDisplaySettings()
        {
            CommitDisplaySettingsCallCount++;
            currentState = CommitStates is { Count: > 0 }
                ? CommitStates.Dequeue()
                : restoredState;
            return NativeMethods.DISP_CHANGE_SUCCESSFUL;
        }

        public Task<int> RunDisplaySwitchExtendAsync(CancellationToken cancellationToken)
        {
            DisplaySwitchExtendCallCount++;
            if (ExtendedState is not null)
            {
                currentState = ExtendedState;
            }

            return Task.FromResult(0);
        }
    }

    private sealed record SetDisplayConfigCall(
        uint NumPathArrayElements,
        uint NumModeInfoArrayElements,
        uint Flags);

    private sealed record ChangeDisplaySettingsExCall(
        string DeviceName,
        uint Width,
        uint Height,
        int PositionX,
        int PositionY,
        uint Flags);
}
