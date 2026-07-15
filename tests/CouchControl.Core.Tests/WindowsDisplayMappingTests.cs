using CouchControl.Core.Models;
using CouchControl.Windows.Displays;
using CouchControl.Windows.Displays.Interop;
using Xunit;

namespace CouchControl.Core.Tests;

public class WindowsDisplayMappingTests
{
    [Fact]
    public void MapToDomain_ActivePath_CorrectlyMapsBasicFields()
    {
        // Arrange
        var path = new DISPLAYCONFIG_PATH_INFO
        {
            flags = NativeMethods.DISPLAYCONFIG_PATH_ACTIVE,
            sourceInfo = new DISPLAYCONFIG_PATH_SOURCE_INFO
            {
                id = 1,
                modeInfoIdx = 0,
                adapterId = new LUID { LowPart = 123, HighPart = 456 }
            },
            targetInfo = new DISPLAYCONFIG_PATH_TARGET_INFO
            {
                id = 2,
                refreshRate = new DISPLAYCONFIG_RATIONAL { Numerator = 60000, Denominator = 1000 },
                outputTechnology = DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_HDMI
            }
        };

        var sourceMode = new DISPLAYCONFIG_MODE_INFO
        {
            infoType = DISPLAYCONFIG_MODE_INFO_TYPE.DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE,
            sourceMode = new DISPLAYCONFIG_SOURCE_MODE
            {
                width = 1920,
                height = 1080,
                position = new POINTL { x = 0, y = 0 } // Primary display
            }
        };

        string friendlyName = "Samsung TV";
        string devicePath = "\\\\?\\DISPLAY#SAM1234#4&abc123&0&UID0";

        // Act
        var display = DisplayMapper.MapToDomain(path, friendlyName, devicePath, sourceMode);

        // Assert
        Assert.Equal("Samsung TV", display.FriendlyName);
        Assert.Equal(devicePath, display.DevicePath);
        Assert.Equal(devicePath, display.Identifier.Value);
        Assert.True(display.IsActive);
        Assert.True(display.IsPrimary);
        Assert.NotNull(display.CurrentMode);
        Assert.Equal(1920, display.CurrentMode.Width);
        Assert.Equal(1080, display.CurrentMode.Height);
        Assert.Equal(60m, display.CurrentMode.RefreshRateHz);
        Assert.Equal("HDMI", display.OutputTechnology);
        Assert.Equal("000001C8:0000007B", display.AdapterLuid); // 456:123 in hex
        Assert.Equal(1u, display.SourceId);
        Assert.Equal(2u, display.TargetId);
    }

