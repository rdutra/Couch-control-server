using CouchControl.Core.Abstractions;
using CouchControl.Core.Models;
using CouchControl.Core.Orchestration;
using Microsoft.Extensions.Logging.Abstractions;

namespace CouchControl.Core.Tests;

public sealed class ProfileOrchestratorTests
{
    [Fact]
    public async Task ActivateCouchModeAsync_ReturnsSuccessAndUpdatesStatus()
    {
        var targetDisplay = CreateDisplayDevice(@"\\?\DISPLAY#SAM0F8C#1", "Samsung TV", isActive: false);
        var snapshot = CreateSnapshot(targetDisplay);
        var displayManager = new FakeDisplayManager
        {
            ConnectedDisplays = [targetDisplay],
            SnapshotToCapture = snapshot,
            ActivateOnlyResult = OperationResult.Success("switched")
        };
        var configurationStore = new FakeConfigurationStore(CreateConfiguration(targetDisplay) with
        {
            LaunchSteamAutomatically = false
        });
        var snapshotStore = new FakeSnapshotStore { LastSnapshot = snapshot };
        var orchestrator = CreateOrchestrator(configurationStore, displayManager, snapshotStore);

        var result = await orchestrator.ActivateCouchModeAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(ProfileActivationStatus.Success, result.Status);
        Assert.Null(snapshotStore.SavedSnapshot);
        Assert.Equal(snapshot, await snapshotStore.LoadByIdAsync(snapshot.SnapshotId));
        Assert.NotNull(displayManager.ActivateOnlyCall);
        Assert.Equal(targetDisplay.Identifier, displayManager.ActivateOnlyCall!.Display);

        var status = orchestrator.GetStatus();
        Assert.Equal(AgentOperationState.Succeeded, status.State);
        Assert.Equal(ProfileOperationType.None, status.CurrentOperation);
        Assert.Equal(ProfileOperationStep.Completed, status.CurrentStep);
        Assert.Equal(AgentMode.Couch, status.CurrentMode);
        Assert.NotNull(status.OperationId);
        Assert.NotNull(status.OperationStartedAtUtc);
        Assert.NotNull(status.OperationCompletedAtUtc);
        Assert.Null(status.LastError);
    }

    [Fact]
    public async Task ActivateCouchModeAsync_ReturnsPartialSuccessWhenSteamIsUnavailable()
    {
        var targetDisplay = CreateDisplayDevice(@"\\?\DISPLAY#SAM0F8C#1", "Samsung TV", isActive: false);
        var displayManager = new FakeDisplayManager
        {
            ConnectedDisplays = [targetDisplay],
            SnapshotToCapture = CreateSnapshot(targetDisplay),
            ActivateOnlyResult = OperationResult.Success("switched")
        };
        var configurationStore = new FakeConfigurationStore(CreateConfiguration(targetDisplay));
        var orchestrator = CreateOrchestrator(
            configurationStore,
            displayManager,
            new FakeSnapshotStore { LastSnapshot = CreateSnapshot("manual-desktop", targetDisplay) },
            steamLauncher: new FakeSteamLauncher { IsInstalledResult = false });

        var result = await orchestrator.ActivateCouchModeAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(ProfileActivationStatus.PartialSuccess, result.Status);
        Assert.NotNull(result.SteamResult);
        Assert.True(result.SteamResult!.IsPartialSuccess);
        Assert.Equal(AgentOperationState.PartiallySucceeded, orchestrator.GetStatus().State);
        Assert.Equal(AgentMode.Couch, orchestrator.GetStatus().CurrentMode);
    }

    [Fact]
    public async Task ActivateCouchModeAsync_ReturnsPartialSuccessWhenSteamLaunchFails()
    {
        var targetDisplay = CreateDisplayDevice(@"\\?\DISPLAY#SAM0F8C#1", "Samsung TV", isActive: false);
        var displayManager = new FakeDisplayManager
        {
            ConnectedDisplays = [targetDisplay],
            SnapshotToCapture = CreateSnapshot(targetDisplay),
            ActivateOnlyResult = OperationResult.Success("switched")
        };
        var configurationStore = new FakeConfigurationStore(CreateConfiguration(targetDisplay));
        var snapshotStore = new FakeSnapshotStore { LastSnapshot = CreateSnapshot("manual-desktop", targetDisplay) };
        var steamLauncher = new FakeSteamLauncher
        {
            IsInstalledResult = true,
            StartResult = OperationResult.Failure("launch failed", "steam_launch_failed")
        };
        var orchestrator = CreateOrchestrator(configurationStore, displayManager, snapshotStore, steamLauncher);

        var result = await orchestrator.ActivateCouchModeAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(ProfileActivationStatus.PartialSuccess, result.Status);
        Assert.NotNull(result.SteamResult);
        Assert.False(result.SteamResult!.Succeeded);
        Assert.Equal(displayManager.SnapshotToCapture, result.Snapshot);
        Assert.Null(displayManager.RestoredSnapshot);
    }

