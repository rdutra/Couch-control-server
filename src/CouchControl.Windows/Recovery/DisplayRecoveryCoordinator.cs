using CouchControl.Core.Abstractions;
using CouchControl.Core.Models;
using Microsoft.Extensions.Logging;

namespace CouchControl.Windows.Recovery;

public interface IDisplayRecoveryCoordinator
{
    Task<DisplayRecoveryCheckResult> CheckAsync(CancellationToken cancellationToken = default);

    Task<DisplayRecoveryAttemptResult> RecoverAsync(CancellationToken cancellationToken = default);
}

public sealed class DisplayRecoveryCoordinator : IDisplayRecoveryCoordinator
{
    private readonly IAgentConfigurationStore configurationStore;
    private readonly IDisplayOperationJournalStore journalStore;
    private readonly IDisplaySnapshotStore snapshotStore;
    private readonly IProfileOrchestrator profileOrchestrator;
    private readonly CouchControlPaths paths;
    private readonly ILogger<DisplayRecoveryCoordinator> logger;

    public DisplayRecoveryCoordinator(
        IAgentConfigurationStore configurationStore,
        IDisplayOperationJournalStore journalStore,
        IDisplaySnapshotStore snapshotStore,
        IProfileOrchestrator profileOrchestrator,
        CouchControlPaths paths,
        ILogger<DisplayRecoveryCoordinator> logger)
    {
        this.configurationStore = configurationStore;
        this.journalStore = journalStore;
        this.snapshotStore = snapshotStore;
        this.profileOrchestrator = profileOrchestrator;
        this.paths = paths;
        this.logger = logger;
    }

    public async Task<DisplayRecoveryCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var configuration = await configurationStore.LoadAsync(cancellationToken);

        DisplayOperationJournal? journal;
        try
        {
            journal = await journalStore.LoadAsync(cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogError(ex, "Failed to load the display operation journal from {JournalPath}.", paths.OperationJournalFilePath);
            return new DisplayRecoveryCheckResult(
                DisplayRecoveryIssue.CorruptedJournal,
                null,
                null,
                $"The display recovery journal is corrupted. Review {paths.OperationJournalFilePath} and the latest logs before attempting manual recovery.",
                configuration.AutomaticallyRecoverInterruptedDisplayOperations);
        }

        if (journal is null || !journal.IsInProgress)
        {
            return new DisplayRecoveryCheckResult(
                DisplayRecoveryIssue.None,
                journal,
                null,
                string.Empty,
                configuration.AutomaticallyRecoverInterruptedDisplayOperations);
        }

        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["CorrelationId"] = journal.OperationId
        });

        var snapshot = await snapshotStore.LoadByIdAsync(journal.RollbackSnapshotId, cancellationToken);
        if (snapshot is null)
        {
            logger.LogWarning(
                "Interrupted display operation detected, but rollback snapshot {SnapshotId} is missing.",
                journal.RollbackSnapshotId);

            return new DisplayRecoveryCheckResult(
                DisplayRecoveryIssue.MissingRollbackSnapshot,
                journal,
                null,
                $"Interrupted display operation {journal.OperationId} cannot be recovered automatically because rollback snapshot {journal.RollbackSnapshotId} is missing. Open the logs and restore the desktop layout manually before running Couch Mode again.",
                configuration.AutomaticallyRecoverInterruptedDisplayOperations);
        }

        logger.LogWarning(
            "Interrupted display operation detected for rollback snapshot {SnapshotId}.",
            journal.RollbackSnapshotId);

        return new DisplayRecoveryCheckResult(
            DisplayRecoveryIssue.InterruptedOperation,
            journal,
            snapshot,
            $"Interrupted display operation detected from {journal.StartedAtUtc.LocalDateTime:g}. Restore the previous desktop configuration before running Couch Mode again.",
            configuration.AutomaticallyRecoverInterruptedDisplayOperations);
    }

    public async Task<DisplayRecoveryAttemptResult> RecoverAsync(CancellationToken cancellationToken = default)
    {
        var check = await CheckAsync(cancellationToken);
        if (check.Issue != DisplayRecoveryIssue.InterruptedOperation || check.Journal is null)
        {
            return new DisplayRecoveryAttemptResult(
                false,
                check.Journal,
                null,
                check.Message.Length == 0
                    ? "No interrupted display operation is waiting for recovery."
                    : check.Message);
        }

        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["CorrelationId"] = check.Journal.OperationId
        });

        logger.LogInformation("Starting interrupted display recovery for rollback snapshot {SnapshotId}.", check.Journal.RollbackSnapshotId);
        var result = await profileOrchestrator.ActivateDesktopModeAsync(cancellationToken);
        if (result.Succeeded)
        {
            logger.LogInformation("Interrupted display recovery completed successfully.");
            return new DisplayRecoveryAttemptResult(
                true,
                check.Journal,
                result,
                "Previous desktop configuration restored successfully.");
        }

        logger.LogError(
            "Interrupted display recovery failed. Keep the journal at {JournalPath} and review the latest logs.",
            paths.OperationJournalFilePath);

        return new DisplayRecoveryAttemptResult(
            false,
            check.Journal,
            result,
            $"Recovery failed: {result.DisplayResult.Message} Review {paths.OperationJournalFilePath} and the latest logs, then restore the desktop layout manually before retrying.");
    }
}

public sealed record DisplayRecoveryCheckResult(
    DisplayRecoveryIssue Issue,
    DisplayOperationJournal? Journal,
    DisplaySnapshot? Snapshot,
    string Message,
    bool AutomaticRecoveryConfigured);

public sealed record DisplayRecoveryAttemptResult(
    bool Succeeded,
    DisplayOperationJournal? Journal,
    ProfileActivationResult? Result,
    string Message);

public enum DisplayRecoveryIssue
{
    None = 0,
    InterruptedOperation = 1,
    CorruptedJournal = 2,
    MissingRollbackSnapshot = 3
}
