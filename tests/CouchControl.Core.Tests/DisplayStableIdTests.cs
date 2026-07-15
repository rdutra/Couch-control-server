using CouchControl.Core.Models;

namespace CouchControl.Core.Tests;

public sealed class DisplayStableIdTests
{
    [Fact]
    public void FromDevicePath_IsDeterministicAndShort()
    {
        const string devicePath = @"\\?\DISPLAY#SAM0123#4&abcd12&0&UID0";

        string first = DisplayStableId.FromDevicePath(devicePath);
        string second = DisplayStableId.FromDevicePath(devicePath);

        Assert.Equal(first, second);
        Assert.Equal(12, first.Length);
    }

    [Fact]
    public void FromDevicePath_BlankPath_ReturnsUnknown()
    {
        Assert.Equal("unknown", DisplayStableId.FromDevicePath(" "));
    }
}