    [Fact]
    public async Task ActivateCouchModeAsync_LaunchesSelectedHeroicConsoleMode()
    {
        var targetDisplay = CreateDisplayDevice(@"\\?\DISPLAY#SAM0F8C#1", "Samsung TV", isActive: false);
        var displayManager = new FakeDisplayManager
        {
            ConnectedDisplays = [targetDisplay],
            SnapshotToCapture = CreateSnapshot(targetDisplay),
            ActivateOnlyResult = OperationResult.Success("switched")
        };
        var configurationStore = new FakeConfigurationStore(CreateConfiguration(targetDisplay) with
        {
            CouchLauncher = CouchLauncher.HeroicConsole,
            LaunchSteamAutomatically = false
        });
        var launcher = new FakeSteamLauncher { IsHeroicInstalledResult = true };
        var orchestrator = CreateOrchestrator(
            configurationStore,
            displayManager,
            new FakeSnapshotStore { LastSnapshot = CreateSnapshot("manual-desktop", targetDisplay) },
            launcher);

        var result = await orchestrator.ActivateCouchModeAsync();

        Assert.True(result.Succeeded);
        Assert.True(launcher.HeroicLaunchCalled);
    }

    [Fact]
    public async Task ActivateCouchModeAsync_FailsWhenConfigurationIsInvalid()
    {
        var displayManager = new FakeDisplayManager
        {
            SnapshotToCapture = CreateSnapshot(CreateDisplayDevice(@"\\?\DISPLAY#SAM0F8C#1", "Samsung TV", isActive: false))
        };
        var configurationStore = new FakeConfigurationStore(new AgentConfiguration
        {
            AgentName = "",
            CouchDisplayIdentifier = new DisplayIdentifier("TV")
        });
        var orchestrator = CreateOrchestrator(
            configurationStore,
            displayManager,
            new FakeSnapshotStore());

        var result = await orchestrator.ActivateCouchModeAsync();

        Assert.False(result.Succeeded);
        Assert.Equal(ProfileActivationStatus.Failure, result.Status);
        Assert.Equal("agent_name_missing", result.DisplayResult.ErrorCode);
        Assert.Null(displayManager.ActivateOnlyCall);
    }

    [Fact]
    public async Task ActivateCouchModeAsync_FailsWhenDisplayIsMissingFromConfiguration()
    {
        var configurationStore = new FakeConfigurationStore(new AgentConfiguration());
        var orchestrator = CreateOrchestrator(
            configurationStore,
            new FakeDisplayManager
            {
                SnapshotToCapture = CreateSnapshot(CreateDisplayDevice(@"\\?\DISPLAY#SAM0F8C#1", "Samsung TV", isActive: false))
            },
            new FakeSnapshotStore());

        var result = await orchestrator.ActivateCouchModeAsync();

        Assert.False(result.Succeeded);
        Assert.Equal("couch_display_missing", result.DisplayResult.ErrorCode);
    }

    [Fact]
    public async Task ActivateCouchModeAsync_FailsWhenNoManualDesktopSnapshotExists()
    {
        var targetDisplay = CreateDisplayDevice(@"\\?\DISPLAY#SAM0F8C#1", "Samsung TV", isActive: false);
        var displayManager = new FakeDisplayManager
        {
            ConnectedDisplays = [targetDisplay],
            SnapshotToCapture = CreateSnapshot(targetDisplay),
            ActivateOnlyResult = OperationResult.Success("switched")
        };
        var configurationStore = new FakeConfigurationStore(CreateConfiguration(targetDisplay));
        var orchestrator = CreateOrchestrator(
            configurationStore,
            displayManager,
            new FakeSnapshotStore());

        var result = await orchestrator.ActivateCouchModeAsync();

        Assert.False(result.Succeeded);
        Assert.Equal("desktop_snapshot_missing", result.DisplayResult.ErrorCode);
        Assert.Null(displayManager.ActivateOnlyCall);
    }

    [Fact]
    public async Task ActivateCouchModeAsync_FailsWhenConfiguredTvIsNotConnected()
    {
        var targetDisplay = CreateDisplayDevice(@"\\?\DISPLAY#SAM0F8C#1", "Samsung TV", isActive: false);
        var displayManager = new FakeDisplayManager
        {
            ConnectedDisplays = [],
            SnapshotToCapture = CreateSnapshot(targetDisplay)
        };
        var configurationStore = new FakeConfigurationStore(CreateConfiguration(targetDisplay));
        var orchestrator = CreateOrchestrator(
            configurationStore,
            displayManager,
            new FakeSnapshotStore { LastSnapshot = CreateSnapshot("manual-desktop", targetDisplay) });

        var result = await orchestrator.ActivateCouchModeAsync();

        Assert.False(result.Succeeded);
        Assert.Equal("couch_display_not_connected", result.DisplayResult.ErrorCode);
        Assert.Equal(AgentOperationState.Failed, orchestrator.GetStatus().State);
    }

