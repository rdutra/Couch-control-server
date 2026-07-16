using CouchControl.Core.Models;

namespace CouchControl.Core.Abstractions;

public interface IDisplaySnapshotStore
{
    Task SaveAsync(
        DisplaySnapshot snapshot,
        CancellationToken cancellationToken = default);

    Task SavePendingAsync(
        DisplaySnapshot snapshot,
        CancellationToken cancellationToken = default);

    Task<DisplaySnapshot?> LoadLastDesktopSnapshotAsync(
        CancellationToken cancellationToken = default);

    Task<DisplaySnapshot?> LoadByIdAsync(
        string snapshotId,
        CancellationToken cancellationToken = default);

    Task PromotePendingAsync(
        string snapshotId,
        CancellationToken cancellationToken = default);

    Task ClearAsync(
        CancellationToken cancellationToken = default);
}
