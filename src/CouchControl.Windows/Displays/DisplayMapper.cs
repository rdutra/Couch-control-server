using System;
using CouchControl.Core.Models;
using CouchControl.Windows.Displays.Interop;

namespace CouchControl.Windows.Displays;

internal static class DisplayMapper
{
    public static DisplayDevice MapToDomain(
        DISPLAYCONFIG_PATH_INFO path,
        string? friendlyName,
        string? devicePath,
        DISPLAYCONFIG_MODE_INFO? sourceMode)
    {
        string mappedFriendlyName = string.IsNullOrWhiteSpace(friendlyName)
            ? "Generic Monitor"
            : friendlyName.Trim();

        string mappedDevicePath = string.IsNullOrWhiteSpace(devicePath)
            ? $"\\\\?\\DISPLAY#UNKNOWN#{path.sourceInfo.adapterId.LowPart}_{path.targetInfo.id}"
            : devicePath.Trim();

        var identifier = new DisplayIdentifier(mappedDevicePath);

        bool isActive = (path.flags & NativeMethods.DISPLAYCONFIG_PATH_ACTIVE) != 0;
        bool isPrimary = false;
        DisplayMode? currentMode = null;

        if (isActive && sourceMode.HasValue && sourceMode.Value.infoType == DISPLAYCONFIG_MODE_INFO_TYPE.DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE)
        {
            var srcMode = sourceMode.Value.sourceMode;
            int width = (int)srcMode.width;
            int height = (int)srcMode.height;
            isPrimary = srcMode.position.x == 0 && srcMode.position.y == 0;

            decimal refreshRateHz = CalculateRefreshRate(path.targetInfo.refreshRate);
            currentMode = new DisplayMode(width, height, refreshRateHz);
        }

        string outputTech = MapOutputTechnology(path.targetInfo.outputTechnology);
        string adapterLuidStr = path.sourceInfo.adapterId.ToString();

        return new DisplayDevice(
            Identifier: identifier,
            FriendlyName: mappedFriendlyName,
            IsActive: isActive,
            IsPrimary: isPrimary,
            CurrentMode: currentMode,
            DevicePath: mappedDevicePath,
            AdapterLuid: adapterLuidStr,
            SourceId: path.sourceInfo.id,
            TargetId: path.targetInfo.id,
            OutputTechnology: outputTech
        );
    }

    public static DisplayPathSnapshot MapPathSnapshot(
        DISPLAYCONFIG_PATH_INFO path,
        string? devicePath,
        DISPLAYCONFIG_MODE_INFO? sourceMode,
        DISPLAYCONFIG_MODE_INFO? targetMode)
    {
        string mappedDevicePath = string.IsNullOrWhiteSpace(devicePath)
            ? $"\\\\?\\DISPLAY#UNKNOWN#{path.sourceInfo.adapterId.LowPart}_{path.targetInfo.id}"
            : devicePath.Trim();

        var sourceSnapshot = MapSourceMode(sourceMode);
        var targetSnapshot = MapTargetMode(targetMode);
        var refreshRate = new DisplayRefreshRate(
            path.targetInfo.refreshRate.Numerator,
            path.targetInfo.refreshRate.Denominator);

        return new DisplayPathSnapshot(
            Identifier: new DisplayIdentifier(mappedDevicePath),
            AdapterLuid: path.sourceInfo.adapterId.ToString(),
            SourceId: path.sourceInfo.id,
            TargetId: path.targetInfo.id,
            IsActive: (path.flags & NativeMethods.DISPLAYCONFIG_PATH_ACTIVE) != 0,
            IsPrimary: sourceSnapshot?.IsPrimary ?? false,
            SourceDesktopPosition: sourceSnapshot?.Position,
            Width: sourceSnapshot?.Width,
            Height: sourceSnapshot?.Height,
            PixelFormat: sourceSnapshot?.PixelFormat,
            RefreshRate: refreshRate,
            Rotation: MapRotation(path.targetInfo.rotation),
            Scaling: MapScaling(path.targetInfo.scaling),
            OutputTechnology: MapOutputTechnology(path.targetInfo.outputTechnology),
            SourceMode: sourceSnapshot,
            TargetMode: targetSnapshot);
    }

    public static DisplaySourceModeSnapshot? MapSourceMode(DISPLAYCONFIG_MODE_INFO? sourceMode)
    {
        if (!sourceMode.HasValue || sourceMode.Value.infoType != DISPLAYCONFIG_MODE_INFO_TYPE.DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE)
        {
            return null;
        }

        var mode = sourceMode.Value.sourceMode;
        return new DisplaySourceModeSnapshot(
            mode.width,
            mode.height,
            MapPixelFormat(mode.pixelFormat),
            new DisplayPoint(mode.position.x, mode.position.y));
    }

    public static DisplayTargetModeSnapshot? MapTargetMode(DISPLAYCONFIG_MODE_INFO? targetMode)
    {
        if (!targetMode.HasValue || targetMode.Value.infoType != DISPLAYCONFIG_MODE_INFO_TYPE.DISPLAYCONFIG_MODE_INFO_TYPE_TARGET)
        {
            return null;
        }

        var signalInfo = targetMode.Value.targetMode.targetVideoSignalInfo;
        return new DisplayTargetModeSnapshot(
            new DisplayRefreshRate(signalInfo.vSyncFreq.Numerator, signalInfo.vSyncFreq.Denominator),
            signalInfo.activeSize.cx,
            signalInfo.activeSize.cy,
            MapScanlineOrdering(signalInfo.scanLineOrdering));
    }