    [Fact]
    public async Task ActivateCouchModeAsync_RunsTvPreparationAndRetriesDisplayMatching()
    {
        var targetDisplay = CreateDisplayDevice(@"\\?\DISPLAY#SAM0F8C#1", "Samsung TV", isActive: false);
        var displayManager = new FakeDisplayManager
        {
            ConnectedDisplaysSequence = new Queue<IReadOnlyList<DisplayDevice>>(new[]
            {
                Array.Empty<DisplayDevice>(),
                (IReadOnlyList<DisplayDevice>)[targetDisplay]
            }),
            SnapshotToCapture = CreateSnapshot(targetDisplay),
            ActivateOnlyResult = OperationResult.Success("switched"),
            PrepareForCouchModeResult = OperationResult.Success("prepared")
        };
        var configurationStore = new FakeConfigurationStore(CreateConfiguration(targetDisplay) with
        {
            TvPreparationCommand = "cec-switch-tv-input",
            LaunchSteamAutomatically = false
        });
        var orchestrator = CreateOrchestrator(
            configurationStore,
            displayManager,
            new FakeSnapshotStore { LastSnapshot = CreateSnapshot("manual-desktop", targetDisplay) });

        var result = await orchestrator.ActivateCouchModeAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(2, displayManager.PrepareForCouchModeCallCount);
        Assert.Equal(2, displayManager.GetDisplaysCallCount);
    }

    [Fact]
    public async Task ActivateCouchModeAsync_RunsTvPreparationAfterDisplayActivationWhenDisplayIsAlreadyConnected()
    {
        var targetDisplay = CreateDisplayDevice(@"\\?\DISPLAY#SAM0F8C#1", "Samsung TV", isActive: false);
        var displayManager = new FakeDisplayManager
        {
            ConnectedDisplays = [targetDisplay],
            SnapshotToCapture = CreateSnapshot(targetDisplay),
            ActivateOnlyResult = OperationResult.Success("switched"),
            PrepareForCouchModeResult = OperationResult.Success("prepared")
        };
        var configurationStore = new FakeConfigurationStore(CreateConfiguration(targetDisplay) with
        {
            TvPreparationCommand = "cec-switch-tv-input",
            LaunchSteamAutomatically = false
        });
        var orchestrator = CreateOrchestrator(
            configurationStore,
            displayManager,
            new FakeSnapshotStore { LastSnapshot = CreateSnapshot("manual-desktop", targetDisplay) });

        var result = await orchestrator.ActivateCouchModeAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(1, displayManager.PrepareForCouchModeCallCount);
        Assert.Equal(
            ["get-displays", "capture-snapshot", "activate-only", "prepare-tv"],
            displayManager.Operations);
    }

    [Fact]
    public async Task ActivateCouchModeAsync_FailsWhenTvPreparationFails()
    {
        var targetDisplay = CreateDisplayDevice(@"\\?\DISPLAY#SAM0F8C#1", "Samsung TV", isActive: false);
        var displayManager = new FakeDisplayManager
        {
            ConnectedDisplays = [],
            SnapshotToCapture = CreateSnapshot(targetDisplay),
            PrepareForCouchModeResult = OperationResult.Failure("prep failed", "tv_preparation_command_failed")
        };
        var configurationStore = new FakeConfigurationStore(CreateConfiguration(targetDisplay) with
        {
            TvPreparationCommand = "cec-switch-tv-input"
        });
        var orchestrator = CreateOrchestrator(configurationStore, displayManager, new FakeSnapshotStore());

        var result = await orchestrator.ActivateCouchModeAsync();

        Assert.False(result.Succeeded);
        Assert.Equal("tv_preparation_command_failed", result.DisplayResult.ErrorCode);
        Assert.Equal(1, displayManager.PrepareForCouchModeCallCount);
    }

    [Fact]
    public async Task ActivateCouchModeAsync_RetriesActivationAfterVerificationFailureWhenTvPreparationIsConfigured()
    {
        var targetDisplay = CreateDisplayDevice(@"\\?\DISPLAY#SAM0F8C#1", "Samsung TV", isActive: false);
        var displayManager = new FakeDisplayManager
        {
            ConnectedDisplays = [targetDisplay],
            SnapshotToCapture = CreateSnapshot(targetDisplay),
            ActivateOnlyResults = new Queue<OperationResult>(new[]
            {
                OperationResult.Failure("verification failed", "display_switch_verification_failed"),
                OperationResult.Success("switched")
            }),
            PrepareForCouchModeResult = OperationResult.Success("prepared")
        };
        var configurationStore = new FakeConfigurationStore(CreateConfiguration(targetDisplay) with
        {
            TvPreparationCommand = "cec-switch-tv-input",
            LaunchSteamAutomatically = false
        });
        var orchestrator = CreateOrchestrator(
            configurationStore,
            displayManager,
            new FakeSnapshotStore { LastSnapshot = CreateSnapshot("manual-desktop", targetDisplay) });

        var result = await orchestrator.ActivateCouchModeAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(2, displayManager.ActivateOnlyCallCount);
        Assert.Equal(2, displayManager.PrepareForCouchModeCallCount);
    }

