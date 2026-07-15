using CouchControl.Core.Abstractions;
using CouchControl.Core.Models;

namespace CouchControl.Windows;

public sealed class InMemoryDisplaySnapshotStore : IDisplaySnapshotStore
{
    private DisplaySnapshot? snapshot;

    public Task SaveAsync(DisplaySnapshot snapshot, CancellationToken cancellationToken = default)
    {
        this.snapshot = snapshot;
        return Task.CompletedTask;
    }

    public Task<DisplaySnapshot?> LoadLastDesktopSnapshotAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(snapshot);
}
