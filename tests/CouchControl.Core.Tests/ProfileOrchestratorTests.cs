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
        var snapshotStore = new FakeSnapshotStore();
        var orchestrator = CreateOrchestrator(configurationStore, displayManager, snapshotStore);

        var result = await orchestrator.ActivateCouchModeAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(ProfileActivationStatus.Success, result.Status);
        Assert.Same(snapshot, snapshotStore.SavedSnapshot);
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
            new FakeSnapshotStore(),
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
        var snapshotStore = new FakeSnapshotStore();
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
        Assert.Equal(snapshotStore.SavedSnapshot, result.Snapshot);
        Assert.Null(displayManager.RestoredSnapshot);
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
        var orchestrator = CreateOrchestrator(configurationStore, displayManager, new FakeSnapshotStore());

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
    public async Task ActivateCouchModeAsync_FailsWhenConfiguredTvIsNotConnected()
    {
        var targetDisplay = CreateDisplayDevice(@"\\?\DISPLAY#SAM0F8C#1", "Samsung TV", isActive: false);
        var displayManager = new FakeDisplayManager
        {
            ConnectedDisplays = [],
            SnapshotToCapture = CreateSnapshot(targetDisplay)
        };
        var configurationStore = new FakeConfigurationStore(CreateConfiguration(targetDisplay));
        var orchestrator = CreateOrchestrator(configurationStore, displayManager, new FakeSnapshotStore());

        var result = await orchestrator.ActivateCouchModeAsync();

        Assert.False(result.Succeeded);
        Assert.Equal("couch_display_not_connected", result.DisplayResult.ErrorCode);
        Assert.Equal(AgentOperationState.Failed, orchestrator.GetStatus().State);
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
        var orchestrator = CreateOrchestrator(configurationStore, displayManager, new FakeSnapshotStore());

        var result = await orchestrator.ActivateCouchModeAsync();

        Assert.False(result.Succeeded);
        Assert.NotNull(result.DisplayResult.RollbackResult);
        Assert.True(result.DisplayResult.RollbackResult!.Succeeded);
        Assert.Equal(snapshot, displayManager.RestoredSnapshot);
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
        var orchestrator = CreateOrchestrator(configurationStore, displayManager, new FakeSnapshotStore());

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

        await Assert.ThrowsAsync<OperationCanceledException>(() => task);
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

    private static ProfileOrchestrator CreateOrchestrator(
        FakeConfigurationStore configurationStore,
        FakeDisplayManager displayManager,
        FakeSnapshotStore snapshotStore,
        FakeSteamLauncher? steamLauncher = null)
    {
        return new ProfileOrchestrator(
            configurationStore,
            displayManager,
            new DisplayMatchingService(),
            steamLauncher ?? new FakeSteamLauncher(),
            snapshotStore,
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
        new(
            "snapshot-1",
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
        public required DisplaySnapshot SnapshotToCapture { get; init; }
        public OperationResult ActivateOnlyResult { get; init; } = OperationResult.Success();
        public OperationResult RestoreSnapshotResult { get; init; } = OperationResult.Success();
        public Exception? ActivateOnlyException { get; init; }
        public Exception? RestoreSnapshotException { get; init; }
        public ActivateOnlyCall? ActivateOnlyCall { get; private set; }
        public DisplaySnapshot? RestoredSnapshot { get; private set; }

        public Task<IReadOnlyList<DisplayDevice>> GetDisplaysAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(ConnectedDisplays);

        public Task<DisplaySnapshot> CaptureSnapshotAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(SnapshotToCapture);

        public Task<OperationResult> ActivateOnlyAsync(
            DisplayIdentifier display,
            DisplayMode? preferredMode,
            bool dryRun = false,
            CancellationToken cancellationToken = default)
        {
            ActivateOnlyCall = new ActivateOnlyCall(display, preferredMode, dryRun);

            if (ActivateOnlyException is not null)
            {
                throw ActivateOnlyException;
            }

            return Task.FromResult(ActivateOnlyResult);
        }

        public Task<OperationResult> RestoreSnapshotAsync(
            DisplaySnapshot snapshot,
            RestoreSnapshotOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            RestoredSnapshot = snapshot;

            if (RestoreSnapshotException is not null)
            {
                throw RestoreSnapshotException;
            }

            return Task.FromResult(RestoreSnapshotResult);
        }
    }

    private sealed record ActivateOnlyCall(DisplayIdentifier Display, DisplayMode? PreferredMode, bool DryRun);

    private sealed class FakeSnapshotStore : IDisplaySnapshotStore
    {
        public DisplaySnapshot? LastSnapshot { get; set; }

        public DisplaySnapshot? SavedSnapshot { get; private set; }

        public Task SaveAsync(DisplaySnapshot snapshot, CancellationToken cancellationToken = default)
        {
            SavedSnapshot = snapshot;
            LastSnapshot = snapshot;
            return Task.CompletedTask;
        }

        public Task<DisplaySnapshot?> LoadLastDesktopSnapshotAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(LastSnapshot);
    }

    private sealed class FakeSteamLauncher : ISteamLauncher
    {
        public bool IsInstalledResult { get; init; } = true;

        public OperationResult StartResult { get; init; } = OperationResult.Success("steam launched");

        public bool IsInstalled(AgentConfiguration configuration) => IsInstalledResult;

        public bool IsRunning() => false;

        public Task<OperationResult> StartBigPictureAsync(
            AgentConfiguration configuration,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(StartResult);
    }
}