    [Fact]
    public async Task ActivateCouchModeAsync_RetriesActivationAfterInactiveTargetFailureWhenTvPreparationIsConfigured()
    {
        var targetDisplay = CreateDisplayDevice(@"\\?\DISPLAY#SAM0F8C#1", "Samsung TV", isActive: false);
        var displayManager = new FakeDisplayManager
        {
            ConnectedDisplays = [targetDisplay],
            SnapshotToCapture = CreateSnapshot(targetDisplay),
            ActivateOnlyResults = new Queue<OperationResult>(new[]
            {
                OperationResult.Failure("tv inactive", "display_target_inactive_after_extend"),
                OperationResult.Success("switched")
            }),
            PrepareForCouchModeResult = OperationResult.Success("prepared")
        };
        var configurationStore = new FakeConfigurationStore(CreateConfiguration(targetDisplay) with
        {
            TvPreparationCommand = "cec-switch-tv-input",
            LaunchSteamAutomatically = false
        });
        var orchestrator = CreateOrchestrator(
            configurationStore,
            displayManager,
            new FakeSnapshotStore { LastSnapshot = CreateSnapshot("manual-desktop", targetDisplay) });

        var result = await orchestrator.ActivateCouchModeAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(2, displayManager.ActivateOnlyCallCount);
        Assert.Equal(2, displayManager.PrepareForCouchModeCallCount);
    }

    [Fact]
    public async Task ActivateCouchModeAsync_AttemptsRollbackWhenDisplayActivationFails()
    {
        var targetDisplay = CreateDisplayDevice(@"\\?\DISPLAY#SAM0F8C#1", "Samsung TV", isActive: false);
        var snapshot = CreateSnapshot(targetDisplay);
        var displayManager = new FakeDisplayManager
        {
            ConnectedDisplays = [targetDisplay],
            SnapshotToCapture = snapshot,
            ActivateOnlyResult = OperationResult.Failure("switch failed", "display_switch_apply_failed"),
            RestoreSnapshotResult = OperationResult.Success("restored")
        };
        var configurationStore = new FakeConfigurationStore(CreateConfiguration(targetDisplay));
        var orchestrator = CreateOrchestrator(
            configurationStore,
            displayManager,
            new FakeSnapshotStore { LastSnapshot = CreateSnapshot("manual-desktop", targetDisplay) });

        var result = await orchestrator.ActivateCouchModeAsync();

        Assert.False(result.Succeeded);
        Assert.NotNull(result.DisplayResult.RollbackResult);
        Assert.True(result.DisplayResult.RollbackResult!.Succeeded);
        Assert.Equal(snapshot, displayManager.RestoredSnapshot);
    }

    [Fact]
    public async Task ActivateCouchModeAsync_FailsWhenInterruptedRecoveryIsPending()
    {
        var targetDisplay = CreateDisplayDevice(@"\\?\DISPLAY#SAM0F8C#1", "Samsung TV", isActive: false);
        var snapshotStore = new FakeSnapshotStore();
        var journalStore = new FakeJournalStore
        {
            Journal = new DisplayOperationJournal(
                Guid.NewGuid(),
                DisplayOperationJournalTypes.ActivateCouch,
                DisplayOperationJournalStates.InProgress,
                "snapshot-pending",
                DateTimeOffset.UtcNow)
        };
        var orchestrator = CreateOrchestrator(
            new FakeConfigurationStore(CreateConfiguration(targetDisplay)),
            new FakeDisplayManager
            {
                ConnectedDisplays = [targetDisplay],
                SnapshotToCapture = CreateSnapshot(targetDisplay)
            },
            snapshotStore,
            journalStore: journalStore);

        var result = await orchestrator.ActivateCouchModeAsync();

        Assert.False(result.Succeeded);
        Assert.Equal("display_recovery_pending", result.DisplayResult.ErrorCode);
        Assert.Null(snapshotStore.SavedSnapshot);
    }

    [Fact]
    public async Task ActivateCouchModeAsync_AttemptsRollbackWhenDisplayActivationThrows()
    {
        var targetDisplay = CreateDisplayDevice(@"\\?\DISPLAY#SAM0F8C#1", "Samsung TV", isActive: false);
        var snapshot = CreateSnapshot(targetDisplay);
        var displayManager = new FakeDisplayManager
        {
            ConnectedDisplays = [targetDisplay],
            SnapshotToCapture = snapshot,
            ActivateOnlyException = new InvalidOperationException("boom"),
            RestoreSnapshotResult = OperationResult.Success("restored")
        };
        var configurationStore = new FakeConfigurationStore(CreateConfiguration(targetDisplay));
        var orchestrator = CreateOrchestrator(
            configurationStore,
            displayManager,
            new FakeSnapshotStore { LastSnapshot = CreateSnapshot("manual-desktop", targetDisplay) });

        var result = await orchestrator.ActivateCouchModeAsync();

        Assert.False(result.Succeeded);
        Assert.Equal("display_activation_exception", result.DisplayResult.ErrorCode);
        Assert.NotNull(result.DisplayResult.RollbackResult);
        Assert.Equal(snapshot, displayManager.RestoredSnapshot);
    }

