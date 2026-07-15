using CouchControl.Windows;
using Microsoft.Extensions.Logging.Abstractions;

namespace CouchControl.Core.Tests;

public sealed class WindowsDisplayManagerNativeIntegrationTests
{
    [Fact(Skip = "Native display integration tests are skipped by default because they change active displays.")]
    [Trait("Category", "NativeIntegration")]
    public async Task CaptureSnapshotAsync_CapturesCurrentWindowsTopology()
    {
        var manager = new WindowsDisplayManager(NullLogger<WindowsDisplayManager>.Instance);

        var snapshot = await manager.CaptureSnapshotAsync();

        Assert.NotNull(snapshot);
        Assert.NotEmpty(snapshot.Paths);
    }
}
