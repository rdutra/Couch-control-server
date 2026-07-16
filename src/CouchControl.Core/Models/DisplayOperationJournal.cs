namespace CouchControl.Core.Models;

public sealed record DisplayOperationJournal(
    Guid OperationId,
    string OperationType,
    string State,
    string RollbackSnapshotId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc = null,
    DateTimeOffset? RecoveredAtUtc = null)
{
    public bool IsInProgress =>
        string.Equals(State, DisplayOperationJournalStates.InProgress, StringComparison.OrdinalIgnoreCase);

    public bool IsCompleted =>
        string.Equals(State, DisplayOperationJournalStates.Completed, StringComparison.OrdinalIgnoreCase);

    public bool IsRecovered =>
        string.Equals(State, DisplayOperationJournalStates.Recovered, StringComparison.OrdinalIgnoreCase);

    public DisplayOperationJournal MarkCompleted(DateTimeOffset completedAtUtc) =>
        this with
        {
            State = DisplayOperationJournalStates.Completed,
            CompletedAtUtc = completedAtUtc
        };

    public DisplayOperationJournal MarkRecovered(DateTimeOffset recoveredAtUtc) =>
        this with
        {
            State = DisplayOperationJournalStates.Recovered,
            RecoveredAtUtc = recoveredAtUtc
        };
}

public static class DisplayOperationJournalStates
{
    public const string InProgress = "in-progress";
    public const string Completed = "completed";
    public const string Recovered = "recovered";
}

public static class DisplayOperationJournalTypes
{
    public const string ActivateCouch = "activate-couch";
    public const string ActivateDesktop = "activate-desktop";
}