    [Fact]
    public async Task ActivateCouchModeAsync_RejectsConcurrentOperations()
    {
        var targetDisplay = CreateDisplayDevice(@"\\?\DISPLAY#SAM0F8C#1", "Samsung TV", isActive: false);
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var configurationStore = new FakeConfigurationStore(CreateConfiguration(targetDisplay))
        {
            OnLoadAsync = async cancellationToken =>
            {
                gate.SetResult(true);
                await resume.Task.WaitAsync(cancellationToken);
            }
        };
        var displayManager = new FakeDisplayManager
        {
            ConnectedDisplays = [targetDisplay],
            SnapshotToCapture = CreateSnapshot(targetDisplay),
            ActivateOnlyResult = OperationResult.Success("switched")
        };
        var orchestrator = CreateOrchestrator(
            configurationStore,
            displayManager,
            new FakeSnapshotStore(),
            steamLauncher: new FakeSteamLauncher { IsInstalledResult = false });

        var activeOperation = orchestrator.ActivateCouchModeAsync();
        await gate.Task.WaitAsync(CancellationToken.None);

        var rejected = await orchestrator.ActivateDesktopModeAsync();

        Assert.False(rejected.Succeeded);
        Assert.Equal("operation_in_progress", rejected.DisplayResult.ErrorCode);
        Assert.Equal(ProfileOperationType.ActivateCouchMode, orchestrator.GetStatus().CurrentOperation);

        resume.SetResult(true);
        await activeOperation;
    }