    public static decimal CalculateRefreshRate(DISPLAYCONFIG_RATIONAL refreshRate)
    {
        if (refreshRate.Denominator == 0)
        {
            return 0;
        }

        decimal rate = (decimal)refreshRate.Numerator / refreshRate.Denominator;
        return Math.Round(rate, 2);
    }

    public static string MapOutputTechnology(DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY tech)
    {
        return tech switch
        {
            DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_HD15 => "VGA",
            DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_SVIDEO => "S-Video",
            DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_COMPOSITE_VIDEO => "Composite",
            DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_COMPONENT_VIDEO => "Component",
            DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DVI => "DVI",
            DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_HDMI => "HDMI",
            DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_LVDS => "LVDS",
            DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_D_JPN => "D-JPN",
            DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_SDI => "SDI",
            DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EXTERNAL => "DisplayPort",
            DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EMBEDDED => "Embedded DisplayPort",
            DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_UDI_EXTERNAL => "UDI",
            DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_UDI_EMBEDDED => "Embedded UDI",
            DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_SDTVDONGLE => "SDTV Dongle",
            DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_MIRACAST => "Miracast",
            DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INDIRECT_WIRED => "Indirect Wired",
            DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INDIRECT_VIRTUAL => "Indirect Virtual",
            DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_USB_TUNNEL => "DisplayPort USB Tunnel",
            DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INTERNAL => "Internal",
            DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_OTHER => "Other",
            _ => tech.ToString().Replace("DISPLAYCONFIG_OUTPUT_TECHNOLOGY_", "")
        };
    }

    public static string MapRotation(DISPLAYCONFIG_ROTATION rotation) =>
        rotation switch
        {
            DISPLAYCONFIG_ROTATION.DISPLAYCONFIG_ROTATION_IDENTITY => "Identity",
            DISPLAYCONFIG_ROTATION.DISPLAYCONFIG_ROTATION_90 => "Rotate90",
            DISPLAYCONFIG_ROTATION.DISPLAYCONFIG_ROTATION_180 => "Rotate180",
            DISPLAYCONFIG_ROTATION.DISPLAYCONFIG_ROTATION_270 => "Rotate270",
            _ => rotation.ToString().Replace("DISPLAYCONFIG_ROTATION_", "")
        };

    public static string MapScaling(DISPLAYCONFIG_SCALING scaling) =>
        scaling switch
        {
            DISPLAYCONFIG_SCALING.DISPLAYCONFIG_SCALING_IDENTITY => "Identity",
            DISPLAYCONFIG_SCALING.DISPLAYCONFIG_SCALING_CENTERED => "Centered",
            DISPLAYCONFIG_SCALING.DISPLAYCONFIG_SCALING_STRETCHED => "Stretched",
            DISPLAYCONFIG_SCALING.DISPLAYCONFIG_SCALING_ASPECTRATIORECTANCED => "AspectRatioCenteredMax",
            DISPLAYCONFIG_SCALING.DISPLAYCONFIG_SCALING_CUSTOM => "Custom",
            DISPLAYCONFIG_SCALING.DISPLAYCONFIG_SCALING_PREFERRED => "Preferred",
            _ => scaling.ToString().Replace("DISPLAYCONFIG_SCALING_", "")
        };

    public static string MapPixelFormat(DISPLAYCONFIG_PIXELFORMAT pixelFormat) =>
        pixelFormat switch
        {
            DISPLAYCONFIG_PIXELFORMAT.DISPLAYCONFIG_PIXELFORMAT_8BPP => "8Bpp",
            DISPLAYCONFIG_PIXELFORMAT.DISPLAYCONFIG_PIXELFORMAT_16BPP => "16Bpp",
            DISPLAYCONFIG_PIXELFORMAT.DISPLAYCONFIG_PIXELFORMAT_24BPP => "24Bpp",
            DISPLAYCONFIG_PIXELFORMAT.DISPLAYCONFIG_PIXELFORMAT_32BPP => "32Bpp",
            DISPLAYCONFIG_PIXELFORMAT.DISPLAYCONFIG_PIXELFORMAT_NONGDI => "NonGdi",
            _ => pixelFormat.ToString().Replace("DISPLAYCONFIG_PIXELFORMAT_", "")
        };

    public static string MapScanlineOrdering(DISPLAYCONFIG_SCANLINE_ORDERING ordering) =>
        ordering switch
        {
            DISPLAYCONFIG_SCANLINE_ORDERING.DISPLAYCONFIG_SCANLINE_ORDERING_UNSPECIFIED => "Unspecified",
            DISPLAYCONFIG_SCANLINE_ORDERING.DISPLAYCONFIG_SCANLINE_ORDERING_PROGRESSIVE => "Progressive",
            DISPLAYCONFIG_SCANLINE_ORDERING.DISPLAYCONFIG_SCANLINE_ORDERING_INTERLACED => "Interlaced",
            DISPLAYCONFIG_SCANLINE_ORDERING.DISPLAYCONFIG_SCANLINE_ORDERING_INTERLACED_LOWERFIELDFIRST => "InterlacedLowerFieldFirst",
            _ => ordering.ToString().Replace("DISPLAYCONFIG_SCANLINE_ORDERING_", "")
        };
}
