using CouchControl.Core.Models;
using CouchControl.Windows;
using CouchControl.Windows.Displays.Interop;
using Microsoft.Extensions.Logging.Abstractions;

namespace CouchControl.Core.Tests;

public sealed class WindowsDisplayManagerEnumerationTests
{
    [Fact]
    public async Task GetDisplaysAsync_CollapsesDuplicateWindowsPathsIntoRealDisplays()
    {
        var luid = new LUID { HighPart = 1, LowPart = 1 };
        var gsPath = @"\\?\DISPLAY#GBT3406#5&371a1502&0&UID33024#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";
        var samsungPath = @"\\?\DISPLAY#SAM735A#5&371a1502&0&UID33029#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";
        var unknownPath = @"\\?\DISPLAY#UNKNOWN#79281_33025";

        var query = BuildQueryResult(
            CreateDisplay(luid, 1, 33024, gsPath, "GS34WQC", isActive: true, width: 3440, height: 1440, posX: 0, posY: 0),
            CreateDisplay(luid, 2, 33024, gsPath, "GS34WQC", isActive: false),
            CreateDisplay(luid, 3, 33024, gsPath, "GS34WQC", isActive: false),
            CreateDisplay(luid, 4, 33024, gsPath, "GS34WQC", isActive: false),
            CreateDisplay(luid, 5, 33029, samsungPath, "SAMSUNG", isActive: false),
            CreateDisplay(luid, 6, 33029, samsungPath, "SAMSUNG", isActive: false),
            CreateDisplay(luid, 7, 33025, unknownPath, "Generic Monitor", isActive: false),
            CreateDisplay(luid, 8, 33025, unknownPath, "Generic Monitor", isActive: false));

        var system = new FakeWindowsDisplaySystem(query);
        var manager = new WindowsDisplayManager(system, NullLogger<WindowsDisplayManager>.Instance, skipPlatformCheck: true);

        var displays = await manager.GetDisplaysAsync();

        Assert.Collection(
            displays.OrderBy(display => display.FriendlyName),
            display =>
            {
                Assert.Equal("GS34WQC", display.FriendlyName);
                Assert.True(display.IsActive);
                Assert.True(display.IsPrimary);
            },
            display =>
            {
                Assert.Equal("SAMSUNG", display.FriendlyName);
                Assert.False(display.IsActive);
            });
    }

    private static QueryResult BuildQueryResult(params DisplayDefinition[] definitions)
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
                    outputTechnology = DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_HDMI,
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

        return new QueryResult(
            paths,
            modes,
            definitions
                .GroupBy(static definition => (definition.AdapterId.LowPart, definition.TargetId))
                .ToDictionary(
                    static group => group.Key,
                    static group =>
                    {
                        var definition = group.First();
                        return (definition.FriendlyName, definition.DevicePath);
                    }));
    }

    private static DisplayDefinition CreateDisplay(
        LUID adapterId,
        uint sourceId,
        uint targetId,
        string devicePath,
        string friendlyName,
        bool isActive,
        uint width = 1920,
        uint height = 1080,
        int posX = 0,
        int posY = 0) =>
        new(adapterId, sourceId, targetId, devicePath, friendlyName, isActive, width, height, posX, posY);

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
        int PositionY);

    private sealed record QueryResult(
        DISPLAYCONFIG_PATH_INFO[] Paths,
        DISPLAYCONFIG_MODE_INFO[] Modes,
        IReadOnlyDictionary<(uint AdapterLowPart, uint TargetId), (string FriendlyName, string DevicePath)> TargetDetails);

    private sealed class FakeWindowsDisplaySystem(QueryResult queryResult) : WindowsDisplayManager.IWindowsDisplaySystem
    {
        public int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements)
        {
            numPathArrayElements = (uint)queryResult.Paths.Length;
            numModeInfoArrayElements = (uint)queryResult.Modes.Length;
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
            Array.Copy(queryResult.Paths, pathArray, queryResult.Paths.Length);
            Array.Copy(queryResult.Modes, modeInfoArray, queryResult.Modes.Length);
            numPathArrayElements = (uint)queryResult.Paths.Length;
            numModeInfoArrayElements = (uint)queryResult.Modes.Length;
            return 0;
        }

        public int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_TARGET_DEVICE_NAME requestPacket)
        {
            if (queryResult.TargetDetails.TryGetValue((requestPacket.header.adapterId.LowPart, requestPacket.header.id), out var value))
            {
                requestPacket.monitorFriendlyDeviceName = value.FriendlyName;
                requestPacket.monitorDevicePath = value.DevicePath;
                return 0;
            }

            return 1;
        }

        public int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_SOURCE_DEVICE_NAME requestPacket)
        {
            requestPacket.viewGdiDeviceName = "DISPLAY1";
            return 0;
        }

        public int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_TARGET_PREFERRED_MODE requestPacket) => 1;

        public int SetDisplayConfig(
            uint numPathArrayElements,
            DISPLAYCONFIG_PATH_INFO[] pathArray,
            uint numModeInfoArrayElements,
            DISPLAYCONFIG_MODE_INFO[] modeInfoArray,
            uint flags) =>
            0;

        public bool EnumDisplaySettingsEx(string lpszDeviceName, uint iModeNum, ref DEVMODE lpDevMode, uint dwFlags) =>
            false;

        public int ChangeDisplaySettingsEx(string lpszDeviceName, DEVMODE lpDevMode, uint dwFlags) =>
            NativeMethods.DISP_CHANGE_SUCCESSFUL;

        public int CommitDisplaySettings() =>
            NativeMethods.DISP_CHANGE_SUCCESSFUL;

        public Task<int> RunDisplaySwitchExtendAsync(CancellationToken cancellationToken) =>
            Task.FromResult(0);
    }
}
