using System;
using System.Collections.Generic;
using CouchControl.Core.Models;
using CouchControl.Core.Orchestration;
using Xunit;

namespace CouchControl.Core.Tests;

public class DisplayMatchingTests
{
    private readonly DisplayMatchingService _matcher = new();

    private readonly CouchDisplayIdentity _target = new(
        DevicePath: "\\\\?\\DISPLAY#SAM0123#4&abcd12&0&UID0",
        FriendlyName: "Samsung TV",
        Manufacturer: "SAM",
        ProductCode: "0123",
        SerialOrInstance: "4&abcd12&0&UID0",
        AdapterLuid: "00000000:000001C8",
        TargetId: 2
    );

    [Fact]
    public void MatchDisplay_ExactDevicePath_MatchesSuccessfully()
    {
        // Arrange
        var connected = new List<DisplayDevice>
        {
            new DisplayDevice(new DisplayIdentifier("id1"), "Wrong Name", true, false, null,
                DevicePath: "\\\\?\\DISPLAY#SAM0123#4&abcd12&0&UID0",
                AdapterLuid: "different", TargetId: 99)
        };

        // Act
        var result = _matcher.MatchDisplay(_target, connected);

        // Assert
        Assert.Equal("Wrong Name", result.FriendlyName);
    }

    [Fact]
    public void MatchDisplay_ManufacturerProductSerial_MatchesSuccessfully()
    {
        // Arrange
        var connected = new List<DisplayDevice>
        {
            new DisplayDevice(new DisplayIdentifier("id1"), "Wrong Name", true, false, null,
                DevicePath: "\\\\?\\DISPLAY#SAM0123#4&abcd12&0&UID0#{e6f56179-beb4-4a1f-83b8-0c67a3d6528a}", // Same Manufacturer/ProductCode/SerialOrInstance
                AdapterLuid: "different", TargetId: 99)
        };

        // Act
        var result = _matcher.MatchDisplay(_target, connected);

        // Assert
        Assert.Equal("Wrong Name", result.FriendlyName);
    }

    [Fact]
    public void MatchDisplay_AmbiguousManufacturerProductSerial_ThrowsInvalidOperationException()
    {
        var connected = new List<DisplayDevice>
        {
            new DisplayDevice(new DisplayIdentifier("id1"), "Samsung TV A", true, false, null,
                DevicePath: "\\\\?\\DISPLAY#SAM0123#4&abcd12&0&UID0#{guid-a}"),
            new DisplayDevice(new DisplayIdentifier("id2"), "Samsung TV B", true, false, null,
                DevicePath: "\\\\?\\DISPLAY#SAM0123#4&abcd12&0&UID0#{guid-b}")
        };

        var ex = Assert.Throws<InvalidOperationException>(() => _matcher.MatchDisplay(_target, connected));
        Assert.Contains("manufacturer 'SAM'", ex.Message);
    }

    [Fact]
    public void MatchDisplay_AdapterAndTarget_MatchesSuccessfully()
    {
        // Arrange
        var connected = new List<DisplayDevice>
        {
            new DisplayDevice(new DisplayIdentifier("id1"), "Wrong Name", true, false, null,
                DevicePath: "completely_different",
                AdapterLuid: "00000000:000001C8", TargetId: 2)
        };

        // Act
        var result = _matcher.MatchDisplay(_target, connected);

        // Assert
        Assert.Equal("Wrong Name", result.FriendlyName);
    }

    [Fact]
    public void MatchDisplay_FriendlyName_MatchesSuccessfully()
    {
        // Arrange
        var connected = new List<DisplayDevice>
        {
            new DisplayDevice(new DisplayIdentifier("id1"), "Samsung TV", true, false, null,
                DevicePath: "completely_different",
                AdapterLuid: "different", TargetId: 99)
        };

        // Act
        var result = _matcher.MatchDisplay(_target, connected);

        // Assert
        Assert.Equal("Samsung TV", result.FriendlyName);
    }

    [Fact]
    public void MatchDisplay_AmbiguousFriendlyName_ThrowsInvalidOperationException()
    {
        // Arrange
        var connected = new List<DisplayDevice>
        {
            new DisplayDevice(new DisplayIdentifier("id1"), "Samsung TV", true, false, null,
                DevicePath: "path1", AdapterLuid: "diff1", TargetId: 10),
            new DisplayDevice(new DisplayIdentifier("id2"), "Samsung TV", true, false, null,
                DevicePath: "path2", AdapterLuid: "diff2", TargetId: 20)
        };

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => _matcher.MatchDisplay(_target, connected));
        Assert.Contains("Ambiguous match: Multiple displays found with friendly name", ex.Message);
    }

    [Fact]
    public void MatchDisplay_AmbiguousAdapterAndTarget_ThrowsInvalidOperationException()
    {
        var connected = new List<DisplayDevice>
        {
            new DisplayDevice(new DisplayIdentifier("id1"), "TV 1", true, false, null,
                DevicePath: "path1", AdapterLuid: "00000000:000001C8", TargetId: 2),
            new DisplayDevice(new DisplayIdentifier("id2"), "TV 2", true, false, null,
                DevicePath: "path2", AdapterLuid: "00000000:000001C8", TargetId: 2)
        };

        var ex = Assert.Throws<InvalidOperationException>(() => _matcher.MatchDisplay(_target, connected));
        Assert.Contains("adapter LUID", ex.Message);
    }

    [Fact]
    public void MatchDisplay_NoMatchAtAll_ThrowsInvalidOperationException()
    {
        // Arrange
        var connected = new List<DisplayDevice>
        {
            new DisplayDevice(new DisplayIdentifier("id1"), "LG OLED", true, false, null,
                DevicePath: "path1", AdapterLuid: "diff1", TargetId: 10)
        };

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => _matcher.MatchDisplay(_target, connected));
        Assert.Contains("Display not found", ex.Message);
    }
}
