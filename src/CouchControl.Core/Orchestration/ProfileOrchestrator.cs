using CouchControl.Core.Abstractions;
using CouchControl.Core.Models;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace CouchControl.Core.Orchestration;

public sealed class ProfileOrchestrator : IProfileOrchestrator
{
    private readonly SemaphoreSlim operationLock = new(1, 1);
    private readonly object statusLock = new();

    private readonly IAgentConfigurationStore configurationStore;
    private readonly IDisplayManager displayManager;
    private readonly IDisplayMatchingService displayMatchingService;
    private readonly ISteamLauncher steamLauncher;
    private readonly IModeAutomationService modeAutomationService;
    private readonly IDisplaySnapshotStore snapshotStore;
    private readonly IDisplayOperationJournalStore journalStore;
    private readonly ILogger<ProfileOrchestrator> logger;
    private readonly TimeProvider timeProvider;

    private AgentOperationStatus status = AgentOperationStatus.Idle();

    public ProfileOrchestrator(
        IAgentConfigurationStore configurationStore,
        IDisplayManager displayManager,
        IDisplayMatchingService displayMatchingService,
        ISteamLauncher steamLauncher,
        IModeAutomationService modeAutomationService,
        IDisplaySnapshotStore snapshotStore,
        IDisplayOperationJournalStore journalStore,
        ILogger<ProfileOrchestrator> logger,
        TimeProvider? timeProvider = null)
    {
        this.configurationStore = configurationStore;
        this.displayManager = displayManager;
        this.displayMatchingService = displayMatchingService;
        this.steamLauncher = steamLauncher;
        this.modeAutomationService = modeAutomationService;
        this.snapshotStore = snapshotStore;
        this.journalStore = journalStore;
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
        using var operationScope = BeginOperationLoggingScope(operationId, ProfileOperationType.ActivateCouchMode);

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

            var pendingJournal = await journalStore.LoadAsync(cancellationToken);
            if (pendingJournal is { IsInProgress: true })
            {
                logger.LogWarning(
                    "Rejected Couch Mode activation because interrupted recovery is still pending for rollback snapshot {SnapshotId}.",
                    pendingJournal.RollbackSnapshotId);

                return CompleteOperation(
                    CreateResult(
                        AgentMode.Couch,
                        ProfileActivationStatus.Failure,
                        OperationResult.Failure(
                            "An interrupted display operation must be recovered before Couch Mode can run again.",
                            "display_recovery_pending",
                            outcome: "Failure",
                            details:
                            [
                                $"Pending operation: {pendingJournal.OperationId}",
                                $"Rollback snapshot: {pendingJournal.RollbackSnapshotId}"
                            ]),
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

            if (!string.IsNullOrWhiteSpace(configuration.TvPreparationCommand))
            {
                UpdateStep(ProfileOperationStep.Validating);
                var preparationResult = await displayManager.PrepareForCouchModeAsync(configuration, cancellationToken);
                if (!preparationResult.Succeeded)
                {
                    return CompleteOperation(
                        CreateResult(
                            AgentMode.Couch,
                            ProfileActivationStatus.Failure,
                            preparationResult,
                            null,
                            operationId,
                            startedAt),
                        AgentOperationState.Failed);
                }
            }

            UpdateStep(ProfileOperationStep.MatchingDisplay);
            var matchedDisplay = await MatchDisplayAsync(configuration, cancellationToken);

            var lastDesktopSnapshot = await snapshotStore.LoadLastDesktopSnapshotAsync(cancellationToken);
            if (lastDesktopSnapshot is null)
            {
                return CompleteOperation(
                    CreateResult(
                        AgentMode.Couch,
                        ProfileActivationStatus.Failure,
                        OperationResult.Failure(
                            "A desktop snapshot must be captured manually before couch mode can be activated.",
                            "desktop_snapshot_missing",
                            outcome: "Failure"),
                        null,
                        operationId,
                        startedAt),
                    AgentOperationState.Failed);
            }

            UpdateStep(ProfileOperationStep.CapturingSnapshot);
            var snapshot = await displayManager.CaptureSnapshotAsync(cancellationToken);

            UpdateStep(ProfileOperationStep.PersistingSnapshot);
            await snapshotStore.SavePendingAsync(snapshot, cancellationToken);
            await journalStore.SaveAsync(
                new DisplayOperationJournal(
                    operationId,
                    DisplayOperationJournalTypes.ActivateCouch,
                    DisplayOperationJournalStates.InProgress,
                    snapshot.SnapshotId,
                    startedAt),
                cancellationToken);

            UpdateStep(ProfileOperationStep.ActivatingDisplay);
            var displayResult = await ActivateDisplayAsync(
                configuration,
                matchedDisplay.Identifier,
                configuration.PreferredCouchMode,
                dryRun,
                snapshot,
                cancellationToken);

            if (!displayResult.Succeeded)
            {
                if (displayResult.RollbackResult?.Succeeded == true)
                {
                    await CompleteJournalAsync(operationId, cancellationToken);
                }

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

            var couchAudioResult = await RunPostActivationCommandAsync(AgentMode.Couch, configuration, cancellationToken);
            displayResult = MergePostActivationResult(displayResult, couchAudioResult);

            if (configuration.CouchLauncher == CouchLauncher.None ||
                (configuration.CouchLauncher == CouchLauncher.SteamBigPicture &&
                 !configuration.LaunchSteamAutomatically))
            {
                await CompleteJournalAsync(operationId, cancellationToken);
                return CompleteOperation(
                    CreateResult(
                        AgentMode.Couch,
                        ProfileActivationStatus.Success,
                        displayResult,
                        OperationResult.Success(
                            "Automatic game launcher is disabled in configuration.",
                            outcome: "Success",
                            details:
                            [
                                "Couch Mode display configuration applied",
                                "Game launcher disabled",
                                "Couch Mode ready"
                            ]),
                        operationId,
                        startedAt,
                        snapshot),
                    AgentOperationState.Succeeded,
                    AgentMode.Couch);
            }

            UpdateStep(ProfileOperationStep.LaunchingLauncher);
            var launcherName = configuration.CouchLauncher == CouchLauncher.HeroicConsole
                ? "Heroic Games Launcher"
                : "Steam";
            var launcherInstalled = configuration.CouchLauncher == CouchLauncher.HeroicConsole
                ? steamLauncher.IsHeroicInstalled(configuration)
                : steamLauncher.IsInstalled(configuration);
            if (!launcherInstalled)
            {
                await CompleteJournalAsync(operationId, cancellationToken);
                return CompleteOperation(
                    CreateResult(
                        AgentMode.Couch,
                        ProfileActivationStatus.PartialSuccess,
                        displayResult,
                        OperationResult.PartialSuccess(
                            $"{launcherName} is not installed.",
                            outcome: "Partial success",
                            details:
                            [
                                "Couch Mode display configuration applied",
                                $"{launcherName} installation not found",
                                $"Couch Mode ready without {launcherName}"
                            ]),
                        operationId,
                        startedAt,
                        snapshot),
                    AgentOperationState.PartiallySucceeded,
                    AgentMode.Couch);
            }

            var steamLaunchResult = configuration.CouchLauncher == CouchLauncher.HeroicConsole
                ? await steamLauncher.StartHeroicConsoleAsync(configuration, cancellationToken)
                : await steamLauncher.StartBigPictureAsync(configuration, cancellationToken);
            var steamResult = WrapSteamResult(steamLaunchResult);
            var activationStatus = steamResult.Succeeded
                ? ProfileActivationStatus.Success
                : ProfileActivationStatus.PartialSuccess;
            var operationState = steamResult.Succeeded
                ? AgentOperationState.Succeeded
                : AgentOperationState.PartiallySucceeded;

            await CompleteJournalAsync(operationId, cancellationToken);
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
        using var operationScope = BeginOperationLoggingScope(operationId, ProfileOperationType.ActivateDesktopMode);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var configuration = await configurationStore.LoadAsync(cancellationToken);
            UpdateStep(ProfileOperationStep.LoadingSnapshot);
            var pendingJournal = await journalStore.LoadAsync(cancellationToken);
            var recoveringInterruptedOperation = pendingJournal is { IsInProgress: true };
            var snapshot = recoveringInterruptedOperation
                ? await snapshotStore.LoadByIdAsync(pendingJournal!.RollbackSnapshotId, cancellationToken)
                : await snapshotStore.LoadLastDesktopSnapshotAsync(cancellationToken);
            if (snapshot is null)
            {
                return CompleteOperation(
                    CreateResult(
                        AgentMode.Desktop,
                        ProfileActivationStatus.Failure,
                        OperationResult.Failure(
                            recoveringInterruptedOperation
                                ? $"The rollback snapshot '{pendingJournal!.RollbackSnapshotId}' is missing, so the interrupted display operation cannot be recovered."
                                : "No desktop snapshot is available to restore.",
                            recoveringInterruptedOperation
                                ? "rollback_snapshot_missing"
                                : "desktop_snapshot_missing",
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

            if (displayResult.Succeeded)
            {
                var desktopAudioResult = await RunPostActivationCommandAsync(AgentMode.Desktop, configuration, cancellationToken);
                displayResult = MergePostActivationResult(displayResult, desktopAudioResult);
                if (configuration.CouchLauncher == CouchLauncher.SteamBigPicture)
                {
                    var steamExitResult = await steamLauncher.ExitBigPictureAsync(cancellationToken);
                    displayResult = MergePostActivationResult(displayResult, steamExitResult);
                }
            }

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

            if (recoveringInterruptedOperation && displayResult.Succeeded)
            {
                await journalStore.SaveAsync(pendingJournal!.MarkRecovered(GetUtcNow()), cancellationToken);
            }

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
            try
            {
                return await MatchConnectedDisplayAsync(configuration, cancellationToken);
            }
            catch (InvalidOperationException ex) when (!string.IsNullOrWhiteSpace(configuration.TvPreparationCommand))
            {
                logger.LogInformation(
                    ex,
                    "Configured TV was not detected. Running the configured TV preparation command before retrying display detection.");

                var preparationResult = await displayManager.PrepareForCouchModeAsync(configuration, cancellationToken);
                if (!preparationResult.Succeeded)
                {
                    throw new InvalidOperationException(preparationResult.Message);
                }

                return await MatchConnectedDisplayAsync(configuration, cancellationToken);
            }
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

    private async Task<DisplayDevice> MatchConnectedDisplayAsync(
        AgentConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var connectedDisplays = await displayManager.GetDisplaysAsync(cancellationToken);

        return configuration.CouchDisplayIdentity is not null
            ? displayMatchingService.MatchDisplay(configuration.CouchDisplayIdentity, connectedDisplays)
            : connectedDisplays.FirstOrDefault(display =>
                display.Identifier.Matches(configuration.CouchDisplayIdentifier!)) ??
                throw new InvalidOperationException(
                    $"Configured TV '{configuration.CouchDisplayIdentifier}' is not connected.");
    }

    private async Task<OperationResult> ActivateDisplayAsync(
        AgentConfiguration configuration,
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

            if (!dryRun &&
                ShouldRetryActivationAfterTvPreparation(displayResult.ErrorCode) &&
                !string.IsNullOrWhiteSpace(configuration.TvPreparationCommand))
            {
                logger.LogInformation(
                    "Display activation reported {ErrorCode}. Running the configured TV preparation command and retrying once.",
                    displayResult.ErrorCode);

                var preparationResult = await displayManager.PrepareForCouchModeAsync(configuration, cancellationToken);
                if (!preparationResult.Succeeded)
                {
                    return await EnsureRollbackAsync(preparationResult, snapshot, dryRun, cancellationToken);
                }

                var retryResult = await displayManager.ActivateOnlyAsync(
                    displayIdentifier,
                    preferredMode,
                    dryRun,
                    cancellationToken);

                if (retryResult.Succeeded)
                {
                    return retryResult;
                }

                return await EnsureRollbackAsync(retryResult, snapshot, dryRun, cancellationToken);
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

    private static bool ShouldRetryActivationAfterTvPreparation(string? errorCode) =>
        string.Equals(errorCode, "display_switch_verification_failed", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(errorCode, "display_target_inactive_after_extend", StringComparison.OrdinalIgnoreCase);

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

    private async Task<OperationResult> RunPostActivationCommandAsync(
        AgentMode mode,
        AgentConfiguration configuration,
        CancellationToken cancellationToken) =>
        await modeAutomationService.RunPostActivationAsync(mode, configuration, cancellationToken);

    private static OperationResult MergePostActivationResult(
        OperationResult primaryResult,
        OperationResult postActivationResult)
    {
        if (postActivationResult.Message == "No audio switch command configured.")
        {
            return primaryResult;
        }

        var details = primaryResult.Details
            .Concat(postActivationResult.Details)
            .Append(postActivationResult.Message)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (postActivationResult.Succeeded)
        {
            return primaryResult.IsPartialSuccess
                ? OperationResult.PartialSuccess(primaryResult.Message, primaryResult.Outcome, details)
                : OperationResult.Success(primaryResult.Message, primaryResult.Outcome, details);
        }

        return OperationResult.PartialSuccess(
            $"{primaryResult.Message} Audio output was not switched automatically.",
            primaryResult.Outcome,
            details);
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

    private IDisposable? BeginOperationLoggingScope(Guid operationId, ProfileOperationType operationType)
    {
        var activity = new Activity(operationType.ToString());
        activity.SetIdFormat(ActivityIdFormat.W3C);
        activity.SetTag("OperationId", operationId.ToString());
        activity.AddBaggage("OperationId", operationId.ToString());
        activity.Start();

        return new CompositeDisposable(
            logger.BeginScope(new Dictionary<string, object?>
            {
                ["CorrelationId"] = operationId,
                ["OperationType"] = operationType.ToString()
            }),
            activity);
    }

    private async Task CompleteJournalAsync(Guid operationId, CancellationToken cancellationToken)
    {
        var journal = await journalStore.LoadAsync(cancellationToken);
        if (journal is null || journal.OperationId != operationId || !journal.IsInProgress)
        {
            return;
        }

        await journalStore.SaveAsync(journal.MarkCompleted(GetUtcNow()), cancellationToken);
    }

    private sealed class CompositeDisposable(IDisposable? scope, Activity activity) : IDisposable
    {
        public void Dispose()
        {
            scope?.Dispose();
            activity.Stop();
            activity.Dispose();
        }
    }
}
