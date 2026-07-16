using System.Text.Json;
using CouchControl.Core.Abstractions;
using CouchControl.Core.Models;

namespace CouchControl.Windows.Persistence;

public sealed class JsonDisplayOperationJournalStore : IDisplayOperationJournalStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string filePath;

    public JsonDisplayOperationJournalStore(CouchControlPaths paths)
        : this(paths.OperationJournalFilePath)
    {
    }

    public JsonDisplayOperationJournalStore(string filePath)
    {
        this.filePath = filePath;
    }

    public Task<DisplayOperationJournal?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            return Task.FromResult<DisplayOperationJournal?>(null);
        }

        return AtomicJsonFile.ReadAsync<DisplayOperationJournal>(filePath, JsonOptions, cancellationToken);
    }

    public async Task SaveAsync(DisplayOperationJournal journal, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(journal);

        await AtomicJsonFile.WriteAsync(filePath, journal, JsonOptions, cancellationToken);
    }
}
