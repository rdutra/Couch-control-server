using CouchControl.Core.Abstractions;
using CouchControl.Core.Models;
using Microsoft.Extensions.Logging;

namespace CouchControl.Core.Orchestration;

public sealed class ProfileOrchestrator : IProfileOrchestrator
{
    private readonly SemaphoreSlim operationLock = new(1, 1);
    private readonly object statusLock = new();

    private readonly IAgentConfigurationStore configurationStore;
    private readonly IDisplayManager displayManager;
    private readonly IDisplayMatchingService displayMatchingService;
    private readonly ISteamLauncher steamLauncher;
    private readonly IDisplaySnapshotStore snapshotStore;
    private readonly ILogger<ProfileOrchestrator> logger;
    private readonly TimeProvider timeProvider;

    private AgentOperationStatus status = AgentOperationStatus.Idle();

    public ProfileOrchestrator(
        IAgentConfigurationStore configurationStore,
        IDisplayManager displayManager,
        IDisplayMatchingService displayMatchingService,
        ISteamLauncher steamLauncher,
        IDisplaySnapshotStore snapshotStore,
        ILogger<ProfileOrchestrator> logger,
        TimeProvider? timeProvider = null)
    {
        this.configurationStore = configurationStore;
        this.displayManager = displayManager;
        this.displayMatchingService = displayMatchingService;
        this.steamLauncher = steamLauncher;
        this.snapshotStore = snapshotStore;
        this.logger = logger;
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public Task<ProfileActivationResult> ActivateCouchModeAsync(
        CancellationToken cancellationToken = default) =>
        ActivateCouchModeAsync(dryRun: false, cancellationToken);

    public Task<ProfileActivationResult> ActivateDesktopModeAsync(
        CancellationToken cancellationToken = default) =>
        ActivateDesktopModeAsync(dryRun: false, forceFallback: false, cancellationToken);

    public AgentOperationStatus GetStatus()
    {
        lock (statusLock)
        {
            return status;
        }
    }

    public async Task<ProfileActivationResult> ActivateCouchModeAsync(
        bool dryRun,
        CancellationToken cancellationToken = default)
    {
        if (!await operationLock.WaitAsync(0, cancellationToken))
        {
            return CreateConcurrentFailureResult(
                AgentMode.Couch,
                "Couch Mode activation is already in progress.",
                "operation_in_progress");
        }

        var operationId = Guid.NewGuid();
        var startedAt = GetUtcNow();
        BeginOperation(operationId, AgentMode.Couch, ProfileOperationType.ActivateCouchMode, startedAt);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            UpdateStep(ProfileOperationStep.LoadingConfiguration);
            var configuration = await configurationStore.LoadAsync(cancellationToken);

            UpdateStep(ProfileOperationStep.Validating);
            var validationResult = configuration.Validate();
            if (!validationResult.Succeeded)
            {
                return CompleteOperation(
                    CreateResult(
                        AgentMode.Couch,
                        ProfileActivationStatus.Failure,
                        validationResult,
                        null,
                        operationId,
                        startedAt),
                    AgentOperationState.Failed);
            }

            if (configuration.CouchDisplayIdentifier is null)
            {
                return CompleteOperation(
                    CreateResult(
                        AgentMode.Couch,
                        ProfileActivationStatus.Failure,
                        OperationResult.Failure(
                            "A couch display must be configured before couch mode can be activated.",
                            "couch_display_missing",
                            outcome: "Failure"),
                        null,
                        operationId,
                        startedAt),
                    AgentOperationState.Failed);
            }

            UpdateStep(ProfileOperationStep.MatchingDisplay);
            var matchedDisplay = await MatchDisplayAsync(configuration, cancellationToken);

            UpdateStep(ProfileOperationStep.CapturingSnapshot);
            var snapshot = await displayManager.CaptureSnapshotAsync(cancellationToken);

            UpdateStep(ProfileOperationStep.PersistingSnapshot);
            await snapshotStore.SaveAsync(snapshot, cancellationToken);

            UpdateStep(ProfileOperationStep.ActivatingDisplay);
            var displayResult = await ActivateDisplayAsync(
                matchedDisplay.Identifier,
                configuration.PreferredCouchMode,
                dryRun,
                snapshot,
                cancellationToken);

            if (!displayResult.Succeeded)
            {
                return CompleteOperation(
                    CreateResult(
                        AgentMode.Couch,
                        ProfileActivationStatus.Failure,
                        displayResult,
                        null,
                        operationId,
                        startedAt,
                        snapshot),
                    AgentOperationState.Failed);
            }

            if (!configuration.LaunchSteamAutomatically)
            {
                return CompleteOperation(
                    CreateResult(
                        AgentMode.Couch,
                        ProfileActivationStatus.Success,
                        displayResult,
                        OperationResult.Success(
                            "Steam launch is disabled in configuration.",
                            outcome: "Success",
                            details:
                            [
                                "Couch Mode display configuration applied",
                                "Steam launch disabled",
                                "Couch Mode ready"
                            ]),
                        operationId,
                        startedAt,
                        snapshot),
                    AgentOperationState.Succeeded,
                    AgentMode.Couch);
            }

            UpdateStep(ProfileOperationStep.LaunchingSteam);
            if (!steamLauncher.IsInstalled(configuration))
            {
                return CompleteOperation(
                    CreateResult(
                        AgentMode.Couch,
                        ProfileActivationStatus.PartialSuccess,
                        displayResult,
                        OperationResult.PartialSuccess(
                            "Steam is not installed.",
                            outcome: "Partial success",
                            details:
                            [
                                "Couch Mode display configuration applied",
                                "Steam installation not found",
                                "Couch Mode ready without Steam"
                            ]),
                        operationId,
                        startedAt,
                        snapshot),
                    AgentOperationState.PartiallySucceeded,
                    AgentMode.Couch);
            }

            var steamLaunchResult = await steamLauncher.StartBigPictureAsync(configuration, cancellationToken);
            var steamResult = WrapSteamResult(steamLaunchResult);
            var activationStatus = steamResult.Succeeded
                ? ProfileActivationStatus.Success
                : ProfileActivationStatus.PartialSuccess;
            var operationState = steamResult.Succeeded
                ? AgentOperationState.Succeeded
                : AgentOperationState.PartiallySucceeded;

            return CompleteOperation(
                CreateResult(
                    AgentMode.Couch,
                    activationStatus,
                    displayResult,
                    steamResult,
                    operationId,
                    startedAt,
                    snapshot),
                operationState,
                AgentMode.Couch);
        }
        catch (OperationCanceledException)
        {
            CancelOperation(operationId);
            throw;
        }
        catch (InvalidOperationException ex)
        {
            var result = CreateResult(
                AgentMode.Couch,
                ProfileActivationStatus.Failure,
                OperationResult.Failure(
                    ex.Message,
                    "couch_display_not_connected",
                    outcome: "Failure"),
                null,
                operationId,
                startedAt);

            return CompleteOperation(result, AgentOperationState.Failed, errorOverride: ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Couch Mode activation failed.");
            var result = CreateResult(
                AgentMode.Couch,
                ProfileActivationStatus.Failure,
                OperationResult.Failure(
                    $"Couch Mode failed: {ex.Message}",
                    "couch_mode_exception",
                    outcome: "Failure"),
                null,
                operationId,
                startedAt);

            return CompleteOperation(result, AgentOperationState.Failed, errorOverride: ex.Message);
        }
        finally
        {
            operationLock.Release();
        }
    }

    public async Task<ProfileActivationResult> ActivateDesktopModeAsync(
        bool dryRun,
        bool forceFallback,
        CancellationToken cancellationToken = default)
    {
        if (!await operationLock.WaitAsync(0, cancellationToken))
        {
            return CreateConcurrentFailureResult(
                AgentMode.Desktop,
                "Desktop Mode activation is already in progress.",
                "operation_in_progress");
        }

        var operationId = Guid.NewGuid();
        var startedAt = GetUtcNow();
        BeginOperation(operationId, AgentMode.Desktop, ProfileOperationType.ActivateDesktopMode, startedAt);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            UpdateStep(ProfileOperationStep.LoadingSnapshot);
            var snapshot = await snapshotStore.LoadLastDesktopSnapshotAsync(cancellationToken);
            if (snapshot is null)
            {
                return CompleteOperation(
                    CreateResult(
                        AgentMode.Desktop,
                        ProfileActivationStatus.Failure,
                        OperationResult.Failure(
                            "No desktop snapshot is available to restore.",
                            "desktop_snapshot_missing",
                            outcome: "Failure"),
                        null,
                        operationId,
                        startedAt),
                    AgentOperationState.Failed);
            }

            UpdateStep(ProfileOperationStep.RestoringDesktop);
            var displayResult = await displayManager.RestoreSnapshotAsync(
                snapshot,
                new RestoreSnapshotOptions(dryRun, forceFallback),
                cancellationToken);

            var activationStatus = displayResult.Succeeded
                ? displayResult.IsPartialSuccess
                    ? ProfileActivationStatus.PartialSuccess
                    : ProfileActivationStatus.Success
                : ProfileActivationStatus.Failure;
            var operationState = activationStatus switch
            {
                ProfileActivationStatus.Success => AgentOperationState.Succeeded,
                ProfileActivationStatus.PartialSuccess => AgentOperationState.PartiallySucceeded,
                _ => AgentOperationState.Failed
            };

            return CompleteOperation(
                CreateResult(
                        AgentMode.Desktop,
                        activationStatus,
                        displayResult,
                        null,
                        operationId,
                        startedAt,
                        snapshot),
                operationState,
                activationStatus is ProfileActivationStatus.Failure ? null : AgentMode.Desktop);
        }
        catch (OperationCanceledException)
        {
            CancelOperation(operationId);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Desktop Mode activation failed.");
            var result = CreateResult(
                AgentMode.Desktop,
                ProfileActivationStatus.Failure,
                OperationResult.Failure(
                    $"Desktop Mode failed: {ex.Message}",
                    "desktop_mode_exception",
                    outcome: "Failure"),
                null,
                operationId,
                startedAt);

            return CompleteOperation(result, AgentOperationState.Failed, errorOverride: ex.Message);
        }
        finally
        {
            operationLock.Release();
        }
    }

