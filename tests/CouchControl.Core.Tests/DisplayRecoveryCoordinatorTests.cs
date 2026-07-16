using CouchControl.Core.Abstractions;
using CouchControl.Core.Models;
using CouchControl.Windows;
using CouchControl.Windows.Recovery;
using Microsoft.Extensions.Logging.Abstractions;

namespace CouchControl.Core.Tests;

public sealed class DisplayRecoveryCoordinatorTests
{
    [Fact]
    public async Task CheckAsync_DetectsCrashBeforeDisplaySwitching()
    {
        var journal = new DisplayOperationJournal(
            Guid.NewGuid(),
            DisplayOperationJournalTypes.ActivateCouch,
            DisplayOperationJournalStates.InProgress,
            "rollback-before",
            DateTimeOffset.UtcNow);
        var snapshot = CreateSnapshot("rollback-before");
        var coordinator = CreateCoordinator(
            new FakeJournalStore { Journal = journal },
            new FakeSnapshotStore { Snapshots = { [snapshot.SnapshotId] = snapshot } });

        var result = await coordinator.CheckAsync();

        Assert.Equal(DisplayRecoveryIssue.InterruptedOperation, result.Issue);
        Assert.Equal(journal.OperationId, result.Journal!.OperationId);
        Assert.Equal(snapshot.SnapshotId, result.Snapshot!.SnapshotId);
    }

    [Fact]
    public async Task CheckAsync_DetectsCrashAfterDisplaySwitching()
    {
        var journal = new DisplayOperationJournal(
            Guid.NewGuid(),
            DisplayOperationJournalTypes.ActivateCouch,
            DisplayOperationJournalStates.InProgress,
            "rollback-after",
            DateTimeOffset.UtcNow.AddMinutes(-2));
        var snapshot = CreateSnapshot("rollback-after");
        var snapshotStore = new FakeSnapshotStore
        {
            LastSnapshot = CreateSnapshot("stable-desktop")
        };
        snapshotStore.Snapshots[snapshot.SnapshotId] = snapshot;
        var coordinator = CreateCoordinator(new FakeJournalStore { Journal = journal }, snapshotStore);

        var result = await coordinator.CheckAsync();

        Assert.Equal(DisplayRecoveryIssue.InterruptedOperation, result.Issue);
        Assert.Equal("stable-desktop", snapshotStore.LastSnapshot!.SnapshotId);
        Assert.Equal("rollback-after", result.Snapshot!.SnapshotId);
    }

    [Fact]
    public async Task CheckAsync_IgnoresCompletedOperation()
    {
        var coordinator = CreateCoordinator(
            new FakeJournalStore
            {
                Journal = new DisplayOperationJournal(
                    Guid.NewGuid(),
                    DisplayOperationJournalTypes.ActivateCouch,
                    DisplayOperationJournalStates.Completed,
                    "rollback-complete",
                    DateTimeOffset.UtcNow)
            },
            new FakeSnapshotStore());

        var result = await coordinator.CheckAsync();

        Assert.Equal(DisplayRecoveryIssue.None, result.Issue);
    }

