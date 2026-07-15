using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using CouchControl.Core.Models;
using CouchControl.Windows.Persistence;
using Xunit;

namespace CouchControl.Core.Tests;

public class PersistenceTests : IDisposable
{
    private readonly string _tempFolder;

    public PersistenceTests()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), "CouchControlTests_" + Guid.NewGuid().ToString("N"));
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
    public async Task ConfigurationStore_SavesAndLoadsSuccessfully()
    {
        // Arrange
        string filePath = Path.Combine(_tempFolder, "config.json");
        var store = new JsonAgentConfigurationStore(filePath);
        var expectedConfig = new AgentConfiguration
        {
            AgentName = "Test Agent",
            CouchDisplayIdentifier = new DisplayIdentifier(@"\\?\DISPLAY#SAM0123#4&abcd12&0&UID0"),
            CouchDisplayIdentity = new CouchDisplayIdentity(
                @"\\?\DISPLAY#SAM0123#4&abcd12&0&UID0",
                "Samsung TV",
                "SAM",
                "0123",
                "4&abcd12&0&UID0",
                "00000000:000001C8",
                2),
            PreferredCouchWidth = 1920,
            PreferredCouchHeight = 1080,
            PreferredCouchRefreshRateHz = 120,
            LaunchSteamAutomatically = false
        };

        // Act
        await store.SaveAsync(expectedConfig);
        var actualConfig = await store.LoadAsync();

        // Assert
        Assert.True(File.Exists(filePath));
        Assert.Equal(expectedConfig.SchemaVersion, actualConfig.SchemaVersion);
        Assert.Equal(expectedConfig.AgentName, actualConfig.AgentName);
        Assert.Equal(expectedConfig.CouchDisplayIdentifier, actualConfig.CouchDisplayIdentifier);
        Assert.NotNull(actualConfig.CouchDisplayIdentity);
        Assert.Equal("Samsung TV", actualConfig.CouchDisplayIdentity!.FriendlyName);
        Assert.Equal(
            DisplayStableId.FromDevicePath(expectedConfig.CouchDisplayIdentity!.DevicePath),
            actualConfig.CouchDisplayIdentity.StableId);
        Assert.Equal(expectedConfig.PreferredCouchWidth, actualConfig.PreferredCouchWidth);
        Assert.Equal(expectedConfig.PreferredCouchHeight, actualConfig.PreferredCouchHeight);
        Assert.Equal(expectedConfig.PreferredCouchRefreshRateHz, actualConfig.PreferredCouchRefreshRateHz);
        Assert.Equal(expectedConfig.LaunchSteamAutomatically, actualConfig.LaunchSteamAutomatically);

        string savedJson = await File.ReadAllTextAsync(filePath);
        Assert.Contains("\"SchemaVersion\": 1", savedJson);
        Assert.Contains("\"CouchDisplay\"", savedJson);
    }

    [Fact]
    public async Task DisplaySnapshotStore_SavesAndLoadsSuccessfully()
    {
        // Arrange
        string filePath = Path.Combine(_tempFolder, "snapshot.json");
        var store = new JsonDisplaySnapshotStore(filePath);
        var displays = new[]
        {
            new DisplayDevice(
                new DisplayIdentifier("test_id"),
                "Test Monitor",
                true,
                true,
                new DisplayMode(1920, 1080, 60))
        };
        var snapshot = new DisplaySnapshot(
            "snapshot-1",
            DateTimeOffset.UtcNow,
            displays,
            [
                new DisplayPathSnapshot(
                    new DisplayIdentifier("test_id"),
                    "00000000:000001C8",
                    1,
                    2,
                    true,
                    true,
                    new DisplayPoint(0, 0),
                    1920,
                    1080,
                    "32Bpp",
                    new DisplayRefreshRate(60000, 1000),
                    "Identity",
                    "Preferred",
                    "HDMI",
                    new DisplaySourceModeSnapshot(1920, 1080, "32Bpp", new DisplayPoint(0, 0)),
                    new DisplayTargetModeSnapshot(new DisplayRefreshRate(60000, 1000), 1920, 1080, "Progressive"))
            ]);

        // Act
        await store.SaveAsync(snapshot);
        var loaded = await store.LoadLastDesktopSnapshotAsync();

        // Assert
        Assert.True(File.Exists(filePath));
        Assert.NotNull(loaded);
        Assert.Equal(snapshot.SnapshotId, loaded.SnapshotId);
        Assert.Equal(snapshot.CapturedAtUtc, loaded.CapturedAtUtc);
        Assert.Single(loaded.Displays);
        Assert.Single(loaded.Paths);
        Assert.Equal("Test Monitor", loaded.Displays[0].FriendlyName);
        Assert.Equal(new DisplayPoint(0, 0), loaded.Paths[0].SourceDesktopPosition);
        Assert.Equal(60000u, loaded.Paths[0].RefreshRate.Numerator);
        Assert.Equal(1000u, loaded.Paths[0].RefreshRate.Denominator);

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(filePath));
        Assert.Equal(1, document.RootElement.GetProperty("SchemaVersion").GetInt32());
        Assert.Equal("snapshot-1", document.RootElement.GetProperty("SnapshotId").GetString());
        Assert.True(document.RootElement.TryGetProperty("SavedAtUtc", out _));
    }

    [Fact]
    public async Task ConfigurationStore_CorruptedJson_ThrowsInvalidOperationException()
    {
        // Arrange
        string filePath = Path.Combine(_tempFolder, "corrupted_config.json");
        await File.WriteAllTextAsync(filePath, "{ invalid json: ");
        var store = new JsonAgentConfigurationStore(filePath);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => store.LoadAsync());
        Assert.Contains("corrupted or invalid", ex.Message);
    }

    [Fact]
    public async Task DisplaySnapshotStore_CorruptedJson_ThrowsInvalidOperationException()
    {
        // Arrange
        string filePath = Path.Combine(_tempFolder, "corrupted_snapshot.json");
        await File.WriteAllTextAsync(filePath, "{ invalid json: ");
        var store = new JsonDisplaySnapshotStore(filePath);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => store.LoadLastDesktopSnapshotAsync());
        Assert.Contains("corrupted or invalid", ex.Message);
    }

    [Fact]
    public async Task ConfigurationStore_AtomicReplacement_ReplacesCorrectlyAndCleansUpTempFile()
    {
        // Arrange
        string filePath = Path.Combine(_tempFolder, "atomic_config.json");
        var store = new JsonAgentConfigurationStore(filePath);
        var initialConfig = new AgentConfiguration { AgentName = "Initial" };
        await store.SaveAsync(initialConfig);

        var nextConfig = new AgentConfiguration { AgentName = "Next" };

        // Act
        await store.SaveAsync(nextConfig);
        var loaded = await store.LoadAsync();

        // Assert
        Assert.Equal("Next", loaded.AgentName);
        Assert.Empty(Directory.GetFiles(_tempFolder, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task ConfigurationStore_CreatesSupportingDirectoriesAutomatically()
    {
        string filePath = Path.Combine(_tempFolder, "state", "config.json");
        var store = new JsonAgentConfigurationStore(filePath);

        await store.SaveAsync(new AgentConfiguration());

        Assert.True(Directory.Exists(Path.Combine(_tempFolder, "state", "snapshots")));
        Assert.True(Directory.Exists(Path.Combine(_tempFolder, "state", "logs")));
    }

    [Fact]
    public async Task ConfigurationStore_PreservesIdentifierWithoutIdentity()
    {
        string filePath = Path.Combine(_tempFolder, "identifier_only.json");
        var store = new JsonAgentConfigurationStore(filePath);
        var configuration = new AgentConfiguration
        {
            CouchDisplayIdentifier = new DisplayIdentifier(@"\\?\DISPLAY#ONLY#PATH")
        };

        await store.SaveAsync(configuration);
        var loaded = await store.LoadAsync();

        Assert.Equal(configuration.CouchDisplayIdentifier, loaded.CouchDisplayIdentifier);
        Assert.Null(loaded.CouchDisplayIdentity);
    }
}