    private async Task<DisplayDevice> MatchDisplayAsync(
        AgentConfiguration configuration,
        CancellationToken cancellationToken)
    {
        try
        {
            var connectedDisplays = await displayManager.GetDisplaysAsync(cancellationToken);

            return configuration.CouchDisplayIdentity is not null
                ? displayMatchingService.MatchDisplay(configuration.CouchDisplayIdentity, connectedDisplays)
                : connectedDisplays.FirstOrDefault(display =>
                    display.Identifier.Matches(configuration.CouchDisplayIdentifier!)) ??
                    throw new InvalidOperationException(
                        $"Configured TV '{configuration.CouchDisplayIdentifier}' is not connected.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Configured TV could not be matched to a connected display.");
            throw new InvalidOperationException(
                $"Configured TV is not connected: {ex.Message}",
                ex);
        }
    }

    private async Task<OperationResult> ActivateDisplayAsync(
        DisplayIdentifier displayIdentifier,
        DisplayMode preferredMode,
        bool dryRun,
        DisplaySnapshot snapshot,
        CancellationToken cancellationToken)
    {
        try
        {
            var displayResult = await displayManager.ActivateOnlyAsync(
                displayIdentifier,
                preferredMode,
                dryRun,
                cancellationToken);

            if (displayResult.Succeeded)
            {
                return displayResult;
            }

            return await EnsureRollbackAsync(displayResult, snapshot, dryRun, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Display activation threw an exception.");
            return await EnsureRollbackAsync(
                OperationResult.Failure(
                    $"Display activation failed: {ex.Message}",
                    "display_activation_exception",
                    outcome: "Failure"),
                snapshot,
                dryRun,
                cancellationToken);
        }
    }

    private async Task<OperationResult> EnsureRollbackAsync(
        OperationResult displayResult,
        DisplaySnapshot snapshot,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        if (displayResult.RollbackResult is not null || dryRun)
        {
            return displayResult;
        }

        OperationResult rollbackResult;
        try
        {
            rollbackResult = await displayManager.RestoreSnapshotAsync(
                snapshot,
                cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            rollbackResult = OperationResult.Failure(
                $"Rollback threw an exception: {ex.Message}",
                "desktop_rollback_exception",
                outcome: "Failure");
        }

        return OperationResult.Failure(
            displayResult.Message,
            displayResult.ErrorCode,
            rollbackResult,
            displayResult.Outcome,
            displayResult.Details);
    }

    private static OperationResult WrapSteamResult(OperationResult steamLaunchResult)
    {
        var details = new List<string>
        {
            "Couch Mode display configuration applied"
        };
        details.AddRange(steamLaunchResult.Details);
        details.Add(steamLaunchResult.Succeeded
            ? "Couch Mode ready"
            : "Couch Mode ready without Steam");

        return steamLaunchResult.Succeeded
            ? OperationResult.Success(
                steamLaunchResult.Message,
                outcome: "Success",
                details: details)
            : OperationResult.Failure(
                steamLaunchResult.Message,
                steamLaunchResult.ErrorCode,
                outcome: "Failure",
                details: details);
    }

    private ProfileActivationResult CreateConcurrentFailureResult(
        AgentMode mode,
        string message,
        string errorCode)
    {
        logger.LogWarning("{Mode} request rejected because another operation is already in progress.", mode);
        return new ProfileActivationResult(
            mode,
            ProfileActivationStatus.Failure,
            OperationResult.Failure(message, errorCode, outcome: "Failure"));
    }

    private ProfileActivationResult CreateResult(
        AgentMode mode,
        ProfileActivationStatus status,
        OperationResult displayResult,
        OperationResult? steamResult,
        Guid operationId,
        DateTimeOffset startedAt,
        DisplaySnapshot? snapshot = null) =>
        new(
            mode,
            status,
            displayResult,
            steamResult,
            snapshot,
            operationId,
            startedAt,
            null);

    private ProfileActivationResult CompleteOperation(
        ProfileActivationResult result,
        AgentOperationState state,
        AgentMode? currentMode = null,
        string? errorOverride = null)
    {
        var completedAt = GetUtcNow();
        var finalized = result with
        {
            CompletedAtUtc = completedAt
        };

        lock (statusLock)
        {
            status = new AgentOperationStatus(
                finalized.OperationId,
                currentMode ?? status.CurrentMode,
                ProfileOperationType.None,
                ProfileOperationStep.Completed,
                state,
                FinalOperationResult(finalized),
                errorOverride ?? FinalError(finalized),
                finalized.StartedAtUtc,
                completedAt);
        }

        return finalized;
    }

    private void BeginOperation(
        Guid operationId,
        AgentMode mode,
        ProfileOperationType operationType,
        DateTimeOffset startedAt)
    {
        lock (statusLock)
        {
            status = new AgentOperationStatus(
                operationId,
                status.CurrentMode ?? mode,
                operationType,
                ProfileOperationStep.Validating,
                AgentOperationState.Validating,
                status.LastOperationResult,
                null,
                startedAt,
                null);
        }
    }

    private void UpdateStep(ProfileOperationStep step)
    {
        lock (statusLock)
        {
            status = status with
            {
                CurrentStep = step,
                State = step == ProfileOperationStep.Validating
                    ? AgentOperationState.Validating
                    : AgentOperationState.Running
            };
        }
    }

    private void CancelOperation(Guid operationId)
    {
        lock (statusLock)
        {
            if (status.OperationId != operationId)
            {
                return;
            }

            status = status with
            {
                CurrentOperation = ProfileOperationType.None,
                CurrentStep = ProfileOperationStep.Completed,
                State = AgentOperationState.Canceled,
                LastError = "Operation canceled.",
                OperationCompletedAtUtc = GetUtcNow()
            };
        }
    }

    private static OperationResult FinalOperationResult(ProfileActivationResult result) =>
        result.SteamResult ?? result.DisplayResult;

    private static string? FinalError(ProfileActivationResult result) =>
        result.SteamResult is { Succeeded: false } steamResult
            ? steamResult.Message
            : result.DisplayResult.Succeeded
                ? null
                : result.DisplayResult.Message;

    private DateTimeOffset GetUtcNow() => timeProvider.GetUtcNow();
}