    [Theory]
    [InlineData(60000, 1000, 60.00)]
    [InlineData(165002, 1000, 165.00)]
    [InlineData(59940, 1000, 59.94)]
    [InlineData(119880, 1000, 119.88)]
    [InlineData(0, 1000, 0.00)]
    [InlineData(60, 0, 0.00)]
    public void CalculateRefreshRate_CalculatesExpectedRefreshRate(uint numerator, uint denominator, decimal expectedRate)
    {
        // Arrange
        var rational = new DISPLAYCONFIG_RATIONAL { Numerator = numerator, Denominator = denominator };

        // Act
        decimal result = DisplayMapper.CalculateRefreshRate(rational);

        // Assert
        Assert.Equal(expectedRate, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MapToDomain_NullOrMissingFriendlyName_FallsBackToDefault(string? friendlyName)
    {
        // Arrange
        var path = new DISPLAYCONFIG_PATH_INFO
        {
            flags = NativeMethods.DISPLAYCONFIG_PATH_ACTIVE,
            sourceInfo = new DISPLAYCONFIG_PATH_SOURCE_INFO { adapterId = new LUID { LowPart = 1, HighPart = 0 } },
            targetInfo = new DISPLAYCONFIG_PATH_TARGET_INFO { id = 10 }
        };

        // Act
        var display = DisplayMapper.MapToDomain(path, friendlyName, null, null);

        // Assert
        Assert.Equal("Generic Monitor", display.FriendlyName);
    }

    [Fact]
    public void MapToDomain_DuplicateFriendlyNamesWithDifferentPaths_CreatesDistinctDisplayDevices()
    {
        // Arrange
        var path1 = new DISPLAYCONFIG_PATH_INFO
        {
            flags = NativeMethods.DISPLAYCONFIG_PATH_ACTIVE,
            sourceInfo = new DISPLAYCONFIG_PATH_SOURCE_INFO { adapterId = new LUID { LowPart = 1, HighPart = 0 } },
            targetInfo = new DISPLAYCONFIG_PATH_TARGET_INFO { id = 1 }
        };
        var path2 = new DISPLAYCONFIG_PATH_INFO
        {
            flags = NativeMethods.DISPLAYCONFIG_PATH_ACTIVE,
            sourceInfo = new DISPLAYCONFIG_PATH_SOURCE_INFO { adapterId = new LUID { LowPart = 1, HighPart = 0 } },
            targetInfo = new DISPLAYCONFIG_PATH_TARGET_INFO { id = 2 }
        };

        string friendlyName = "Samsung TV";
        string path1Device = "\\\\?\\DISPLAY#SAM1#path1";
        string path2Device = "\\\\?\\DISPLAY#SAM1#path2";

        // Act
        var display1 = DisplayMapper.MapToDomain(path1, friendlyName, path1Device, null);
        var display2 = DisplayMapper.MapToDomain(path2, friendlyName, path2Device, null);

        // Assert
        Assert.Equal(display1.FriendlyName, display2.FriendlyName);
        Assert.NotEqual(display1.DevicePath, display2.DevicePath);
        Assert.NotEqual(display1.Identifier, display2.Identifier);
        Assert.False(display1.Identifier.Matches(display2.Identifier));
    }

    [Fact]
    public void MapSourceMode_PreservesDesktopPositionAndPrimaryInference()
    {
        var sourceMode = new DISPLAYCONFIG_MODE_INFO
        {
            infoType = DISPLAYCONFIG_MODE_INFO_TYPE.DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE,
            sourceMode = new DISPLAYCONFIG_SOURCE_MODE
            {
                width = 2560,
                height = 1440,
                pixelFormat = DISPLAYCONFIG_PIXELFORMAT.DISPLAYCONFIG_PIXELFORMAT_32BPP,
                position = new POINTL { x = 2560, y = 0 }
            }
        };

        var mapped = DisplayMapper.MapSourceMode(sourceMode);

        Assert.NotNull(mapped);
        Assert.Equal(new DisplayPoint(2560, 0), mapped!.Position);
        Assert.False(mapped.IsPrimary);
        Assert.Equal("32Bpp", mapped.PixelFormat);
    }

    [Fact]
    public void MapPathSnapshot_PreservesRefreshRateNumeratorAndDenominator()
    {
        var path = new DISPLAYCONFIG_PATH_INFO
        {
            flags = NativeMethods.DISPLAYCONFIG_PATH_ACTIVE,
            sourceInfo = new DISPLAYCONFIG_PATH_SOURCE_INFO
            {
                id = 1,
                adapterId = new LUID { LowPart = 123, HighPart = 456 }
            },
            targetInfo = new DISPLAYCONFIG_PATH_TARGET_INFO
            {
                id = 2,
                refreshRate = new DISPLAYCONFIG_RATIONAL { Numerator = 59940, Denominator = 1000 },
                outputTechnology = DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_HDMI,
                rotation = DISPLAYCONFIG_ROTATION.DISPLAYCONFIG_ROTATION_IDENTITY,
                scaling = DISPLAYCONFIG_SCALING.DISPLAYCONFIG_SCALING_PREFERRED
            }
        };

        var sourceMode = new DISPLAYCONFIG_MODE_INFO
        {
            infoType = DISPLAYCONFIG_MODE_INFO_TYPE.DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE,
            sourceMode = new DISPLAYCONFIG_SOURCE_MODE
            {
                width = 1920,
                height = 1080,
                pixelFormat = DISPLAYCONFIG_PIXELFORMAT.DISPLAYCONFIG_PIXELFORMAT_32BPP,
                position = new POINTL { x = 0, y = 0 }
            }
        };

        var targetMode = new DISPLAYCONFIG_MODE_INFO
        {
            infoType = DISPLAYCONFIG_MODE_INFO_TYPE.DISPLAYCONFIG_MODE_INFO_TYPE_TARGET,
            targetMode = new DISPLAYCONFIG_TARGET_MODE
            {
                targetVideoSignalInfo = new DISPLAYCONFIG_VIDEO_SIGNAL_INFO
                {
                    vSyncFreq = new DISPLAYCONFIG_RATIONAL { Numerator = 59940, Denominator = 1000 },
                    activeSize = new DISPLAYCONFIG_2DREGION { cx = 1920, cy = 1080 },
                    scanLineOrdering = DISPLAYCONFIG_SCANLINE_ORDERING.DISPLAYCONFIG_SCANLINE_ORDERING_PROGRESSIVE
                }
            }
        };

        var snapshot = DisplayMapper.MapPathSnapshot(path, "\\\\?\\DISPLAY#SAM#1", sourceMode, targetMode);

        Assert.Equal(59940u, snapshot.RefreshRate.Numerator);
        Assert.Equal(1000u, snapshot.RefreshRate.Denominator);
        Assert.Equal(59.94m, snapshot.RefreshRate.Hertz);
        Assert.Equal(new DisplayPoint(0, 0), snapshot.SourceDesktopPosition);
        Assert.True(snapshot.IsPrimary);
    }
}
