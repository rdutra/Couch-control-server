using System.IO;
using System.Text.Json;
using CouchControl.Core.Models;
using CouchControl.Windows.Persistence;

namespace CouchControl.Core.Tests;

public class DisplaySnapshotTests : IDisposable
{
    private readonly string _tempFolder;

    public DisplaySnapshotTests()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), "CouchControlSnapshotTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempFolder);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempFolder))
        {
            try { Directory.Delete(_tempFolder, true); } catch { }
        }
    }

    [Fact]
    public void Validate_FailsWhenNoActivePathExists()
    {
        var snapshot = new DisplaySnapshot(
            "snapshot-no-active",
            DateTimeOffset.UtcNow,
            Paths:
            [
                CreatePathSnapshot("display-1", false, new DisplayPoint(100, 100))
            ]);

        var result = snapshot.Validate();

        Assert.False(result.Succeeded);
        Assert.Equal("snapshot_active_path_required", result.ErrorCode);
    }

    [Fact]
    public void Validate_FailsWhenPrimaryDisplayIsNotAtOrigin()
    {
        var snapshot = new DisplaySnapshot(
            "snapshot-primary-invalid",
            DateTimeOffset.UtcNow,
            Paths:
            [
                CreatePathSnapshot("display-1", true, new DisplayPoint(100, 0), isPrimary: true)
            ]);

        var result = snapshot.Validate();

        Assert.False(result.Succeeded);
        Assert.Equal("snapshot_primary_position_invalid", result.ErrorCode);
    }

    [Fact]
    public async Task SerializationRoundTrip_PreservesRefreshRateAndPosition()
    {
        string filePath = Path.Combine(_tempFolder, "roundtrip.json");
        var store = new JsonDisplaySnapshotStore(filePath);
        var snapshot = new DisplaySnapshot(
            "snapshot-roundtrip",
            DateTimeOffset.UtcNow,
            Displays:
            [
                new DisplayDevice(
                    new DisplayIdentifier("display-1"),
                    "Monitor 1",
                    true,
                    true,
                    new DisplayMode(2560, 1440, 59.94m))
            ],
            Paths:
            [
                CreatePathSnapshot("display-1", true, new DisplayPoint(0, 0), refreshNumerator: 59940, refreshDenominator: 1000)
            ]);

        await store.SaveAsync(snapshot);
        var loaded = await store.LoadLastDesktopSnapshotAsync();

        Assert.NotNull(loaded);
        Assert.Equal(snapshot.SnapshotId, loaded.SnapshotId);
        Assert.Equal(new DisplayPoint(0, 0), loaded.Paths[0].SourceDesktopPosition);
        Assert.Equal(59940u, loaded.Paths[0].RefreshRate.Numerator);
        Assert.Equal(1000u, loaded.Paths[0].RefreshRate.Denominator);
        Assert.Equal(59.94m, loaded.Paths[0].RefreshRate.Hertz);

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(filePath));
        Assert.Equal(59940u, document.RootElement.GetProperty("Paths")[0].GetProperty("RefreshRate").GetProperty("Numerator").GetUInt32());
        Assert.Equal(1000u, document.RootElement.GetProperty("Paths")[0].GetProperty("RefreshRate").GetProperty("Denominator").GetUInt32());
    }

    [Fact]
    public async Task MultiDisplaySnapshot_RoundTripsAllPaths()
    {
        string filePath = Path.Combine(_tempFolder, "multi-display.json");
        var store = new JsonDisplaySnapshotStore(filePath);
        var snapshot = new DisplaySnapshot(
            "snapshot-multi",
            DateTimeOffset.UtcNow,
            Displays:
            [
                new DisplayDevice(new DisplayIdentifier("display-1"), "Primary", true, true, new DisplayMode(2560, 1440, 144m)),
                new DisplayDevice(new DisplayIdentifier("display-2"), "Secondary", true, false, new DisplayMode(1920, 1080, 60m))
            ],
            Paths:
            [
                CreatePathSnapshot("display-1", true, new DisplayPoint(0, 0), isPrimary: true, refreshNumerator: 144000, refreshDenominator: 1000),
                CreatePathSnapshot("display-2", true, new DisplayPoint(2560, 0), isPrimary: false, refreshNumerator: 60000, refreshDenominator: 1000)
            ]);

        await store.SaveAsync(snapshot);
        var loaded = await store.LoadLastDesktopSnapshotAsync();

        Assert.NotNull(loaded);
        Assert.Equal(2, loaded.Paths.Count);
        Assert.Equal(new DisplayPoint(0, 0), loaded.Paths[0].SourceDesktopPosition);
        Assert.Equal(new DisplayPoint(2560, 0), loaded.Paths[1].SourceDesktopPosition);
        Assert.True(loaded.Paths[0].IsPrimary);
        Assert.False(loaded.Paths[1].IsPrimary);
    }

    private static DisplayPathSnapshot CreatePathSnapshot(
        string identifier,
        bool isActive,
        DisplayPoint position,
        bool isPrimary = false,
        uint refreshNumerator = 60000,
        uint refreshDenominator = 1000) =>
        new(
            new DisplayIdentifier(identifier),
            "00000000:000001C8",
            1,
            2,
            isActive,
            isPrimary,
            position,
            1920,
            1080,
            "32Bpp",
            new DisplayRefreshRate(refreshNumerator, refreshDenominator),
            "Identity",
            "Preferred",
            "HDMI",
            new DisplaySourceModeSnapshot(1920, 1080, "32Bpp", position),
            new DisplayTargetModeSnapshot(new DisplayRefreshRate(refreshNumerator, refreshDenominator), 1920, 1080, "Progressive"));
}