    [Fact]
    public async Task ActivateCouchModeAsync_PropagatesCancellationAndUpdatesStatus()
    {
        var targetDisplay = CreateDisplayDevice(@"\\?\DISPLAY#SAM0F8C#1", "Samsung TV", isActive: false);
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var configurationStore = new FakeConfigurationStore(CreateConfiguration(targetDisplay))
        {
            OnLoadAsync = async cancellationToken =>
            {
                gate.SetResult(true);
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
        };
        var orchestrator = CreateOrchestrator(
            configurationStore,
            new FakeDisplayManager
            {
                SnapshotToCapture = CreateSnapshot(targetDisplay)
            },
            new FakeSnapshotStore());
        using var cancellationTokenSource = new CancellationTokenSource();

        var task = orchestrator.ActivateCouchModeAsync(cancellationTokenSource.Token);
        await gate.Task.WaitAsync(CancellationToken.None);
        cancellationTokenSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.Equal(AgentOperationState.Canceled, orchestrator.GetStatus().State);
    }

    [Fact]
    public async Task ActivateDesktopModeAsync_ReturnsFailureWhenNoSnapshotExists()
    {
        var orchestrator = CreateOrchestrator(
            new FakeConfigurationStore(new AgentConfiguration()),
            new FakeDisplayManager
            {
                SnapshotToCapture = CreateSnapshot(CreateDisplayDevice(@"\\?\DISPLAY#SAM0F8C#1", "Samsung TV", isActive: false))
            },
            new FakeSnapshotStore());

        var result = await orchestrator.ActivateDesktopModeAsync();

        Assert.False(result.Succeeded);
        Assert.Equal("desktop_snapshot_missing", result.DisplayResult.ErrorCode);
    }

    [Fact]
    public async Task ActivateDesktopModeAsync_ReturnsSuccessForExactRestore()
    {
        var targetDisplay = CreateDisplayDevice(@"\\?\DISPLAY#SAM0F8C#1", "Samsung TV", isActive: true);
        var snapshot = CreateSnapshot(targetDisplay);
        var snapshotStore = new FakeSnapshotStore { LastSnapshot = snapshot };
        var displayManager = new FakeDisplayManager
        {
            SnapshotToCapture = snapshot,
            RestoreSnapshotResult = OperationResult.Success("Desktop restored", outcome: "exact")
        };
        var orchestrator = CreateOrchestrator(
            new FakeConfigurationStore(CreateConfiguration(targetDisplay)),
            displayManager,
            snapshotStore);

        var result = await orchestrator.ActivateDesktopModeAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(ProfileActivationStatus.Success, result.Status);
        Assert.Equal(snapshot, displayManager.RestoredSnapshot);
        Assert.Equal(AgentMode.Desktop, orchestrator.GetStatus().CurrentMode);
    }

    [Fact]
    public async Task ActivateDesktopModeAsync_ReturnsPartialSuccessForBestEffortRestore()
    {
        var targetDisplay = CreateDisplayDevice(@"\\?\DISPLAY#SAM0F8C#1", "Samsung TV", isActive: true);
        var snapshot = CreateSnapshot(targetDisplay);
        var snapshotStore = new FakeSnapshotStore { LastSnapshot = snapshot };
        var displayManager = new FakeDisplayManager
        {
            SnapshotToCapture = snapshot,
            RestoreSnapshotResult = OperationResult.PartialSuccess(
                "Desktop restored with best-effort topology",
                outcome: "best_effort")
        };
        var orchestrator = CreateOrchestrator(
            new FakeConfigurationStore(CreateConfiguration(targetDisplay)),
            displayManager,
            snapshotStore);

        var result = await orchestrator.ActivateDesktopModeAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(ProfileActivationStatus.PartialSuccess, result.Status);
        Assert.Equal(AgentOperationState.PartiallySucceeded, orchestrator.GetStatus().State);
    }

    [Fact]
    public async Task ActivateDesktopModeAsync_ReturnsPartialSuccessForFallbackRestore()
    {
        var targetDisplay = CreateDisplayDevice(@"\\?\DISPLAY#SAM0F8C#1", "Samsung TV", isActive: true);
        var snapshot = CreateSnapshot(targetDisplay);
        var snapshotStore = new FakeSnapshotStore { LastSnapshot = snapshot };
        var displayManager = new FakeDisplayManager
        {
            SnapshotToCapture = snapshot,
            RestoreSnapshotResult = OperationResult.PartialSuccess(
                "Desktop Mode restored with emergency fallback",
                outcome: "fallback")
        };
        var orchestrator = CreateOrchestrator(
            new FakeConfigurationStore(CreateConfiguration(targetDisplay)),
            displayManager,
            snapshotStore);

        var result = await orchestrator.ActivateDesktopModeAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(ProfileActivationStatus.PartialSuccess, result.Status);
        Assert.Equal("fallback", result.DisplayResult.Outcome);
    }

    [Fact]
    public async Task ActivateDesktopModeAsync_ReturnsFailureWhenRestoreFails()
    {
        var targetDisplay = CreateDisplayDevice(@"\\?\DISPLAY#SAM0F8C#1", "Samsung TV", isActive: true);
        var snapshot = CreateSnapshot(targetDisplay);
        var snapshotStore = new FakeSnapshotStore { LastSnapshot = snapshot };
        var displayManager = new FakeDisplayManager
        {
            SnapshotToCapture = snapshot,
            RestoreSnapshotResult = OperationResult.Failure("restore failed", "display_restore_failed")
        };
        var orchestrator = CreateOrchestrator(
            new FakeConfigurationStore(CreateConfiguration(targetDisplay)),
            displayManager,
            snapshotStore);

        var result = await orchestrator.ActivateDesktopModeAsync();

        Assert.False(result.Succeeded);
        Assert.Equal(ProfileActivationStatus.Failure, result.Status);
        Assert.Equal(AgentOperationState.Failed, orchestrator.GetStatus().State);
    }

    [Fact]
    public async Task ActivateDesktopModeAsync_ConvertsExceptionsToSafeResults()
    {
        var targetDisplay = CreateDisplayDevice(@"\\?\DISPLAY#SAM0F8C#1", "Samsung TV", isActive: true);
        var snapshot = CreateSnapshot(targetDisplay);
        var snapshotStore = new FakeSnapshotStore { LastSnapshot = snapshot };
        var displayManager = new FakeDisplayManager
        {
            SnapshotToCapture = snapshot,
            RestoreSnapshotException = new InvalidOperationException("restore blew up")
        };
        var orchestrator = CreateOrchestrator(
            new FakeConfigurationStore(CreateConfiguration(targetDisplay)),
            displayManager,
            snapshotStore);

        var result = await orchestrator.ActivateDesktopModeAsync();

        Assert.False(result.Succeeded);
        Assert.Equal("desktop_mode_exception", result.DisplayResult.ErrorCode);
        Assert.Equal("restore blew up", orchestrator.GetStatus().LastError);
    }

    [Fact]
    public async Task ActivateDesktopModeAsync_UsesPendingRecoverySnapshotAndMarksJournalRecovered()
    {
        var targetDisplay = CreateDisplayDevice(@"\\?\DISPLAY#SAM0F8C#1", "Samsung TV", isActive: true);
        var rollbackSnapshot = CreateSnapshot("rollback-1", targetDisplay);
        var snapshotStore = new FakeSnapshotStore();
        await snapshotStore.SavePendingAsync(rollbackSnapshot);
        var journalStore = new FakeJournalStore
        {
            Journal = new DisplayOperationJournal(
                Guid.NewGuid(),
                DisplayOperationJournalTypes.ActivateCouch,
                DisplayOperationJournalStates.InProgress,
                rollbackSnapshot.SnapshotId,
                DateTimeOffset.UtcNow)
        };
        var orchestrator = CreateOrchestrator(
            new FakeConfigurationStore(CreateConfiguration(targetDisplay)),
            new FakeDisplayManager
            {
                SnapshotToCapture = rollbackSnapshot,
                RestoreSnapshotResult = OperationResult.Success("Desktop restored")
            },
            snapshotStore,
            journalStore: journalStore);

        var result = await orchestrator.ActivateDesktopModeAsync();

        Assert.True(result.Succeeded);
        Assert.True(journalStore.Journal!.IsRecovered);
        Assert.Null(snapshotStore.SavedSnapshot);
    }

    [Fact]
    public async Task ActivateDesktopModeAsync_PreservesJournalWhenRecoveryFails()
    {
        var targetDisplay = CreateDisplayDevice(@"\\?\DISPLAY#SAM0F8C#1", "Samsung TV", isActive: true);
        var rollbackSnapshot = CreateSnapshot("rollback-2", targetDisplay);
        var snapshotStore = new FakeSnapshotStore();
        await snapshotStore.SavePendingAsync(rollbackSnapshot);
        var journal = new DisplayOperationJournal(
            Guid.NewGuid(),
            DisplayOperationJournalTypes.ActivateCouch,
            DisplayOperationJournalStates.InProgress,
            rollbackSnapshot.SnapshotId,
            DateTimeOffset.UtcNow);
        var journalStore = new FakeJournalStore { Journal = journal };
        var orchestrator = CreateOrchestrator(
            new FakeConfigurationStore(CreateConfiguration(targetDisplay)),
            new FakeDisplayManager
            {
                SnapshotToCapture = rollbackSnapshot,
                RestoreSnapshotResult = OperationResult.Failure("restore failed", "display_restore_failed")
            },
            snapshotStore,
            journalStore: journalStore);

        var result = await orchestrator.ActivateDesktopModeAsync();

        Assert.False(result.Succeeded);
        Assert.Same(journal, journalStore.Journal);
        Assert.True(journalStore.Journal!.IsInProgress);
    }

    private static ProfileOrchestrator CreateOrchestrator(
        FakeConfigurationStore configurationStore,
        FakeDisplayManager displayManager,
        FakeSnapshotStore snapshotStore,
        FakeSteamLauncher? steamLauncher = null,
        FakeJournalStore? journalStore = null)
    {
        return new ProfileOrchestrator(
            configurationStore,
            displayManager,
            new DisplayMatchingService(),
            steamLauncher ?? new FakeSteamLauncher(),
            new FakeModeAutomationService(),
            snapshotStore,
            journalStore ?? new FakeJournalStore(),
            NullLogger<ProfileOrchestrator>.Instance);
    }

    private static AgentConfiguration CreateConfiguration(DisplayDevice targetDisplay)
    {
        var parsed = DisplayMatchingService.ParseDevicePath(targetDisplay.DevicePath);
        return new AgentConfiguration
        {
            CouchDisplayIdentifier = targetDisplay.Identifier,
            CouchDisplayIdentity = new CouchDisplayIdentity(
                targetDisplay.DevicePath ?? targetDisplay.Identifier.Value,
                targetDisplay.FriendlyName,
                parsed?.Manufacturer ?? "SAM",
                parsed?.ProductCode ?? "0F8C",
                parsed?.SerialOrInstance ?? "1",
                targetDisplay.AdapterLuid ?? "00000000:00000001",
                targetDisplay.TargetId ?? 1)
        };
    }

    private static DisplayDevice CreateDisplayDevice(string id, string name, bool isActive) =>
        new(
            new DisplayIdentifier(id),
            name,
            isActive,
            isActive,
            new DisplayMode(3840, 2160, 60),
            id,
            "00000000:00000001",
            1,
            1,
            "HDMI");

    private static DisplaySnapshot CreateSnapshot(DisplayDevice targetDisplay) =>
        CreateSnapshot("snapshot-1", targetDisplay);

    private static DisplaySnapshot CreateSnapshot(string snapshotId, DisplayDevice targetDisplay) =>
        new(
            snapshotId,
            DateTimeOffset.UtcNow,
            [targetDisplay],
            [
                new DisplayPathSnapshot(
                    targetDisplay.Identifier,
                    "00000000:00000001",
                    1,
                    1,
                    true,
                    true,
                    new DisplayPoint(0, 0),
                    3840,
                    2160,
                    "32Bpp",
                    new DisplayRefreshRate(60, 1),
                    "Identity",
                    "Identity",
                    "HDMI",
                    new DisplaySourceModeSnapshot(3840, 2160, "32Bpp", new DisplayPoint(0, 0)),
                    new DisplayTargetModeSnapshot(new DisplayRefreshRate(60, 1), 3840, 2160, "Progressive"))
            ]);

    private sealed class FakeConfigurationStore(AgentConfiguration configuration) : IAgentConfigurationStore
    {
        private AgentConfiguration configuration = configuration;

        public Func<CancellationToken, Task>? OnLoadAsync { get; init; }

        public async Task<AgentConfiguration> LoadAsync(CancellationToken cancellationToken = default)
        {
            if (OnLoadAsync is not null)
            {
                await OnLoadAsync(cancellationToken);
            }

            return configuration;
        }

        public Task SaveAsync(AgentConfiguration configuration, CancellationToken cancellationToken = default)
        {
            this.configuration = configuration;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeDisplayManager : IDisplayManager
    {
        public IReadOnlyList<DisplayDevice> ConnectedDisplays { get; init; } = [];
        public Queue<IReadOnlyList<DisplayDevice>>? ConnectedDisplaysSequence { get; init; }
        public required DisplaySnapshot SnapshotToCapture { get; init; }
        public OperationResult ActivateOnlyResult { get; init; } = OperationResult.Success();
        public Queue<OperationResult>? ActivateOnlyResults { get; init; }
        public OperationResult RestoreSnapshotResult { get; init; } = OperationResult.Success();
        public OperationResult PrepareForCouchModeResult { get; init; } = OperationResult.Success("prepared");
        public Exception? ActivateOnlyException { get; init; }
        public Exception? RestoreSnapshotException { get; init; }
        public int GetDisplaysCallCount { get; private set; }
        public int PrepareForCouchModeCallCount { get; private set; }
        public int ActivateOnlyCallCount { get; private set; }
        public ActivateOnlyCall? ActivateOnlyCall { get; private set; }
        public DisplaySnapshot? RestoredSnapshot { get; private set; }
        public List<string> Operations { get; } = [];

        public Task<IReadOnlyList<DisplayDevice>> GetDisplaysAsync(CancellationToken cancellationToken = default)
        {
            GetDisplaysCallCount++;
            Operations.Add("get-displays");

            if (ConnectedDisplaysSequence is { Count: > 0 })
            {
                return Task.FromResult(ConnectedDisplaysSequence.Dequeue());
            }

            return Task.FromResult(ConnectedDisplays);
        }

        public Task<DisplaySnapshot> CaptureSnapshotAsync(CancellationToken cancellationToken = default) =>
            CaptureSnapshotCoreAsync();

        private Task<DisplaySnapshot> CaptureSnapshotCoreAsync()
        {
            Operations.Add("capture-snapshot");
            return Task.FromResult(SnapshotToCapture);
        }

        public Task<OperationResult> PrepareForCouchModeAsync(
            AgentConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            PrepareForCouchModeCallCount++;
            Operations.Add("prepare-tv");
            return Task.FromResult(PrepareForCouchModeResult);
        }

        public Task<OperationResult> ActivateOnlyAsync(
            DisplayIdentifier display,
            DisplayMode? preferredMode,
            bool dryRun = false,
            CancellationToken cancellationToken = default)
        {
            ActivateOnlyCallCount++;
            Operations.Add("activate-only");
            ActivateOnlyCall = new ActivateOnlyCall(display, preferredMode, dryRun);

            if (ActivateOnlyException is not null)
            {
                throw ActivateOnlyException;
            }

            if (ActivateOnlyResults is { Count: > 0 })
            {
                return Task.FromResult(ActivateOnlyResults.Dequeue());
            }

            return Task.FromResult(ActivateOnlyResult);
        }

        public Task<OperationResult> RestoreSnapshotAsync(
            DisplaySnapshot snapshot,
            RestoreSnapshotOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Operations.Add("restore-snapshot");
            RestoredSnapshot = snapshot;

            if (RestoreSnapshotException is not null)
            {
                throw RestoreSnapshotException;
            }

            return Task.FromResult(RestoreSnapshotResult);
        }
    }

    private sealed class FakeModeAutomationService : IModeAutomationService
    {
        public OperationResult Result { get; init; } = OperationResult.Success("No audio switch command configured.");

        public Task<OperationResult> RunPostActivationAsync(
            AgentMode mode,
            AgentConfiguration configuration,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result);
    }

    private sealed record ActivateOnlyCall(DisplayIdentifier Display, DisplayMode? PreferredMode, bool DryRun);

    private sealed class FakeSnapshotStore : IDisplaySnapshotStore
    {
        private readonly Dictionary<string, DisplaySnapshot> pendingSnapshots = new(StringComparer.Ordinal);

        public DisplaySnapshot? LastSnapshot { get; set; }

        public DisplaySnapshot? SavedSnapshot { get; private set; }

        public Task SaveAsync(DisplaySnapshot snapshot, CancellationToken cancellationToken = default)
        {
            SavedSnapshot = snapshot;
            LastSnapshot = snapshot;
            return Task.CompletedTask;
        }

        public Task SavePendingAsync(DisplaySnapshot snapshot, CancellationToken cancellationToken = default)
        {
            pendingSnapshots[snapshot.SnapshotId] = snapshot;
            return Task.CompletedTask;
        }

        public Task<DisplaySnapshot?> LoadLastDesktopSnapshotAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(LastSnapshot);

        public Task<DisplaySnapshot?> LoadByIdAsync(string snapshotId, CancellationToken cancellationToken = default)
        {
            pendingSnapshots.TryGetValue(snapshotId, out var snapshot);
            return Task.FromResult(snapshot);
        }

        public Task PromotePendingAsync(string snapshotId, CancellationToken cancellationToken = default)
        {
            pendingSnapshots.TryGetValue(snapshotId, out var snapshot);
            SavedSnapshot = snapshot;
            LastSnapshot = snapshot;
            return Task.CompletedTask;
        }

        public Task ClearAsync(CancellationToken cancellationToken = default)
        {
            LastSnapshot = null;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeJournalStore : IDisplayOperationJournalStore
    {
        public DisplayOperationJournal? Journal { get; set; }

        public Task<DisplayOperationJournal?> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Journal);

        public Task SaveAsync(DisplayOperationJournal journal, CancellationToken cancellationToken = default)
        {
            Journal = journal;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSteamLauncher : ISteamLauncher
    {
        public bool IsInstalledResult { get; init; } = true;

        public OperationResult StartResult { get; init; } = OperationResult.Success("steam launched");

        public bool IsHeroicInstalledResult { get; init; }

        public bool HeroicLaunchCalled { get; private set; }

        public bool IsInstalled(AgentConfiguration configuration) => IsInstalledResult;

        public bool IsRunning() => false;

        public Task<OperationResult> StartBigPictureAsync(
            AgentConfiguration configuration,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(StartResult);

        public Task<OperationResult> ExitBigPictureAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult.Success("steam exited"));

        public bool IsHeroicInstalled(AgentConfiguration configuration) => IsHeroicInstalledResult;

        public Task<OperationResult> StartHeroicConsoleAsync(
            AgentConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            HeroicLaunchCalled = true;
            return Task.FromResult(OperationResult.Success("heroic launched"));
        }
    }
}
