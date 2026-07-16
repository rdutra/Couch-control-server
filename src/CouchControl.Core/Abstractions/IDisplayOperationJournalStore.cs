using CouchControl.Core.Models;

namespace CouchControl.Core.Abstractions;

public interface IDisplayOperationJournalStore
{
    Task<DisplayOperationJournal?> LoadAsync(
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        DisplayOperationJournal journal,
        CancellationToken cancellationToken = default);
}
