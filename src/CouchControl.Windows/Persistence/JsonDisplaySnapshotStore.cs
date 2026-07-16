using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CouchControl.Core.Abstractions;
using CouchControl.Core.Models;

namespace CouchControl.Windows.Persistence;

public sealed class JsonDisplaySnapshotStore : IDisplaySnapshotStore
{
    private const int CurrentSchemaVersion = 1;
    private readonly string filePath;
    private readonly string snapshotsDirectory;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public JsonDisplaySnapshotStore()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        snapshotsDirectory = Path.Combine(localAppData, "CouchControl", "snapshots");
        filePath = Path.Combine(snapshotsDirectory, "last-desktop.json");
    }

    public JsonDisplaySnapshotStore(string filePath)
    {
        this.filePath = filePath;
        snapshotsDirectory = Path.GetDirectoryName(filePath)
            ?? throw new InvalidOperationException($"Cannot determine snapshot directory for '{filePath}'.");
    }

    public async Task<DisplaySnapshot?> LoadLastDesktopSnapshotAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        var persisted = await AtomicJsonFile.ReadAsync<PersistedDisplaySnapshot>(
            filePath,
            JsonOptions,
            cancellationToken);

        if (persisted == null)
        {
            throw new InvalidOperationException(
                $"Failed to load display snapshot from '{filePath}': the file is empty.");
        }

        if (persisted.SchemaVersion <= 0)
        {
            throw new InvalidOperationException(
                $"Failed to load display snapshot from '{filePath}': schemaVersion must be a positive integer.");
        }

        var snapshot = persisted.ToDomain();
        var validation = snapshot.Validate();
        if (!validation.Succeeded)
        {
            throw new InvalidOperationException(
                $"Failed to load display snapshot from '{filePath}': {validation.Message}");
        }

        return snapshot;
    }

    public async Task SaveAsync(DisplaySnapshot snapshot, CancellationToken cancellationToken = default)
    {
        if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

        var validation = snapshot.Validate();
        if (!validation.Succeeded)
        {
            throw new InvalidOperationException($"Cannot save invalid display snapshot: {validation.Message}");
        }

        string? directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var persisted = PersistedDisplaySnapshot.FromDomain(snapshot);
        await AtomicJsonFile.WriteAsync(filePath, persisted, JsonOptions, cancellationToken);
        await AtomicJsonFile.WriteAsync(GetSnapshotFilePath(snapshot.SnapshotId), persisted, JsonOptions, cancellationToken);
    }

    public async Task SavePendingAsync(DisplaySnapshot snapshot, CancellationToken cancellationToken = default)
    {
        if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

        var validation = snapshot.Validate();
        if (!validation.Succeeded)
        {
            throw new InvalidOperationException($"Cannot save invalid display snapshot: {validation.Message}");
        }

        Directory.CreateDirectory(snapshotsDirectory);
        await AtomicJsonFile.WriteAsync(
            GetSnapshotFilePath(snapshot.SnapshotId),
            PersistedDisplaySnapshot.FromDomain(snapshot),
            JsonOptions,
            cancellationToken);
    }

    public async Task<DisplaySnapshot?> LoadByIdAsync(string snapshotId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(snapshotId))
        {
            throw new ArgumentException("A snapshot ID must be provided.", nameof(snapshotId));
        }

        string pendingPath = GetSnapshotFilePath(snapshotId);
        if (!File.Exists(pendingPath))
        {
            return null;
        }

        var persisted = await AtomicJsonFile.ReadAsync<PersistedDisplaySnapshot>(pendingPath, JsonOptions, cancellationToken);
        if (persisted == null)
        {
            throw new InvalidOperationException(
                $"Failed to load display snapshot from '{pendingPath}': the file is empty.");
        }

        return persisted.ToDomain();
    }

    public async Task PromotePendingAsync(string snapshotId, CancellationToken cancellationToken = default)
    {
        var snapshot = await LoadByIdAsync(snapshotId, cancellationToken);
        if (snapshot is null)
        {
            throw new InvalidOperationException(
                $"Cannot promote rollback snapshot '{snapshotId}' because it does not exist.");
        }

        await SaveAsync(snapshot, cancellationToken);
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }

        return Task.CompletedTask;
    }

    private string GetSnapshotFilePath(string snapshotId) =>
        Path.Combine(snapshotsDirectory, $"{snapshotId}.json");

    private sealed record PersistedDisplaySnapshot
    {
        public int SchemaVersion { get; init; } = CurrentSchemaVersion;

        public string SnapshotId { get; init; } = string.Empty;

        public DateTimeOffset CapturedAtUtc { get; init; }

        public DateTimeOffset SavedAtUtc { get; init; }

        public PersistedDisplayDevice[] Displays { get; init; } = [];

        public PersistedDisplayPathSnapshot[] Paths { get; init; } = [];

        public DisplaySnapshot ToDomain() =>
            new(
                SnapshotId,
                CapturedAtUtc,
                Displays.Select(static d => d.ToDomain()).ToArray(),
                Paths.Select(static p => p.ToDomain()).ToArray());

        public static PersistedDisplaySnapshot FromDomain(DisplaySnapshot snapshot) =>
            new()
            {
                SchemaVersion = CurrentSchemaVersion,
                SnapshotId = snapshot.SnapshotId,
                CapturedAtUtc = snapshot.CapturedAtUtc,
                SavedAtUtc = DateTimeOffset.UtcNow,
                Displays = snapshot.Displays.Select(static d => PersistedDisplayDevice.FromDomain(d)).ToArray(),
                Paths = snapshot.Paths.Select(static p => PersistedDisplayPathSnapshot.FromDomain(p)).ToArray()
            };
    }

    private sealed record PersistedDisplayDevice
    {
        public string Identifier { get; init; } = string.Empty;

        public string FriendlyName { get; init; } = string.Empty;

        public bool IsActive { get; init; }

        public bool IsPrimary { get; init; }

        public PersistedDisplayMode? CurrentMode { get; init; }

        public string? DevicePath { get; init; }

        public string? AdapterLuid { get; init; }

        public uint? SourceId { get; init; }

        public uint? TargetId { get; init; }

        public string? OutputTechnology { get; init; }

        public DisplayDevice ToDomain() =>
            new(
                new DisplayIdentifier(Identifier),
                FriendlyName,
                IsActive,
                IsPrimary,
                CurrentMode?.ToDomain(),
                DevicePath,
                AdapterLuid,
                SourceId,
                TargetId,
                OutputTechnology);

        public static PersistedDisplayDevice FromDomain(DisplayDevice display) =>
            new()
            {
                Identifier = display.Identifier.Value,
                FriendlyName = display.FriendlyName,
                IsActive = display.IsActive,
                IsPrimary = display.IsPrimary,
                CurrentMode = PersistedDisplayMode.FromNullableDomain(display.CurrentMode),
                DevicePath = display.DevicePath,
                AdapterLuid = display.AdapterLuid,
                SourceId = display.SourceId,
                TargetId = display.TargetId,
                OutputTechnology = display.OutputTechnology
            };
    }

    private sealed record PersistedDisplayPathSnapshot
    {
        public string Identifier { get; init; } = string.Empty;

        public string AdapterLuid { get; init; } = string.Empty;

        public uint SourceId { get; init; }

        public uint TargetId { get; init; }

        public bool IsActive { get; init; }

        public bool IsPrimary { get; init; }

        public PersistedDisplayPoint? SourceDesktopPosition { get; init; }

        public uint? Width { get; init; }

        public uint? Height { get; init; }

        public string? PixelFormat { get; init; }

        public PersistedDisplayRefreshRate RefreshRate { get; init; } = new();

        public string Rotation { get; init; } = string.Empty;

        public string Scaling { get; init; } = string.Empty;

        public string OutputTechnology { get; init; } = string.Empty;

        public PersistedDisplaySourceModeSnapshot? SourceMode { get; init; }

        public PersistedDisplayTargetModeSnapshot? TargetMode { get; init; }

        public DisplayPathSnapshot ToDomain() =>
            new(
                new DisplayIdentifier(Identifier),
                AdapterLuid,
                SourceId,
                TargetId,
                IsActive,
                IsPrimary,
                SourceDesktopPosition?.ToDomain(),
                Width,
                Height,
                PixelFormat,
                RefreshRate.ToDomain(),
                Rotation,
                Scaling,
                OutputTechnology,
                SourceMode?.ToDomain(),
                TargetMode?.ToDomain());

        public static PersistedDisplayPathSnapshot FromDomain(DisplayPathSnapshot path) =>
            new()
            {
                Identifier = path.Identifier.Value,
                AdapterLuid = path.AdapterLuid,
                SourceId = path.SourceId,
                TargetId = path.TargetId,
                IsActive = path.IsActive,
                IsPrimary = path.IsPrimary,
                SourceDesktopPosition = PersistedDisplayPoint.FromNullableDomain(path.SourceDesktopPosition),
                Width = path.Width,
                Height = path.Height,
                PixelFormat = path.PixelFormat,
                RefreshRate = PersistedDisplayRefreshRate.FromDomain(path.RefreshRate),
                Rotation = path.Rotation,
                Scaling = path.Scaling,
                OutputTechnology = path.OutputTechnology,
                SourceMode = PersistedDisplaySourceModeSnapshot.FromNullableDomain(path.SourceMode),
                TargetMode = PersistedDisplayTargetModeSnapshot.FromNullableDomain(path.TargetMode)
            };
    }

    private sealed record PersistedDisplayPoint
    {
        public int X { get; init; }

        public int Y { get; init; }

        public DisplayPoint ToDomain() => new(X, Y);

        public static PersistedDisplayPoint? FromNullableDomain(DisplayPoint? point) =>
            point == null
                ? null
                : new PersistedDisplayPoint
                {
                    X = point.X,
                    Y = point.Y
                };
    }

    private sealed record PersistedDisplayRefreshRate
    {
        public uint Numerator { get; init; }

        public uint Denominator { get; init; }

        public DisplayRefreshRate ToDomain() => new(Numerator, Denominator);

        public static PersistedDisplayRefreshRate FromDomain(DisplayRefreshRate refreshRate) =>
            new()
            {
                Numerator = refreshRate.Numerator,
                Denominator = refreshRate.Denominator
            };
    }

    private sealed record PersistedDisplaySourceModeSnapshot
    {
        public uint Width { get; init; }

        public uint Height { get; init; }

        public string PixelFormat { get; init; } = string.Empty;

        public PersistedDisplayPoint Position { get; init; } = new();

        public DisplaySourceModeSnapshot ToDomain() =>
            new(Width, Height, PixelFormat, Position.ToDomain());

        public static PersistedDisplaySourceModeSnapshot? FromNullableDomain(DisplaySourceModeSnapshot? mode) =>
            mode == null
                ? null
                : new PersistedDisplaySourceModeSnapshot
                {
                    Width = mode.Width,
                    Height = mode.Height,
                    PixelFormat = mode.PixelFormat,
                    Position = PersistedDisplayPoint.FromNullableDomain(mode.Position) ?? new PersistedDisplayPoint()
                };
    }

    private sealed record PersistedDisplayTargetModeSnapshot
    {
        public PersistedDisplayRefreshRate RefreshRate { get; init; } = new();

        public uint ActiveWidth { get; init; }

        public uint ActiveHeight { get; init; }

        public string ScanLineOrdering { get; init; } = string.Empty;

        public DisplayTargetModeSnapshot ToDomain() =>
            new(RefreshRate.ToDomain(), ActiveWidth, ActiveHeight, ScanLineOrdering);

        public static PersistedDisplayTargetModeSnapshot? FromNullableDomain(DisplayTargetModeSnapshot? mode) =>
            mode == null
                ? null
                : new PersistedDisplayTargetModeSnapshot
                {
                    RefreshRate = PersistedDisplayRefreshRate.FromDomain(mode.RefreshRate),
                    ActiveWidth = mode.ActiveWidth,
                    ActiveHeight = mode.ActiveHeight,
                    ScanLineOrdering = mode.ScanLineOrdering
                };
    }

    private sealed record PersistedDisplayMode
    {
        public int Width { get; init; }

        public int Height { get; init; }

        public decimal RefreshRateHz { get; init; }

        public DisplayMode ToDomain() => new(Width, Height, RefreshRateHz);

        public static PersistedDisplayMode? FromNullableDomain(DisplayMode? mode) =>
            mode == null
                ? null
                : new PersistedDisplayMode
                {
                    Width = mode.Width,
                    Height = mode.Height,
                    RefreshRateHz = mode.RefreshRateHz
                };
    }
}
