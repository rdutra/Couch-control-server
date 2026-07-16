using CouchControl.Core.Abstractions;
using CouchControl.Core.Models;

namespace CouchControl.Windows;

public sealed class InMemoryDisplaySnapshotStore : IDisplaySnapshotStore
{
    private readonly Dictionary<string, DisplaySnapshot> snapshots = new(StringComparer.Ordinal);
    private DisplaySnapshot? snapshot;

    public Task SaveAsync(DisplaySnapshot snapshot, CancellationToken cancellationToken = default)
    {
        this.snapshot = snapshot;
        snapshots[snapshot.SnapshotId] = snapshot;
        return Task.CompletedTask;
    }

    public Task SavePendingAsync(DisplaySnapshot snapshot, CancellationToken cancellationToken = default)
    {
        snapshots[snapshot.SnapshotId] = snapshot;
        return Task.CompletedTask;
    }

    public Task<DisplaySnapshot?> LoadLastDesktopSnapshotAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(snapshot);

    public Task<DisplaySnapshot?> LoadByIdAsync(string snapshotId, CancellationToken cancellationToken = default)
    {
        snapshots.TryGetValue(snapshotId, out var loaded);
        return Task.FromResult(loaded);
    }

    public Task PromotePendingAsync(string snapshotId, CancellationToken cancellationToken = default)
    {
        snapshots.TryGetValue(snapshotId, out snapshot);
        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        snapshot = null;
        return Task.CompletedTask;
    }
}
