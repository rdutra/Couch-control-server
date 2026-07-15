using CouchControl.Core.Models;

namespace CouchControl.Core.Abstractions;

public interface IDisplaySnapshotStore
{
    Task SaveAsync(
        DisplaySnapshot snapshot,
        CancellationToken cancellationToken = default);

    Task<DisplaySnapshot?> LoadLastDesktopSnapshotAsync(
        CancellationToken cancellationToken = default);
}