    [Fact]
    public async Task CheckAsync_ReportsCorruptedJournal()
    {
        var coordinator = CreateCoordinator(
            new FakeJournalStore
            {
                LoadException = new InvalidOperationException("bad json")
            },
            new FakeSnapshotStore());

        var result = await coordinator.CheckAsync();

        Assert.Equal(DisplayRecoveryIssue.CorruptedJournal, result.Issue);
        Assert.Contains("corrupted", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CheckAsync_ReportsMissingRollbackSnapshot()
    {
        var coordinator = CreateCoordinator(
            new FakeJournalStore
            {
                Journal = new DisplayOperationJournal(
                    Guid.NewGuid(),
                    DisplayOperationJournalTypes.ActivateCouch,
                    DisplayOperationJournalStates.InProgress,
                    "missing-snapshot",
                    DateTimeOffset.UtcNow)
            },
            new FakeSnapshotStore());

        var result = await coordinator.CheckAsync();

        Assert.Equal(DisplayRecoveryIssue.MissingRollbackSnapshot, result.Issue);
        Assert.Contains("missing", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RecoverAsync_ReturnsSuccessWhenDesktopRestoreSucceeds()
    {
        var journal = new DisplayOperationJournal(
            Guid.NewGuid(),
            DisplayOperationJournalTypes.ActivateCouch,
            DisplayOperationJournalStates.InProgress,
            "rollback-success",
            DateTimeOffset.UtcNow);
        var snapshot = CreateSnapshot("rollback-success");
        var orchestrator = new FakeProfileOrchestrator
        {
            Result = new ProfileActivationResult(
                AgentMode.Desktop,
                ProfileActivationStatus.Success,
                OperationResult.Success("Desktop restored"))
        };
        var coordinator = CreateCoordinator(
            new FakeJournalStore { Journal = journal },
            new FakeSnapshotStore { Snapshots = { [snapshot.SnapshotId] = snapshot } },
            orchestrator);

        var result = await coordinator.RecoverAsync();

        Assert.True(result.Succeeded);
        Assert.True(orchestrator.ActivateDesktopCalled);
    }

    [Fact]
    public async Task RecoverAsync_PreservesJournalWhenDesktopRestoreFails()
    {
        var journal = new DisplayOperationJournal(
            Guid.NewGuid(),
            DisplayOperationJournalTypes.ActivateCouch,
            DisplayOperationJournalStates.InProgress,
            "rollback-failure",
            DateTimeOffset.UtcNow);
        var snapshot = CreateSnapshot("rollback-failure");
        var journalStore = new FakeJournalStore { Journal = journal };
        var orchestrator = new FakeProfileOrchestrator
        {
            Result = new ProfileActivationResult(
                AgentMode.Desktop,
                ProfileActivationStatus.Failure,
                OperationResult.Failure("restore failed", "display_restore_failed"))
        };
        var coordinator = CreateCoordinator(
            journalStore,
            new FakeSnapshotStore { Snapshots = { [snapshot.SnapshotId] = snapshot } },
            orchestrator);

        var result = await coordinator.RecoverAsync();

        Assert.False(result.Succeeded);
        Assert.Same(journal, journalStore.Journal);
    }

    private static DisplayRecoveryCoordinator CreateCoordinator(
        FakeJournalStore journalStore,
        FakeSnapshotStore snapshotStore,
        FakeProfileOrchestrator? profileOrchestrator = null)
    {
        return new DisplayRecoveryCoordinator(
            new FakeConfigurationStore(new AgentConfiguration()),
            journalStore,
            snapshotStore,
            profileOrchestrator ?? new FakeProfileOrchestrator(),
            new CouchControlPaths(Path.Combine(Path.GetTempPath(), "CouchControlRecoveryTests")),
            NullLogger<DisplayRecoveryCoordinator>.Instance);
    }

    private static DisplaySnapshot CreateSnapshot(string snapshotId) =>
        new(
            snapshotId,
            DateTimeOffset.UtcNow,
            [
                new DisplayDevice(
                    new DisplayIdentifier(snapshotId),
                    "Monitor",
                    true,
                    true,
                    new DisplayMode(1920, 1080, 60))
            ],
            [
                new DisplayPathSnapshot(
                    new DisplayIdentifier(snapshotId),
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

    private sealed class FakeConfigurationStore(AgentConfiguration configuration) : IAgentConfigurationStore
    {
        public Task<AgentConfiguration> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(configuration);

        public Task SaveAsync(AgentConfiguration configuration, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeJournalStore : IDisplayOperationJournalStore
    {
        public DisplayOperationJournal? Journal { get; set; }

        public Exception? LoadException { get; init; }

        public Task<DisplayOperationJournal?> LoadAsync(CancellationToken cancellationToken = default)
        {
            if (LoadException is not null)
            {
                throw LoadException;
            }

            return Task.FromResult(Journal);
        }

        public Task SaveAsync(DisplayOperationJournal journal, CancellationToken cancellationToken = default)
        {
            Journal = journal;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSnapshotStore : IDisplaySnapshotStore
    {
        public Dictionary<string, DisplaySnapshot> Snapshots { get; } = new(StringComparer.Ordinal);

        public DisplaySnapshot? LastSnapshot { get; set; }

        public Task SaveAsync(DisplaySnapshot snapshot, CancellationToken cancellationToken = default)
        {
            LastSnapshot = snapshot;
            Snapshots[snapshot.SnapshotId] = snapshot;
            return Task.CompletedTask;
        }

        public Task SavePendingAsync(DisplaySnapshot snapshot, CancellationToken cancellationToken = default)
        {
            Snapshots[snapshot.SnapshotId] = snapshot;
            return Task.CompletedTask;
        }

        public Task<DisplaySnapshot?> LoadLastDesktopSnapshotAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(LastSnapshot);

        public Task<DisplaySnapshot?> LoadByIdAsync(string snapshotId, CancellationToken cancellationToken = default)
        {
            Snapshots.TryGetValue(snapshotId, out var snapshot);
            return Task.FromResult(snapshot);
        }

        public Task PromotePendingAsync(string snapshotId, CancellationToken cancellationToken = default)
        {
            Snapshots.TryGetValue(snapshotId, out var snapshot);
            LastSnapshot = snapshot;
            return Task.CompletedTask;
        }

        public Task ClearAsync(CancellationToken cancellationToken = default)
        {
            LastSnapshot = null;
            Snapshots.Clear();
            return Task.CompletedTask;
        }
    }

    private sealed class FakeProfileOrchestrator : IProfileOrchestrator
    {
        public ProfileActivationResult Result { get; init; } = new(
            AgentMode.Desktop,
            ProfileActivationStatus.Success,
            OperationResult.Success("Desktop restored"));

        public bool ActivateDesktopCalled { get; private set; }

        public Task<ProfileActivationResult> ActivateCouchModeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Result);

        public Task<ProfileActivationResult> ActivateDesktopModeAsync(CancellationToken cancellationToken = default)
        {
            ActivateDesktopCalled = true;
            return Task.FromResult(Result);
        }

        public AgentOperationStatus GetStatus() => AgentOperationStatus.Idle();
    }
}
