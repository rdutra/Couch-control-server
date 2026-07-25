using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using CouchControl.Core.Abstractions;
using CouchControl.Core.Models;
using CouchControl.Windows.Displays;
using CouchControl.Windows.Displays.Interop;
using Microsoft.Extensions.Logging;

namespace CouchControl.Windows;

public sealed class WindowsDisplayManager : IDisplayManager
{
    private static readonly SemaphoreSlim SwitchSemaphore = new(1, 1);
    private static readonly TimeSpan ExtendFallbackSettleDelay = TimeSpan.FromSeconds(3);
    private readonly ILogger<WindowsDisplayManager>? _logger;
    private readonly IWindowsDisplaySystem _displaySystem;
    private readonly bool _skipPlatformCheck;

    public WindowsDisplayManager(ILogger<WindowsDisplayManager>? logger = null)
        : this(new NativeWindowsDisplaySystem(), logger, skipPlatformCheck: false)
    {
    }

    internal WindowsDisplayManager(
        IWindowsDisplaySystem displaySystem,
        ILogger<WindowsDisplayManager>? logger = null,
        bool skipPlatformCheck = false)
    {
        _displaySystem = displaySystem;
        _logger = logger;
        _skipPlatformCheck = skipPlatformCheck;
    }

    public Task<IReadOnlyList<DisplayDevice>> GetDisplaysAsync(CancellationToken cancellationToken = default)
    {
        EnsureWindows();

        cancellationToken.ThrowIfCancellationRequested();

        var configuration = QueryDisplayConfiguration(NativeMethods.QDC_ALL_PATHS);
        var displays = BuildDisplayContexts(configuration)
            .Select(static context => new DisplayDevice(
                context.Identifier,
                context.FriendlyName,
                context.IsActive,
                context.SourceMode.HasValue && context.SourceMode.Value.infoType == DISPLAYCONFIG_MODE_INFO_TYPE.DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE &&
                context.SourceMode.Value.sourceMode.position.x == 0 &&
                context.SourceMode.Value.sourceMode.position.y == 0,
                ToDisplayMode(context.SourceMode ?? default, context.Path.targetInfo.refreshRate),
                context.DevicePath,
                context.Path.sourceInfo.adapterId.ToString(),
                context.Path.sourceInfo.id,
                context.Path.targetInfo.id,
                DisplayMapper.MapOutputTechnology(context.Path.targetInfo.outputTechnology)))
            .ToArray();

        return Task.FromResult<IReadOnlyList<DisplayDevice>>(displays);
    }

    public async Task<DisplaySnapshot> CaptureSnapshotAsync(CancellationToken cancellationToken = default)
    {
        EnsureWindows();

        cancellationToken.ThrowIfCancellationRequested();

        var configuration = QueryDisplayConfiguration(NativeMethods.QDC_ONLY_ACTIVE_PATHS);
        var displays = new List<DisplayDevice>(configuration.Paths.Length);
        var paths = new List<DisplayPathSnapshot>();

        foreach (var path in configuration.Paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var targetDetails = TryGetTargetDeviceName(path);
            var sourceMode = ResolveMode(configuration.Modes, path.sourceInfo.modeInfoIdx, DISPLAYCONFIG_MODE_INFO_TYPE.DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE);
            var targetMode = ResolveMode(configuration.Modes, path.targetInfo.modeInfoIdx, DISPLAYCONFIG_MODE_INFO_TYPE.DISPLAYCONFIG_MODE_INFO_TYPE_TARGET);

            displays.Add(DisplayMapper.MapToDomain(path, targetDetails.FriendlyName, targetDetails.DevicePath, sourceMode));
            paths.Add(DisplayMapper.MapPathSnapshot(path, targetDetails.DevicePath, sourceMode, targetMode));
        }

        var snapshot = new DisplaySnapshot(
            SnapshotId: Guid.NewGuid().ToString("N"),
            CapturedAtUtc: DateTimeOffset.UtcNow,
            Displays: displays,
            Paths: paths);

        var validation = snapshot.Validate();
        if (!validation.Succeeded)
        {
            throw new InvalidOperationException($"Captured display snapshot is invalid: {validation.Message}");
        }

        return await Task.FromResult(snapshot);
    }

    public async Task<OperationResult> PrepareForCouchModeAsync(
        AgentConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        if (configuration is null) throw new ArgumentNullException(nameof(configuration));

        var command = configuration.TvPreparationCommand;
        if (string.IsNullOrWhiteSpace(command))
        {
            return OperationResult.Success("No TV preparation command is configured.");
        }

        EnsureWindows();
        cancellationToken.ThrowIfCancellationRequested();

        _logger?.LogInformation("Running configured TV preparation command before couch mode activation.");

        var startInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/d /s /c \"{command}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start the configured TV preparation command.");

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        var standardOutput = await outputTask;
        var standardError = await errorTask;

        if (process.ExitCode != 0)
        {
            var failureMessage = string.IsNullOrWhiteSpace(standardError)
                ? $"TV preparation command failed with exit code {process.ExitCode}."
                : $"TV preparation command failed with exit code {process.ExitCode}: {standardError.Trim()}";

            _logger?.LogWarning(
                "TV preparation command failed with exit code {ExitCode}. Stdout: {Stdout}. Stderr: {Stderr}",
                process.ExitCode,
                standardOutput.Trim(),
                standardError.Trim());

            return OperationResult.Failure(failureMessage, "tv_preparation_command_failed");
        }

        if (configuration.TvPreparationDelayMs > 0)
        {
            _logger?.LogInformation(
                "Waiting {DelayMs} ms after TV preparation command before retrying display detection.",
                configuration.TvPreparationDelayMs);

            await Task.Delay(configuration.TvPreparationDelayMs, cancellationToken);
        }

        return OperationResult.Success(
            "TV preparation command completed successfully.",
            details: string.IsNullOrWhiteSpace(standardOutput)
                ? null
                : [standardOutput.Trim()]);
    }

    public async Task<OperationResult> ActivateOnlyAsync(
        DisplayIdentifier display,
        DisplayMode? preferredMode,
        bool dryRun = false,
        CancellationToken cancellationToken = default)
    {
        EnsureWindows();

        await SwitchSemaphore.WaitAsync(cancellationToken);
        try
        {
            var correlationId = GetCorrelationId();
            using var scope = _logger?.BeginScope(new Dictionary<string, object?>
            {
                ["CorrelationId"] = correlationId
            });

            _logger?.LogInformation("Preparing native TV-only activation. CorrelationId: {CorrelationId}", correlationId);

            cancellationToken.ThrowIfCancellationRequested();

            var configuration = QueryDisplayConfiguration(NativeMethods.QDC_ALL_PATHS);
            var targetContext = BuildDisplayContexts(configuration)
                .FirstOrDefault(context => context.Identifier.Matches(display));

            if (targetContext is null)
            {
                return OperationResult.Failure(
                    $"Configured TV '{display}' is not connected.",
                    "display_not_connected");
            }

            if (targetContext.SourceDeviceName is null)
            {
                return OperationResult.Failure(
                    $"Windows did not expose a source device name for '{targetContext.FriendlyName}'.",
                    "display_source_name_unavailable");
            }

            var selectedSourceMode = SelectBestSourceModeForExplicitActivation(targetContext, preferredMode);
            if (!selectedSourceMode.Succeeded || selectedSourceMode.SourceMode is null)
            {
                return OperationResult.Failure(
                    selectedSourceMode.Message,
                    selectedSourceMode.ErrorCode);
            }

            _logger?.LogInformation("Switching to TV-only topology");
            _logger?.LogInformation(
                "Applying {Width}x{Height} at {RefreshRate:0.##} Hz",
                selectedSourceMode.SourceMode.Width,
                selectedSourceMode.SourceMode.Height,
                selectedSourceMode.SourceMode.RefreshRateHz);

            var details = new List<string>
            {
                "Attempting explicit single-display activation"
            };

            if (!dryRun && !targetContext.IsActive)
            {
                var preActivationResult = await EnsureDisplayIsActiveBeforeSingleDisplayActivationAsync(
                    display,
                    details,
                    cancellationToken);

                if (!preActivationResult.Succeeded)
                {
                    return preActivationResult;
                }

                if (preActivationResult.Details.Count > details.Count)
                {
                    details = preActivationResult.Details.ToList();
                }
            }

            var activationConfiguration = QueryDisplayConfiguration(NativeMethods.QDC_ALL_PATHS);
            var activationContexts = BuildDisplayContexts(activationConfiguration);
            var activationTarget = activationContexts.FirstOrDefault(context => context.Identifier.Matches(display));
            if (activationTarget is null)
            {
                return OperationResult.Failure(
                    $"Configured TV '{display}' is not connected.",
                    "display_not_connected",
                    details: details);
            }

            var activationResult = TryApplySingleDisplayDeviceSettings(
                activationContexts,
                activationTarget,
                selectedSourceMode.SourceMode,
                dryRun,
                details);

            if (activationResult.Succeeded)
            {
                _logger?.LogInformation("Couch display active");
                return OperationResult.Success(
                    $"Activated {targetContext.FriendlyName} using {selectedSourceMode.SourceMode}.",
                    outcome: "single_display_device_settings",
                    details: activationResult.Details);
            }

            if (!dryRun && string.Equals(activationResult.ErrorCode, "display_switch_verification_failed", StringComparison.OrdinalIgnoreCase))
            {
                var fallbackResult = await TryActivateAfterExtendFallbackAsync(
                    display,
                    selectedSourceMode.SourceMode,
                    activationResult.Details.ToList(),
                    cancellationToken);

                if (fallbackResult.Succeeded)
                {
                    _logger?.LogInformation("Couch display active after extend fallback.");
                    return fallbackResult;
                }

                return fallbackResult;
            }

            return OperationResult.Failure(
                activationResult.Message,
                activationResult.ErrorCode,
                outcome: activationResult.Outcome,
                details: activationResult.Details);
        }
        finally
        {
            SwitchSemaphore.Release();
        }
    }

    private async Task<OperationResult> TryActivateAfterExtendFallbackAsync(
        DisplayIdentifier display,
        DisplayMode targetMode,
        List<string> seedDetails,
        CancellationToken cancellationToken)
    {
        var details = new List<string>(seedDetails)
        {
            "Using activation fallback: DisplaySwitch.exe /extend"
        };

        cancellationToken.ThrowIfCancellationRequested();
        var fallbackExitCode = await _displaySystem.RunDisplaySwitchExtendAsync(cancellationToken);
        details.Add($"DisplaySwitch.exe exited with code {fallbackExitCode}");

        _logger?.LogInformation(
            "Waiting {DelayMs} ms for the extended topology to settle before retrying TV-only activation.",
            (int)ExtendFallbackSettleDelay.TotalMilliseconds);
        await Task.Delay(ExtendFallbackSettleDelay, cancellationToken);

        var extendedConfiguration = QueryDisplayConfiguration(NativeMethods.QDC_ALL_PATHS);
        var extendedContexts = BuildDisplayContexts(extendedConfiguration);
        var refreshedTarget = extendedContexts.FirstOrDefault(context => context.Identifier.Matches(display));
        if (refreshedTarget is null)
        {
            return OperationResult.Failure(
                $"Activation fallback did not leave '{display}' connected.",
                "display_switch_verification_failed",
                outcome: "single_display_extend_fallback",
                details: details);
        }

        details.Add("Attempting explicit single-display activation after extend fallback");
        var retryResult = TryApplySingleDisplayDeviceSettings(
            extendedContexts,
            refreshedTarget,
            targetMode,
            dryRun: false,
            details);

        if (retryResult.Succeeded)
        {
            return OperationResult.Success(
                retryResult.Message,
                outcome: "single_display_device_settings_after_extend_fallback",
                details: retryResult.Details);
        }

        return OperationResult.Failure(
            retryResult.Message,
            retryResult.ErrorCode,
            outcome: "single_display_extend_fallback_failed",
            details: retryResult.Details);
    }

    private async Task<OperationResult> EnsureDisplayIsActiveBeforeSingleDisplayActivationAsync(
        DisplayIdentifier display,
        List<string> seedDetails,
        CancellationToken cancellationToken)
    {
        var details = new List<string>(seedDetails)
        {
            "Target display is not active yet; attempting DisplaySwitch.exe /extend before single-display activation"
        };

        cancellationToken.ThrowIfCancellationRequested();
        var fallbackExitCode = await _displaySystem.RunDisplaySwitchExtendAsync(cancellationToken);
        details.Add($"DisplaySwitch.exe exited with code {fallbackExitCode}");

        _logger?.LogInformation(
            "Waiting {DelayMs} ms for the extended topology to settle before checking whether the target display became active.",
            (int)ExtendFallbackSettleDelay.TotalMilliseconds);
        await Task.Delay(ExtendFallbackSettleDelay, cancellationToken);

        var extendedConfiguration = QueryDisplayConfiguration(NativeMethods.QDC_ALL_PATHS);
        var extendedContexts = BuildDisplayContexts(extendedConfiguration);
        var refreshedTarget = extendedContexts.FirstOrDefault(context => context.Identifier.Matches(display));
        if (refreshedTarget is null)
        {
            return OperationResult.Failure(
                $"Activation fallback did not leave '{display}' connected.",
                "display_switch_verification_failed",
                outcome: "single_display_pre_activation_extend_failed",
                details: details);
        }

        if (!refreshedTarget.IsActive)
        {
            details.Add($"Aborted single-display activation because '{refreshedTarget.FriendlyName}' is still inactive after extend fallback.");
            return OperationResult.Failure(
                $"'{refreshedTarget.FriendlyName}' did not become active after the extend fallback. Leaving the current desktop display unchanged.",
                "display_target_inactive_after_extend",
                outcome: "single_display_pre_activation_extend_failed",
                details: details);
        }

        details.Add($"Confirmed '{refreshedTarget.FriendlyName}' is active after extend fallback.");
        return OperationResult.Success(
            $"Confirmed '{refreshedTarget.FriendlyName}' is active after extend fallback.",
            outcome: "single_display_pre_activation_extend",
            details: details);
    }

    public async Task<OperationResult> RestoreSnapshotAsync(
        DisplaySnapshot snapshot,
        RestoreSnapshotOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        EnsureWindows();

        ArgumentNullException.ThrowIfNull(snapshot);
        var validation = snapshot.Validate();
        if (!validation.Succeeded)
        {
            return validation;
        }

        await SwitchSemaphore.WaitAsync(cancellationToken);
        try
        {
            options ??= new RestoreSnapshotOptions();
            var correlationId = GetCorrelationId();
            using var scope = _logger?.BeginScope(new Dictionary<string, object?>
            {
                ["CorrelationId"] = correlationId
            });

            _logger?.LogInformation("Restoring display snapshot {SnapshotId}. CorrelationId: {CorrelationId}", snapshot.SnapshotId, correlationId);

            cancellationToken.ThrowIfCancellationRequested();
            var details = new List<string>
            {
                $"Loaded desktop snapshot from {snapshot.CapturedAtUtc.LocalDateTime:yyyy-MM-dd HH:mm}"
            };
            var currentConfiguration = QueryDisplayConfiguration(NativeMethods.QDC_ALL_PATHS);
            var contexts = BuildDisplayContexts(currentConfiguration);
            var plan = BuildRestorePlan(snapshot, contexts);
            details.AddRange(plan.Details);
            details.Add($"Selected baseline restore strategy: {DescribeRestoreStrategy(plan.Strategy)}");

            if (options.ForceFallback)
            {
                details.Add("Force-fallback option enabled");
                return await ExecuteFallbackRecoveryAsync(
                    snapshot,
                    contexts,
                    details,
                    options.DryRun,
                    cancellationToken);
            }

            switch (plan.Strategy)
            {
                case BaselineRestoreStrategy.SingleDisplayDeviceSettings:
                {
                    var singleDisplayResult = await TryRestoreSingleDisplayAsync(
                        snapshot,
                        plan,
                        options.DryRun,
                        details,
                        cancellationToken);

                    if (singleDisplayResult.Succeeded || options.DryRun)
                    {
                        return singleDisplayResult;
                    }

                    details = MergeDetails(details, singleDisplayResult.Details);
                    break;
                }
                case BaselineRestoreStrategy.ExactNative:
                {
                    var exactResult = await TryApplyRestoreConfigurationAsync(
                        snapshot,
                        plan,
                        plan.ExactConfiguration!.Value,
                        RestoreAttemptKind.Exact,
                        options.DryRun,
                        details,
                        cancellationToken);

                    if (exactResult.Succeeded)
                    {
                        return exactResult;
                    }

                    details = MergeDetails(details, exactResult.Details);
                    break;
                }
                case BaselineRestoreStrategy.BestEffortNative:
                {
                    var bestEffortResult = await TryApplyRestoreConfigurationAsync(
                        snapshot,
                        plan,
                        plan.BestEffortConfiguration!.Value,
                        RestoreAttemptKind.BestEffort,
                        options.DryRun,
                        details,
                        cancellationToken);

                    if (bestEffortResult.Succeeded)
                    {
                        return bestEffortResult;
                    }

                    details = MergeDetails(details, bestEffortResult.Details);
                    break;
                }
            }

            if (plan.Matches.Count > 1 && plan.MissingPaths.Count == 0)
            {
                var deviceSettingsResult = TryRestoreMultipleDisplaysWithDeviceSettings(
                    snapshot,
                    plan,
                    options.DryRun,
                    details);

                if (deviceSettingsResult.Succeeded || options.DryRun)
                {
                    return deviceSettingsResult;
                }

                details = MergeDetails(details, deviceSettingsResult.Details);
            }

            return await ExecuteFallbackRecoveryAsync(
                snapshot,
                contexts,
                details,
                options.DryRun,
                cancellationToken);
        }
        finally
        {
            SwitchSemaphore.Release();
        }
    }

    private OperationResult TryRestoreMultipleDisplaysWithDeviceSettings(
        DisplaySnapshot snapshot,
        RestorePlan plan,
        bool dryRun,
        List<string> seedDetails)
    {
        var details = new List<string>(seedDetails)
        {
            "Attempting registry-independent multi-display restoration"
        };
        var targetSourceNames = plan.Matches
            .Select(static match => match.Context.SourceDeviceName)
            .Where(static sourceName => !string.IsNullOrWhiteSpace(sourceName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var currentContexts = BuildDisplayContexts(QueryDisplayConfiguration(NativeMethods.QDC_ALL_PATHS));

        foreach (var context in currentContexts
                     .Where(context =>
                         !string.IsNullOrWhiteSpace(context.SourceDeviceName) &&
                         !targetSourceNames.Contains(context.SourceDeviceName!))
                     .GroupBy(static context => context.SourceDeviceName!, StringComparer.OrdinalIgnoreCase)
                     .Select(static group => group.First()))
        {
            details.Add($"Detaching {context.FriendlyName}");
            var detachResult = _displaySystem.ChangeDisplaySettingsEx(
                context.SourceDeviceName!,
                CreateDetachedDisplayMode(),
                NativeMethods.CDS_UPDATEREGISTRY | NativeMethods.CDS_NORESET);
            if (detachResult != NativeMethods.DISP_CHANGE_SUCCESSFUL)
            {
                details.Add($"Detaching {context.FriendlyName} failed with error {detachResult}");
                return OperationResult.Failure(
                    $"Desktop snapshot could not be restored while detaching '{context.FriendlyName}'.",
                    "multi_display_detach_failed",
                    outcome: "device_settings_failed",
                    details: details);
            }
        }

        foreach (var match in plan.Matches.OrderByDescending(static match => match.SnapshotPath.IsPrimary))
        {
            if (string.IsNullOrWhiteSpace(match.Context.SourceDeviceName))
            {
                details.Add($"Windows did not expose a source device name for '{match.Context.FriendlyName}'");
                return OperationResult.Failure(
                    $"Desktop snapshot could not be restored because '{match.Context.FriendlyName}' has no source device name.",
                    "multi_display_source_name_unavailable",
                    outcome: "device_settings_failed",
                    details: details);
            }

            if (!TryBuildEnabledDisplaySettings(match.Context, match.SnapshotPath, out var mode, out var buildError))
            {
                details.Add(buildError);
                return OperationResult.Failure(
                    $"Desktop snapshot could not be restored because '{match.Context.FriendlyName}' could not be configured.",
                    "multi_display_target_mode_unavailable",
                    outcome: "device_settings_failed",
                    details: details);
            }

            details.Add($"Configuring {match.Context.FriendlyName}{(match.SnapshotPath.IsPrimary ? " as primary display" : string.Empty)}");
            var flags = NativeMethods.CDS_UPDATEREGISTRY | NativeMethods.CDS_NORESET;
            if (match.SnapshotPath.IsPrimary)
            {
                flags |= NativeMethods.CDS_SET_PRIMARY;
            }

            var applyResult = _displaySystem.ChangeDisplaySettingsEx(match.Context.SourceDeviceName!, mode, flags);
            if (applyResult != NativeMethods.DISP_CHANGE_SUCCESSFUL)
            {
                details.Add($"Configuring {match.Context.FriendlyName} failed with error {applyResult}");
                return OperationResult.Failure(
                    $"Desktop snapshot could not be restored while configuring '{match.Context.FriendlyName}'.",
                    "multi_display_target_apply_failed",
                    outcome: "device_settings_failed",
                    details: details);
            }
        }

        if (dryRun)
        {
            details.Add("Dry run planned registry-independent multi-display restoration");
            return OperationResult.Success(
                $"Dry run validated registry-independent restoration of desktop snapshot {snapshot.SnapshotId}.",
                outcome: "multi_display_device_settings",
                details: details);
        }

        var commitResult = _displaySystem.CommitDisplaySettings();
        if (commitResult != NativeMethods.DISP_CHANGE_SUCCESSFUL)
        {
            details.Add($"Committing registry-independent multi-display restoration failed with error {commitResult}");
            return OperationResult.Failure(
                "Desktop snapshot could not be restored while committing the multi-display layout.",
                "multi_display_commit_failed",
                outcome: "device_settings_failed",
                details: details);
        }

        var verification = VerifyRestoredTopology(snapshot, plan, RestoreAttemptKind.Exact);
        details.AddRange(verification.Details);
        if (!verification.Succeeded)
        {
            return OperationResult.Failure(
                verification.Message ?? "Desktop snapshot verification failed.",
                verification.ErrorCode ?? "multi_display_verification_failed",
                outcome: "device_settings_failed",
                details: details);
        }

        return OperationResult.Success(
            "Desktop Mode restored",
            outcome: "multi_display_device_settings",
            details: details);
    }

    private async Task<OperationResult> TryApplyRestoreConfigurationAsync(
        DisplaySnapshot snapshot,
        RestorePlan plan,
        (DISPLAYCONFIG_PATH_INFO[] Paths, DISPLAYCONFIG_MODE_INFO[] Modes) configuration,
        RestoreAttemptKind attemptKind,
        bool dryRun,
        List<string> seedDetails,
        CancellationToken cancellationToken)
    {
        var details = new List<string>(seedDetails);
        var label = attemptKind switch
        {
            RestoreAttemptKind.SingleDisplayTopologyOnly => "topology-only single-display",
            RestoreAttemptKind.SingleDisplayTopologyOnlyAfterFallback => "topology-only single-display after fallback",
            RestoreAttemptKind.Exact => "exact",
            _ => "best-effort"
        };
        details.Add($"Attempting {label} native restoration");

        var validationResult = ApplyDisplayConfiguration(configuration, validateOnly: true);
        if (validationResult != 0)
        {
            details.Add($"Native {label} validation failed with error {validationResult}");
            return await RecoverAfterFailedRestoreAttemptAsync(
                snapshot,
                plan,
                dryRun,
                details,
                validationResult,
                $"{label}_validation_failed",
                cancellationToken);
        }

        if (dryRun)
        {
            details.Add($"Dry run validated the {label} restoration path");
            return attemptKind switch
            {
                RestoreAttemptKind.Exact => OperationResult.Success(
                    $"Dry run validated exact restoration of desktop snapshot {snapshot.SnapshotId}.",
                    outcome: "exact",
                    details: details),
                RestoreAttemptKind.SingleDisplayTopologyOnly => OperationResult.Success(
                    $"Dry run validated topology-only single-display restoration of desktop snapshot {snapshot.SnapshotId}.",
                    outcome: "single_display_topology_only",
                    details: details),
                RestoreAttemptKind.SingleDisplayTopologyOnlyAfterFallback => OperationResult.Success(
                    $"Dry run validated topology-only single-display restoration after emergency fallback for desktop snapshot {snapshot.SnapshotId}.",
                    outcome: "single_display_topology_only_after_fallback",
                    details: details),
                _ => OperationResult.PartialSuccess(
                    $"Dry run validated best-effort restoration of desktop snapshot {snapshot.SnapshotId}.",
                    outcome: "best_effort",
                    details: details)
            };
        }

        cancellationToken.ThrowIfCancellationRequested();

        var applyResult = ApplyDisplayConfiguration(configuration, validateOnly: false);
        if (applyResult != 0)
        {
            details.Add($"Native {label} apply failed with error {applyResult}");
            return await RecoverAfterFailedRestoreAttemptAsync(
                snapshot,
                plan,
                dryRun,
                details,
                applyResult,
                $"{label}_apply_failed",
                cancellationToken);
        }

        var verification = VerifyRestoredTopology(snapshot, plan, attemptKind);
        details.AddRange(verification.Details);
        if (!verification.Succeeded)
        {
            return await RecoverAfterFailedRestoreAttemptAsync(
                snapshot,
                plan,
                dryRun,
                details,
                null,
                verification.ErrorCode ?? $"{label}_verification_failed",
                cancellationToken,
                verification.Message);
        }

        return attemptKind switch
        {
            RestoreAttemptKind.Exact => OperationResult.Success(
                "Desktop Mode restored",
                outcome: "exact",
                details: details),
            RestoreAttemptKind.SingleDisplayTopologyOnly => OperationResult.Success(
                "Desktop Mode restored",
                outcome: "single_display_topology_only",
                details: details),
            RestoreAttemptKind.SingleDisplayTopologyOnlyAfterFallback => OperationResult.Success(
                "Desktop Mode restored after topology-only emergency fallback recovery",
                outcome: "single_display_topology_only_after_fallback",
                details: details),
            _ => OperationResult.PartialSuccess(
                "Desktop Mode restored with best-effort topology",
                outcome: "best_effort",
                details: details)
        };
    }

    private static string GetCorrelationId() =>
        Activity.Current?.GetBaggageItem("OperationId")
        ?? Activity.Current?.GetTagItem("OperationId")?.ToString()
        ?? Activity.Current?.Id
        ?? Guid.NewGuid().ToString("N");

    private async Task<OperationResult> TryRestoreSingleDisplayAsync(
        DisplaySnapshot snapshot,
        RestorePlan plan,
        bool dryRun,
        List<string> seedDetails,
        CancellationToken cancellationToken)
    {
        var details = new List<string>(seedDetails)
        {
            "Attempting explicit single-display restore"
        };

        var targetMatch = plan.Matches[0];
        var currentContexts = BuildDisplayContexts(QueryDisplayConfiguration(NativeMethods.QDC_ALL_PATHS));
        var currentByIdentifier = currentContexts.ToDictionary(context => context.Identifier, context => context);
        var targetContext = currentByIdentifier.TryGetValue(targetMatch.Context.Identifier, out var refreshedTarget)
            ? refreshedTarget
            : targetMatch.Context;

        var result = TryApplySingleDisplayDeviceSettings(currentContexts, targetContext, targetMatch.SnapshotPath, dryRun, details);
        if (result.Succeeded || dryRun)
        {
            return OperationResult.Success(
                result.Message,
                outcome: result.Outcome,
                details: result.Details);
        }

        return await RecoverAfterFailedRestoreAttemptAsync(
            snapshot,
            plan,
            dryRun,
            result.Details.ToList(),
            null,
            result.ErrorCode ?? "single_display_explicit_failed",
            cancellationToken,
            result.Message);
    }

    private async Task<OperationResult> RecoverAfterFailedRestoreAttemptAsync(
        DisplaySnapshot snapshot,
        RestorePlan plan,
        bool dryRun,
        List<string> details,
        int? errorCode,
        string errorSuffix,
        CancellationToken cancellationToken,
        string? messageOverride = null)
    {
        if (!dryRun)
        {
            details = await EnsureAnyDisplayActiveAfterFailureAsync(plan, details, cancellationToken);
        }

        return OperationResult.Failure(
            messageOverride ?? $"Desktop snapshot {snapshot.SnapshotId} could not be restored during {errorSuffix.Replace('_', ' ')}.",
            $"display_restore_{errorSuffix}",
            outcome: "native_failed",
            details: details);
    }

    private async Task<OperationResult> ExecuteFallbackRecoveryAsync(
        DisplaySnapshot snapshot,
        IReadOnlyList<DisplayPathContext> contexts,
        List<string> seedDetails,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        var details = new List<string>(seedDetails)
        {
            "Using emergency fallback: DisplaySwitch.exe /extend"
        };

        if (dryRun)
        {
            details.Add("Dry run skipped execution of DisplaySwitch.exe /extend");
            return OperationResult.PartialSuccess(
                $"Dry run would use emergency fallback for desktop snapshot {snapshot.SnapshotId}.",
                outcome: "fallback",
                details: details);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var fallbackExitCode = await _displaySystem.RunDisplaySwitchExtendAsync(cancellationToken);
        details.Add($"DisplaySwitch.exe exited with code {fallbackExitCode}");

        _logger?.LogInformation(
            "Waiting {DelayMs} ms for the extended desktop fallback topology to settle before retrying snapshot restoration.",
            (int)ExtendFallbackSettleDelay.TotalMilliseconds);
        await Task.Delay(ExtendFallbackSettleDelay, cancellationToken);

        var activeAfterFallback = BuildDisplayContexts(QueryDisplayConfiguration(NativeMethods.QDC_ONLY_ACTIVE_PATHS));
        var singleDisplayRecoveryResult = await TryRestoreSingleDisplayAfterFallbackAsync(
            snapshot,
            details,
            cancellationToken);

        if (singleDisplayRecoveryResult is not null)
        {
            if (singleDisplayRecoveryResult.Succeeded)
            {
                return singleDisplayRecoveryResult;
            }

            details = MergeDetails(details, singleDisplayRecoveryResult.Details);
            activeAfterFallback = BuildDisplayContexts(QueryDisplayConfiguration(NativeMethods.QDC_ONLY_ACTIVE_PATHS));
        }

        if (activeAfterFallback.Any(static context => context.IsActive))
        {
            details.Add("Emergency fallback left at least one display active");
            return OperationResult.PartialSuccess(
                "Desktop Mode restored with emergency fallback",
                outcome: "fallback",
                details: details);
        }

        details.Add("Emergency fallback did not activate any displays");
        details = await EnsureAnyDisplayActiveAfterFailureAsync(
            new RestorePlan(BaselineRestoreStrategy.BestEffortNative, null, null, [], [], []),
            details,
            cancellationToken);

        var finalContexts = BuildDisplayContexts(QueryDisplayConfiguration(NativeMethods.QDC_ALL_PATHS));
        if (finalContexts.Any(static context => context.IsActive))
        {
            details.Add("Safe mode activation recovered an active display");
            return OperationResult.PartialSuccess(
                "Desktop snapshot could not be fully restored, but a safe display was activated.",
                outcome: "safe_mode",
                details: details);
        }

        return OperationResult.Failure(
            $"Desktop snapshot {snapshot.SnapshotId} could not be restored and no fallback display could be activated.",
            "display_restore_fallback_failed",
            outcome: "fallback_failed",
            details: details);
    }

    private async Task<OperationResult?> TryRestoreSingleDisplayAfterFallbackAsync(
        DisplaySnapshot snapshot,
        List<string> seedDetails,
        CancellationToken cancellationToken)
    {
        if (snapshot.Paths.Count(static path => path.IsActive) != 1)
        {
            return null;
        }

        var currentContexts = BuildDisplayContexts(QueryDisplayConfiguration(NativeMethods.QDC_ALL_PATHS));
        var snapshotPath = snapshot.Paths.Single(static path => path.IsActive);
        var snapshotDisplay = snapshot.Displays.FirstOrDefault(display => display.Identifier.Matches(snapshotPath.Identifier));
        var matchedTarget = MatchSnapshotPath(snapshotPath, snapshotDisplay, currentContexts);
        if (matchedTarget is null)
        {
            return null;
        }

        var plan = BuildRestorePlan(snapshot, currentContexts);
        var details = new List<string>(seedDetails)
        {
            "Attempting explicit single-display restoration after emergency fallback"
        };

        var result = TryApplySingleDisplayDeviceSettings(
            currentContexts,
            matchedTarget,
            snapshotPath,
            dryRun: false,
            details);

        if (result.Succeeded)
        {
            return OperationResult.Success(
                result.Message,
                outcome: result.Outcome,
                details: result.Details);
        }

        return await RecoverAfterFailedRestoreAttemptAsync(
            snapshot,
            plan,
            dryRun: false,
            result.Details.ToList(),
            null,
            result.ErrorCode ?? "single_display_explicit_after_fallback_failed",
            cancellationToken,
            result.Message);
    }

    private async Task<List<string>> EnsureAnyDisplayActiveAfterFailureAsync(
        RestorePlan plan,
        List<string> details,
        CancellationToken cancellationToken)
    {
        var contexts = BuildDisplayContexts(QueryDisplayConfiguration(NativeMethods.QDC_ALL_PATHS));
        if (contexts.Any(static context => context.IsActive))
        {
            return details;
        }

        details.Add("No active display detected after failed restoration");
        var safeTarget = plan.Matches
            .Select(match => match.Context)
            .Concat(contexts)
            .FirstOrDefault();

        if (safeTarget is null)
        {
            details.Add("No connected display was available for safe-mode recovery");
            return details;
        }

        var safeConfiguration = BuildSafeModeConfiguration(safeTarget);
        if (safeConfiguration is null)
        {
            details.Add($"Could not build a safe mode for {safeTarget.FriendlyName}");
            return details;
        }

        var validationResult = ApplyDisplayConfiguration(safeConfiguration.Value, validateOnly: true);
        if (validationResult != 0)
        {
            details.Add($"Safe-mode validation failed with error {validationResult}");
            return details;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var applyResult = ApplyDisplayConfiguration(safeConfiguration.Value, validateOnly: false);
        if (applyResult != 0)
        {
            details.Add($"Safe-mode apply failed with error {applyResult}");
            return details;
        }

        details.Add($"Activated safe mode on {safeTarget.FriendlyName}");
        return details;
    }

    private DeviceSettingsRestoreResult TryApplySingleDisplayDeviceSettings(
        IReadOnlyList<DisplayPathContext> contexts,
        DisplayPathContext targetContext,
        DisplayPathSnapshot snapshotPath,
        bool dryRun,
        List<string> seedDetails)
    {
        var width = snapshotPath.SourceMode?.Width ?? 0;
        var height = snapshotPath.SourceMode?.Height ?? 0;
        var refreshRateHz = snapshotPath.RefreshRate.Hertz;

        return TryApplySingleDisplayDeviceSettings(
            contexts,
            targetContext,
            width,
            height,
            refreshRateHz,
            snapshotPath.SourceMode?.Position.X ?? 0,
            snapshotPath.SourceMode?.Position.Y ?? 0,
            dryRun,
            seedDetails);
    }

    private DeviceSettingsRestoreResult TryApplySingleDisplayDeviceSettings(
        IReadOnlyList<DisplayPathContext> contexts,
        DisplayPathContext targetContext,
        DisplayMode targetMode,
        bool dryRun,
        List<string> seedDetails) =>
        TryApplySingleDisplayDeviceSettings(
            contexts,
            targetContext,
            (uint)targetMode.Width,
            (uint)targetMode.Height,
            targetMode.RefreshRateHz,
            0,
            0,
            dryRun,
            seedDetails);

    private DeviceSettingsRestoreResult TryApplySingleDisplayDeviceSettings(
        IReadOnlyList<DisplayPathContext> contexts,
        DisplayPathContext targetContext,
        uint width,
        uint height,
        decimal refreshRateHz,
        int positionX,
        int positionY,
        bool dryRun,
        List<string> seedDetails)
    {
        var details = new List<string>(seedDetails);
        if (string.IsNullOrWhiteSpace(targetContext.SourceDeviceName))
        {
            details.Add($"Windows did not expose a source device name for '{targetContext.FriendlyName}'");
            return DeviceSettingsRestoreResult.Failure(
                $"Desktop snapshot could not be restored because '{targetContext.FriendlyName}' has no source device name.",
                "single_display_source_name_unavailable",
                details);
        }

        if (!TryBuildEnabledDisplaySettings(targetContext, width, height, refreshRateHz, positionX, positionY, out var enabledTargetMode, out var targetBuildError))
        {
            details.Add(targetBuildError);
            return DeviceSettingsRestoreResult.Failure(
                $"Desktop snapshot could not be restored because '{targetContext.FriendlyName}' could not be configured.",
                "single_display_target_mode_unavailable",
                details);
        }

        var uniqueSourceNames = contexts
            .Where(static context => !string.IsNullOrWhiteSpace(context.SourceDeviceName))
            .GroupBy(context => context.SourceDeviceName!, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToArray();

        foreach (var context in uniqueSourceNames.Where(context =>
                     !StringComparer.OrdinalIgnoreCase.Equals(context.SourceDeviceName, targetContext.SourceDeviceName)))
        {
            var detachMode = CreateDetachedDisplayMode();
            details.Add($"Detaching {context.FriendlyName}");

            var detachResult = _displaySystem.ChangeDisplaySettingsEx(
                context.SourceDeviceName!,
                detachMode,
                NativeMethods.CDS_UPDATEREGISTRY | NativeMethods.CDS_NORESET);

            if (detachResult != NativeMethods.DISP_CHANGE_SUCCESSFUL)
            {
                details.Add($"Detaching {context.FriendlyName} failed with error {detachResult}");
                return DeviceSettingsRestoreResult.Failure(
                    $"Desktop snapshot could not be restored while detaching '{context.FriendlyName}'.",
                    "single_display_detach_failed",
                    details);
            }
        }

        details.Add($"Configuring {targetContext.FriendlyName} as primary display");
        var targetFlags = NativeMethods.CDS_UPDATEREGISTRY | NativeMethods.CDS_NORESET | NativeMethods.CDS_SET_PRIMARY;
        var targetResult = _displaySystem.ChangeDisplaySettingsEx(targetContext.SourceDeviceName!, enabledTargetMode, targetFlags);
        if (targetResult != NativeMethods.DISP_CHANGE_SUCCESSFUL)
        {
            details.Add($"Configuring {targetContext.FriendlyName} failed with error {targetResult}");
            return DeviceSettingsRestoreResult.Failure(
                $"Desktop snapshot could not be restored while configuring '{targetContext.FriendlyName}'.",
                "single_display_target_apply_failed",
                details);
        }

        if (dryRun)
        {
            details.Add("Dry run planned explicit single-display restore");
            return DeviceSettingsRestoreResult.Success(
                $"Dry run validated explicit single-display restoration of desktop snapshot.",
                "single_display_device_settings",
                details);
        }

        var commitResult = _displaySystem.CommitDisplaySettings();
        if (commitResult != NativeMethods.DISP_CHANGE_SUCCESSFUL)
        {
            details.Add($"Committing explicit single-display restore failed with error {commitResult}");
            return DeviceSettingsRestoreResult.Failure(
                "Desktop snapshot could not be restored while committing the explicit single-display restore.",
                "single_display_commit_failed",
                details);
        }

        var verification = VerifySingleActiveDisplay(targetContext.Identifier);
        details.Add(verification.Message ?? $"Verified '{targetContext.FriendlyName}' as the only active display.");
        if (!verification.Succeeded)
        {
            return DeviceSettingsRestoreResult.Failure(
                verification.Message ?? $"Verification failed for '{targetContext.FriendlyName}'.",
                verification.ErrorCode ?? "single_display_verification_failed",
                details);
        }

        return DeviceSettingsRestoreResult.Success(
            "Desktop Mode restored",
            "single_display_device_settings",
            details);
    }

    private bool TryBuildEnabledDisplaySettings(
        DisplayPathContext targetContext,
        DisplayPathSnapshot snapshotPath,
        out DEVMODE mode,
        out string error)
    {
        var width = snapshotPath.SourceMode?.Width ?? 0;
        var height = snapshotPath.SourceMode?.Height ?? 0;
        var refreshRateHz = snapshotPath.RefreshRate.Hertz;
        var positionX = snapshotPath.SourceMode?.Position.X ?? 0;
        var positionY = snapshotPath.SourceMode?.Position.Y ?? 0;

        return TryBuildEnabledDisplaySettings(targetContext, width, height, refreshRateHz, positionX, positionY, out mode, out error);
    }

    private bool TryBuildEnabledDisplaySettings(
        DisplayPathContext targetContext,
        uint width,
        uint height,
        decimal refreshRateHz,
        int positionX,
        int positionY,
        out DEVMODE mode,
        out string error)
    {
        mode = new DEVMODE
        {
            dmSize = (ushort)Marshal.SizeOf<DEVMODE>()
        };

        error = string.Empty;
        var sourceName = targetContext.SourceDeviceName!;
        if (!_displaySystem.EnumDisplaySettingsEx(sourceName, NativeMethods.ENUM_CURRENT_SETTINGS, ref mode, 0))
        {
            mode = new DEVMODE
            {
                dmSize = (ushort)Marshal.SizeOf<DEVMODE>()
            };
        }

        width = width == 0 ? mode.dmPelsWidth : width;
        height = height == 0 ? mode.dmPelsHeight : height;
        if (width == 0 || height == 0)
        {
            error = $"No usable mode information was available for '{targetContext.FriendlyName}'.";
            return false;
        }

        mode.dmPelsWidth = width;
        mode.dmPelsHeight = height;
        mode.dmPositionX = positionX;
        mode.dmPositionY = positionY;
        mode.dmFields = NativeMethods.DM_POSITION | NativeMethods.DM_PELSWIDTH | NativeMethods.DM_PELSHEIGHT;

        if (mode.dmBitsPerPel != 0)
        {
            mode.dmFields |= NativeMethods.DM_BITSPERPEL;
        }

        if (refreshRateHz > 0)
        {
            mode.dmDisplayFrequency = (uint)Math.Round(refreshRateHz, MidpointRounding.AwayFromZero);
            mode.dmFields |= NativeMethods.DM_DISPLAYFREQUENCY;
        }

        return true;
    }

    private static DEVMODE CreateDetachedDisplayMode() =>
        new()
        {
            dmSize = (ushort)Marshal.SizeOf<DEVMODE>(),
            dmPelsWidth = 0,
            dmPelsHeight = 0,
            dmPositionX = 0,
            dmPositionY = 0,
            dmFields = NativeMethods.DM_POSITION | NativeMethods.DM_PELSWIDTH | NativeMethods.DM_PELSHEIGHT
        };

    private RestoreVerificationResult VerifyRestoredTopology(
        DisplaySnapshot snapshot,
        RestorePlan plan,
        RestoreAttemptKind attemptKind)
    {
        var currentPaths = BuildDisplayContexts(QueryDisplayConfiguration(NativeMethods.QDC_ALL_PATHS))
            .Where(static context => context.IsActive)
            .ToArray();
        var currentByIdentifier = currentPaths.ToDictionary(context => context.Identifier, context => context);
        var verificationDetails = new List<string>();

        foreach (var match in plan.Matches)
        {
            if (!currentByIdentifier.TryGetValue(match.Context.Identifier, out var restored))
            {
                return RestoreVerificationResult.Failure(
                    $"Verification failed: '{match.Context.FriendlyName}' is not active after restore.",
                    "display_restore_verification_failed",
                    verificationDetails);
            }

            var position = restored.SourceMode?.sourceMode.position;
            if (match.SnapshotPath.SourceMode is not null &&
                position.HasValue &&
                (position.Value.x != match.SnapshotPath.SourceMode.Position.X ||
                 position.Value.y != match.SnapshotPath.SourceMode.Position.Y))
            {
                return RestoreVerificationResult.Failure(
                    $"Verification failed: '{match.Context.FriendlyName}' is not at the saved desktop position.",
                    "display_restore_verification_failed",
                    verificationDetails);
            }

            verificationDetails.Add($"Verified {match.Context.FriendlyName}");

            if ((attemptKind is not RestoreAttemptKind.Exact and not RestoreAttemptKind.SingleDisplayTopologyOnly and not RestoreAttemptKind.SingleDisplayTopologyOnlyAfterFallback) ||
                match.SnapshotPath.SourceMode is null ||
                restored.SourceMode is null)
            {
                continue;
            }

            if (restored.SourceMode.Value.sourceMode.width != match.SnapshotPath.SourceMode.Width ||
                restored.SourceMode.Value.sourceMode.height != match.SnapshotPath.SourceMode.Height)
            {
                return RestoreVerificationResult.Failure(
                    $"Verification failed: '{match.Context.FriendlyName}' resolution does not match the saved snapshot.",
                    "display_restore_verification_failed",
                    verificationDetails);
            }
        }

        var expectedActiveCount = snapshot.Paths.Count(static path => path.IsActive);
        if ((attemptKind == RestoreAttemptKind.Exact ||
             attemptKind == RestoreAttemptKind.SingleDisplayTopologyOnly ||
             attemptKind == RestoreAttemptKind.SingleDisplayTopologyOnlyAfterFallback) &&
            currentPaths.Length != expectedActiveCount)
        {
            return RestoreVerificationResult.Failure(
                $"Verification failed: expected {expectedActiveCount} active displays but found {currentPaths.Length}.",
                "display_restore_verification_failed",
                verificationDetails);
        }

        return RestoreVerificationResult.Success(verificationDetails);
    }

    private RestorePlan BuildRestorePlan(
        DisplaySnapshot snapshot,
        IReadOnlyList<DisplayPathContext> contexts)
    {
        var details = new List<string>();
        var activePaths = snapshot.Paths.Where(static path => path.IsActive).ToArray();
        var available = new List<DisplayPathContext>(contexts);
        var matches = new List<RestoreMatch>();
        var missing = new List<DisplayPathSnapshot>();

        foreach (var snapshotPath in activePaths)
        {
            var display = snapshot.Displays.FirstOrDefault(display => display.Identifier.Matches(snapshotPath.Identifier));
            var match = MatchSnapshotPath(snapshotPath, display, available);
            if (match is null)
            {
                missing.Add(snapshotPath);
                details.Add($"Saved target not found: {display?.FriendlyName ?? snapshotPath.Identifier.Value}");
                continue;
            }

            available.Remove(match);
            matches.Add(new RestoreMatch(snapshotPath, match));
            details.Add($"Matched {match.FriendlyName}");
        }

        if (missing.Any(static path => path.IsPrimary))
        {
            details.Add("Saved primary display could not be found");
        }

        if (matches.Count > 0)
        {
            details.Add(BuildTopologyDescription(matches));
            details.AddRange(matches
                .Where(match => match.SnapshotPath.SourceMode is not null)
                .Select(match =>
                    $"Restoring {match.Context.FriendlyName} position {match.SnapshotPath.SourceMode!.Position.X},{match.SnapshotPath.SourceMode.Position.Y}"));
        }

        var exact = missing.Count == 0
            ? BuildExactRestoreConfiguration(matches)
            : null;
        var bestEffort = matches.Count > 0
            ? BuildBestEffortRestoreConfiguration(matches)
            : null;

        var strategy = DetermineRestoreStrategy(snapshot, matches, missing, exact, bestEffort);
        return new RestorePlan(strategy, exact, bestEffort, matches, missing, details);
    }

    private static BaselineRestoreStrategy DetermineRestoreStrategy(
        DisplaySnapshot snapshot,
        IReadOnlyList<RestoreMatch> matches,
        IReadOnlyList<DisplayPathSnapshot> missing,
        (DISPLAYCONFIG_PATH_INFO[] Paths, DISPLAYCONFIG_MODE_INFO[] Modes)? exactConfiguration,
        (DISPLAYCONFIG_PATH_INFO[] Paths, DISPLAYCONFIG_MODE_INFO[] Modes)? bestEffortConfiguration) =>
        snapshot.Paths.Count(static path => path.IsActive) == 1 && matches.Count == 1
            ? BaselineRestoreStrategy.SingleDisplayDeviceSettings
            : exactConfiguration is not null && missing.Count == 0
                ? BaselineRestoreStrategy.ExactNative
                : BaselineRestoreStrategy.BestEffortNative;

    private static string DescribeRestoreStrategy(BaselineRestoreStrategy strategy) =>
        strategy switch
        {
            BaselineRestoreStrategy.SingleDisplayDeviceSettings => "explicit single-display baseline",
            BaselineRestoreStrategy.ExactNative => "native exact snapshot replay",
            _ => "native best-effort snapshot replay"
        };

    private DisplayPathContext? MatchSnapshotPath(
        DisplayPathSnapshot snapshotPath,
        DisplayDevice? display,
        IReadOnlyList<DisplayPathContext> candidates)
    {
        var exact = candidates.FirstOrDefault(context => context.Identifier.Matches(snapshotPath.Identifier));
        if (exact is not null)
        {
            return exact;
        }

        if (display is not null)
        {
            var friendly = candidates.Where(context =>
                StringComparer.OrdinalIgnoreCase.Equals(context.FriendlyName, display.FriendlyName)).ToArray();

            if (friendly.Length == 1)
            {
                return friendly[0];
            }
        }

        return candidates.FirstOrDefault(context =>
            StringComparer.OrdinalIgnoreCase.Equals(context.Path.targetInfo.outputTechnology.ToString(), ParseOutputTechnology(snapshotPath.OutputTechnology).ToString()) &&
            context.SourceMode?.sourceMode.position.x == snapshotPath.SourceMode?.Position.X &&
            context.SourceMode?.sourceMode.position.y == snapshotPath.SourceMode?.Position.Y);
    }

    private (DISPLAYCONFIG_PATH_INFO[] Paths, DISPLAYCONFIG_MODE_INFO[] Modes)? BuildExactRestoreConfiguration(
        IReadOnlyList<RestoreMatch> matches)
    {
        var paths = new DISPLAYCONFIG_PATH_INFO[matches.Count];
        var modes = new DISPLAYCONFIG_MODE_INFO[matches.Count * 2];

        for (var i = 0; i < matches.Count; i++)
        {
            var match = matches[i];
            if (match.SnapshotPath.SourceMode is null || match.SnapshotPath.TargetMode is null)
            {
                return null;
            }

            int sourceModeIndex = i * 2;
            int targetModeIndex = sourceModeIndex + 1;

            paths[i] = CreateRestoredPathInfo(
                match.Context.Path,
                (uint)sourceModeIndex,
                (uint)targetModeIndex,
                match.SnapshotPath);

            modes[sourceModeIndex] = CreateSourceModeInfo(
                match.Context.Path.sourceInfo.adapterId,
                match.Context.Path.sourceInfo.id,
                match.SnapshotPath.SourceMode.Width,
                match.SnapshotPath.SourceMode.Height,
                match.SnapshotPath.SourceMode.Position.X,
                match.SnapshotPath.SourceMode.Position.Y,
                ParsePixelFormat(match.SnapshotPath.SourceMode.PixelFormat));

            modes[targetModeIndex] = CreateTargetModeInfo(
                match.Context.Path.targetInfo.adapterId,
                match.Context.Path.targetInfo.id,
                match.SnapshotPath.TargetMode.ActiveWidth,
                match.SnapshotPath.TargetMode.ActiveHeight,
                match.SnapshotPath.TargetMode.RefreshRate.Numerator,
                match.SnapshotPath.TargetMode.RefreshRate.Denominator,
                ParseScanlineOrdering(match.SnapshotPath.TargetMode.ScanLineOrdering));
        }

        return (paths, modes);
    }

    private (DISPLAYCONFIG_PATH_INFO[] Paths, DISPLAYCONFIG_MODE_INFO[] Modes)? BuildBestEffortRestoreConfiguration(
        IReadOnlyList<RestoreMatch> matches)
    {
        var paths = new DISPLAYCONFIG_PATH_INFO[matches.Count];
        var modes = new DISPLAYCONFIG_MODE_INFO[matches.Count * 2];

        for (var i = 0; i < matches.Count; i++)
        {
            var match = matches[i];
            var modePair = SelectBestEffortRestoreMode(match.Context, match.SnapshotPath);
            if (modePair is null)
            {
                return null;
            }

            var position = match.SnapshotPath.SourceMode?.Position ?? new DisplayPoint(0, 0);
            int sourceModeIndex = i * 2;
            int targetModeIndex = sourceModeIndex + 1;

            paths[i] = CreateRestoredPathInfo(
                match.Context.Path,
                (uint)sourceModeIndex,
                (uint)targetModeIndex,
                match.SnapshotPath,
                new DISPLAYCONFIG_RATIONAL(modePair.TargetMode.RefreshRateNumerator, modePair.TargetMode.RefreshRateDenominator),
                modePair.TargetMode.ScanLineOrdering);

            modes[sourceModeIndex] = CreateSourceModeInfo(
                match.Context.Path.sourceInfo.adapterId,
                match.Context.Path.sourceInfo.id,
                (uint)modePair.SourceMode.Width,
                (uint)modePair.SourceMode.Height,
                position.X,
                position.Y,
                match.Context.SourceMode?.sourceMode.pixelFormat ?? DISPLAYCONFIG_PIXELFORMAT.DISPLAYCONFIG_PIXELFORMAT_32BPP);

            modes[targetModeIndex] = CreateTargetModeInfo(
                match.Context.Path.targetInfo.adapterId,
                match.Context.Path.targetInfo.id,
                modePair.TargetMode.ActiveWidth,
                modePair.TargetMode.ActiveHeight,
                modePair.TargetMode.RefreshRateNumerator,
                modePair.TargetMode.RefreshRateDenominator,
                modePair.TargetMode.ScanLineOrdering);
        }

        return (paths, modes);
    }

    private KnownModePair? SelectBestEffortRestoreMode(DisplayPathContext context, DisplayPathSnapshot snapshotPath)
    {
        var preferred = snapshotPath.SourceMode is null
            ? null
            : new DisplayMode(
                (int)snapshotPath.SourceMode.Width,
                (int)snapshotPath.SourceMode.Height,
                snapshotPath.RefreshRate.Hertz);

        var selected = SelectBestMode(context, preferred);
        if (selected.Succeeded && selected.SourceMode is not null && selected.TargetMode is not null)
        {
            return new KnownModePair(selected.SourceMode, selected.TargetMode, ModePairKind.Current);
        }

        var currentTargetMode = ToKnownTargetMode(context.TargetMode);
        if (context.SourceMode.HasValue && currentTargetMode is not null)
        {
            var currentSourceMode = ToDisplayMode(context.SourceMode.Value, context.Path.targetInfo.refreshRate);
            if (currentSourceMode is not null)
            {
                return new KnownModePair(currentSourceMode, currentTargetMode, ModePairKind.Current);
            }
        }

        return null;
    }

    private static (DISPLAYCONFIG_PATH_INFO[] Paths, DISPLAYCONFIG_MODE_INFO[] Modes) BuildSingleDisplayTopologyOnlyConfiguration(
        DisplayPathContext context)
    {
        var path = context.Path;
        path.flags = NativeMethods.DISPLAYCONFIG_PATH_ACTIVE;
        path.sourceInfo.modeInfoIdx = NativeMethods.DISPLAYCONFIG_PATH_MODE_IDX_INVALID;
        path.targetInfo.modeInfoIdx = NativeMethods.DISPLAYCONFIG_PATH_MODE_IDX_INVALID;
        path.targetInfo.targetAvailable = 1;

        return ([path], []);
    }

    private (DISPLAYCONFIG_PATH_INFO[] Paths, DISPLAYCONFIG_MODE_INFO[] Modes)? BuildSafeModeConfiguration(DisplayPathContext context)
    {
        var modeSelection = SelectBestMode(context, preferredMode: null);
        if (!modeSelection.Succeeded || modeSelection.SourceMode is null || modeSelection.TargetMode is null)
        {
            return null;
        }

        return BuildSingleDisplayConfiguration(context, modeSelection.SourceMode, modeSelection.TargetMode);
    }

    private static DISPLAYCONFIG_PATH_INFO CreateRestoredPathInfo(
        DISPLAYCONFIG_PATH_INFO template,
        uint sourceModeIndex,
        uint targetModeIndex,
        DisplayPathSnapshot snapshotPath,
        DISPLAYCONFIG_RATIONAL? refreshRate = null,
        DISPLAYCONFIG_SCANLINE_ORDERING? scanLineOrdering = null)
    {
        template.flags = NativeMethods.DISPLAYCONFIG_PATH_ACTIVE;
        template.sourceInfo.modeInfoIdx = sourceModeIndex;
        template.targetInfo.modeInfoIdx = targetModeIndex;
        template.targetInfo.outputTechnology = ParseOutputTechnology(snapshotPath.OutputTechnology);
        template.targetInfo.rotation = ParseRotation(snapshotPath.Rotation);
        template.targetInfo.scaling = ParseScaling(snapshotPath.Scaling);
        template.targetInfo.refreshRate = refreshRate ?? new DISPLAYCONFIG_RATIONAL(
            snapshotPath.RefreshRate.Numerator,
            snapshotPath.RefreshRate.Denominator);
        template.targetInfo.scanLineOrdering = scanLineOrdering ?? (
            snapshotPath.TargetMode is null
                ? DISPLAYCONFIG_SCANLINE_ORDERING.DISPLAYCONFIG_SCANLINE_ORDERING_UNSPECIFIED
                : ParseScanlineOrdering(snapshotPath.TargetMode.ScanLineOrdering));
        template.targetInfo.targetAvailable = 1;
        return template;
    }

    private static string BuildTopologyDescription(IReadOnlyList<RestoreMatch> matches) =>
        matches.Count switch
        {
            1 => "Restoring single-display topology",
            2 => "Restoring two-display extended topology",
            _ => $"Restoring {matches.Count}-display topology"
        };

    private static List<string> MergeDetails(List<string> existing, IReadOnlyList<string> additional)
    {
        foreach (var item in additional)
        {
            if (!existing.Contains(item, StringComparer.Ordinal))
            {
                existing.Add(item);
            }
        }

        return existing;
    }

    private void EnsureWindows()
    {
        if (!_skipPlatformCheck && !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            throw new PlatformNotSupportedException("CouchControl Windows display management is only supported on Windows.");
        }
    }

    private IReadOnlyList<DisplayPathContext> BuildDisplayContexts(DisplayConfigurationQueryResult configuration)
    {
        var contexts = new List<DisplayPathContext>(configuration.Paths.Length);

        foreach (var path in configuration.Paths)
        {
            var targetDetails = TryGetTargetDeviceName(path);
            var sourceMode = ResolveMode(configuration.Modes, path.sourceInfo.modeInfoIdx, DISPLAYCONFIG_MODE_INFO_TYPE.DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE);
            var targetMode = ResolveMode(configuration.Modes, path.targetInfo.modeInfoIdx, DISPLAYCONFIG_MODE_INFO_TYPE.DISPLAYCONFIG_MODE_INFO_TYPE_TARGET);
            var mapped = DisplayMapper.MapToDomain(path, targetDetails.FriendlyName, targetDetails.DevicePath, sourceMode);

            contexts.Add(new DisplayPathContext(
                mapped.Identifier,
                mapped.FriendlyName,
                mapped.DevicePath ?? mapped.Identifier.Value,
                path,
                sourceMode,
                targetMode,
                TryGetSourceDeviceName(path),
                EnumerateSupportedSourceModes(path)));
        }

        return CollapseDuplicateContexts(contexts);
    }

    private static IReadOnlyList<DisplayPathContext> CollapseDuplicateContexts(IEnumerable<DisplayPathContext> contexts) =>
        contexts
            .GroupBy(context => context.Identifier, context => context)
            .Select(static group => group
                .OrderByDescending(context => context.IsActive)
                .ThenByDescending(context => !IsUnknownDisplay(context))
                .ThenByDescending(context => context.SourceMode.HasValue)
                .ThenBy(context => context.Path.sourceInfo.id)
                .First())
            .Where(static context => context.IsActive || !IsUnknownDisplay(context))
            .ToArray();

    private static bool IsUnknownDisplay(DisplayPathContext context) =>
        context.FriendlyName == "Generic Monitor" &&
        context.DevicePath.Contains("DISPLAY#UNKNOWN", StringComparison.OrdinalIgnoreCase);

    private ModeSelectionResult SelectBestMode(DisplayPathContext context, DisplayMode? preferredMode)
    {
        var supportedModes = context.SupportedSourceModes
            .Where(mode => mode.IsValid)
            .Distinct(DisplayModeEqualityComparer.Instance)
            .ToArray();

        if (supportedModes.Length == 0)
        {
            return ModeSelectionResult.Failure(
                $"No supported source modes were found for '{context.FriendlyName}'.",
                "display_supported_modes_unavailable");
        }

        var currentTargetMode = ToKnownTargetMode(context.TargetMode);
        var preferredTargetMode = TryGetPreferredTargetMode(context.Path);
        var knownPairs = BuildKnownModePairs(context, supportedModes, currentTargetMode, preferredTargetMode);

        if (preferredMode is not null)
        {
            var exact = knownPairs.FirstOrDefault(pair => pair.SourceMode.Equals(preferredMode));
            if (exact is not null)
            {
                return ModeSelectionResult.Success(exact.SourceMode, exact.TargetMode);
            }

            var sameResolution = knownPairs
                .Where(pair => pair.SourceMode.Width == preferredMode.Width && pair.SourceMode.Height == preferredMode.Height)
                .OrderBy(pair => Math.Abs(pair.SourceMode.RefreshRateHz - preferredMode.RefreshRateHz))
                .FirstOrDefault();

            if (sameResolution is not null)
            {
                return ModeSelectionResult.Success(sameResolution.SourceMode, sameResolution.TargetMode);
            }
        }

        var currentPair = knownPairs.FirstOrDefault(pair => pair.Kind == ModePairKind.Current);
        if (currentPair is not null)
        {
            return ModeSelectionResult.Success(currentPair.SourceMode, currentPair.TargetMode);
        }

        var nativePair = knownPairs.FirstOrDefault(pair => pair.Kind == ModePairKind.Preferred);
        if (nativePair is not null)
        {
            return ModeSelectionResult.Success(nativePair.SourceMode, nativePair.TargetMode);
        }

        return ModeSelectionResult.Failure(
            $"No safe display mode exists for '{context.FriendlyName}'. Requested mode: {preferredMode?.ToString() ?? "default"}",
            "display_safe_mode_unavailable");
    }

    private SourceModeSelectionResult SelectBestSourceModeForExplicitActivation(DisplayPathContext context, DisplayMode? preferredMode)
    {
        var supportedModes = context.SupportedSourceModes
            .Where(mode => mode.IsValid)
            .Distinct(DisplayModeEqualityComparer.Instance)
            .ToArray();

        if (supportedModes.Length == 0)
        {
            return SourceModeSelectionResult.Failure(
                $"No supported source modes were found for '{context.FriendlyName}'.",
                "display_supported_modes_unavailable");
        }

        if (preferredMode is not null)
        {
            var exact = supportedModes.FirstOrDefault(mode => mode.Equals(preferredMode));
            if (exact is not null)
            {
                return SourceModeSelectionResult.Success(exact);
            }

            var sameResolution = supportedModes
                .Where(mode => mode.Width == preferredMode.Width && mode.Height == preferredMode.Height)
                .OrderBy(mode => Math.Abs(mode.RefreshRateHz - preferredMode.RefreshRateHz))
                .FirstOrDefault();

            if (sameResolution is not null)
            {
                return SourceModeSelectionResult.Success(sameResolution);
            }
        }

        if (context.SourceMode.HasValue)
        {
            var currentSourceMode = ToDisplayMode(context.SourceMode.Value, context.Path.targetInfo.refreshRate);
            if (currentSourceMode is not null)
            {
                return SourceModeSelectionResult.Success(currentSourceMode);
            }
        }

        return SourceModeSelectionResult.Success(supportedModes[0]);
    }

    private static List<KnownModePair> BuildKnownModePairs(
        DisplayPathContext context,
        IReadOnlyList<DisplayMode> supportedModes,
        KnownTargetMode? currentTargetMode,
        KnownTargetMode? preferredTargetMode)
    {
        var pairs = new List<KnownModePair>();

        if (context.SourceMode.HasValue && currentTargetMode is not null)
        {
            var currentSourceMode = ToDisplayMode(context.SourceMode.Value, context.Path.targetInfo.refreshRate);
            if (currentSourceMode is not null)
            {
                pairs.Add(new KnownModePair(currentSourceMode, currentTargetMode, ModePairKind.Current));
            }
        }

        if (preferredTargetMode is not null)
        {
            var matchingPreferredSource = supportedModes.FirstOrDefault(mode =>
                mode.Width == (int)preferredTargetMode.ActiveWidth &&
                mode.Height == (int)preferredTargetMode.ActiveHeight &&
                ApproximatelyEqual(mode.RefreshRateHz, preferredTargetMode.RefreshRateHz));

            if (matchingPreferredSource is not null)
            {
                pairs.Add(new KnownModePair(matchingPreferredSource, preferredTargetMode, ModePairKind.Preferred));
            }
        }

        if (currentTargetMode is not null)
        {
            foreach (var mode in supportedModes.Where(mode =>
                         mode.Width == (int)currentTargetMode.ActiveWidth &&
                         mode.Height == (int)currentTargetMode.ActiveHeight &&
                         ApproximatelyEqual(mode.RefreshRateHz, currentTargetMode.RefreshRateHz)))
            {
                pairs.Add(new KnownModePair(mode, currentTargetMode, ModePairKind.AlternateRefresh));
            }
        }

        if (preferredTargetMode is not null)
        {
            foreach (var mode in supportedModes.Where(mode =>
                         mode.Width == (int)preferredTargetMode.ActiveWidth &&
                         mode.Height == (int)preferredTargetMode.ActiveHeight &&
                         ApproximatelyEqual(mode.RefreshRateHz, preferredTargetMode.RefreshRateHz)))
            {
                pairs.Add(new KnownModePair(mode, preferredTargetMode, ModePairKind.AlternateRefresh));
            }
        }

        return pairs
            .Distinct(KnownModePairEqualityComparer.Instance)
            .ToList();
    }

    private (DISPLAYCONFIG_PATH_INFO[] Paths, DISPLAYCONFIG_MODE_INFO[] Modes) BuildSingleDisplayConfiguration(
        DisplayPathContext context,
        DisplayMode sourceMode,
        KnownTargetMode targetMode)
    {
        var path = context.Path;
        path.flags = NativeMethods.DISPLAYCONFIG_PATH_ACTIVE;
        path.sourceInfo.modeInfoIdx = 0;
        path.targetInfo.modeInfoIdx = 1;
        path.targetInfo.refreshRate = new DISPLAYCONFIG_RATIONAL(targetMode.RefreshRateNumerator, targetMode.RefreshRateDenominator);
        path.targetInfo.scanLineOrdering = targetMode.ScanLineOrdering;
        path.targetInfo.targetAvailable = 1;

        var modes = new[]
        {
            CreateSourceModeInfo(
                path.sourceInfo.adapterId,
                path.sourceInfo.id,
                (uint)sourceMode.Width,
                (uint)sourceMode.Height,
                0,
                0,
                context.SourceMode?.sourceMode.pixelFormat ?? DISPLAYCONFIG_PIXELFORMAT.DISPLAYCONFIG_PIXELFORMAT_32BPP),
            CreateTargetModeInfo(
                path.targetInfo.adapterId,
                path.targetInfo.id,
                targetMode.ActiveWidth,
                targetMode.ActiveHeight,
                targetMode.RefreshRateNumerator,
                targetMode.RefreshRateDenominator,
                targetMode.ScanLineOrdering)
        };

        return ([path], modes);
    }

    private int ApplyDisplayConfiguration((DISPLAYCONFIG_PATH_INFO[] Paths, DISPLAYCONFIG_MODE_INFO[] Modes) configuration, bool validateOnly)
    {
        var flags = NativeMethods.SDC_USE_SUPPLIED_DISPLAY_CONFIG |
            NativeMethods.SDC_TOPOLOGY_SUPPLIED |
            NativeMethods.SDC_ALLOW_CHANGES |
            NativeMethods.SDC_PATH_PERSIST_IF_REQUIRED |
            NativeMethods.SDC_VIRTUAL_MODE_AWARE |
            NativeMethods.SDC_VIRTUAL_REFRESH_RATE_AWARE |
            NativeMethods.SDC_ALLOW_PATH_ORDER_CHANGES;

        flags |= validateOnly
            ? NativeMethods.SDC_VALIDATE
            : NativeMethods.SDC_APPLY | NativeMethods.SDC_SAVE_TO_DATABASE | NativeMethods.SDC_FORCE_MODE_ENUMERATION;

        return _displaySystem.SetDisplayConfig(
            (uint)configuration.Paths.Length,
            configuration.Paths,
            (uint)configuration.Modes.Length,
            configuration.Modes,
            flags);
    }

    private OperationResult VerifySingleActiveDisplay(DisplayIdentifier expectedDisplay)
    {
        var configuration = QueryDisplayConfiguration(NativeMethods.QDC_ALL_PATHS);
        var contexts = BuildDisplayContexts(configuration);
        var activeDisplays = contexts.Where(context => context.IsActive).ToArray();
        var target = activeDisplays.FirstOrDefault(context => context.Identifier.Matches(expectedDisplay));

        if (target is null)
        {
            return OperationResult.Failure(
                $"Verification failed: '{expectedDisplay}' is not active after switching.",
                "display_switch_verification_failed");
        }

        if (activeDisplays.Length != 1)
        {
            return OperationResult.Failure(
                $"Verification failed: expected only '{target.FriendlyName}' to be active, but found {activeDisplays.Length} active displays.",
                "display_switch_verification_failed");
        }

        return OperationResult.Success($"Verified '{target.FriendlyName}' as the only active display.");
    }

    private DisplayConfigurationQueryResult QueryDisplayConfiguration(uint flags)
    {
        uint numPaths = 0;
        uint numModes = 0;
        int result;
        int retryCount = 0;
        const int maxRetries = 3;

        _logger?.LogDebug("Querying display configuration buffer sizes with flags {Flags}...", flags);

        do
        {
            result = _displaySystem.GetDisplayConfigBufferSizes(flags, out numPaths, out numModes);
            if (result != 0)
            {
                _logger?.LogError("GetDisplayConfigBufferSizes failed with error code {Error}", result);
                throw new Win32Exception(result, $"GetDisplayConfigBufferSizes failed with error {result}");
            }

            var pathArray = new DISPLAYCONFIG_PATH_INFO[numPaths];
            var modeInfoArray = new DISPLAYCONFIG_MODE_INFO[numModes];

            result = _displaySystem.QueryDisplayConfig(
                flags,
                ref numPaths,
                pathArray,
                ref numModes,
                modeInfoArray,
                IntPtr.Zero);

            if (result == 0)
            {
                if (numPaths != pathArray.Length)
                {
                    Array.Resize(ref pathArray, (int)numPaths);
                }

                if (numModes != modeInfoArray.Length)
                {
                    Array.Resize(ref modeInfoArray, (int)numModes);
                }

                _logger?.LogInformation(
                    "Successfully queried display configuration. Flags: {Flags}, Paths: {NumPaths}, Modes: {NumModes}",
                    flags,
                    numPaths,
                    numModes);

                return new DisplayConfigurationQueryResult(pathArray, modeInfoArray);
            }

            if (result == 122)
            {
                retryCount++;
                _logger?.LogWarning(
                    "QueryDisplayConfig returned ERROR_INSUFFICIENT_BUFFER. Retrying ({RetryCount}/{MaxRetries})...",
                    retryCount,
                    maxRetries);
                continue;
            }

            _logger?.LogError("QueryDisplayConfig failed with error code {Error}", result);
            throw new Win32Exception(result, $"QueryDisplayConfig failed with error {result}");
        }
        while (retryCount < maxRetries);

        throw new Win32Exception(122, "QueryDisplayConfig failed repeatedly with ERROR_INSUFFICIENT_BUFFER.");
    }

    private (string? FriendlyName, string? DevicePath) TryGetTargetDeviceName(DISPLAYCONFIG_PATH_INFO path)
    {
        var deviceName = new DISPLAYCONFIG_TARGET_DEVICE_NAME
        {
            header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
            {
                size = (uint)Marshal.SizeOf<DISPLAYCONFIG_TARGET_DEVICE_NAME>(),
                adapterId = path.targetInfo.adapterId,
                id = path.targetInfo.id,
                type = DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME
            }
        };

        int infoResult = _displaySystem.DisplayConfigGetDeviceInfo(ref deviceName);
        if (infoResult == 0)
        {
            return (deviceName.monitorFriendlyDeviceName, deviceName.monitorDevicePath);
        }

        _logger?.LogWarning(
            "DisplayConfigGetDeviceInfo failed with error code {Error} for adapter LUID {AdapterId}, target ID {TargetId}",
            infoResult,
            path.targetInfo.adapterId,
            path.targetInfo.id);

        return (null, null);
    }

    private string? TryGetSourceDeviceName(DISPLAYCONFIG_PATH_INFO path)
    {
        var sourceName = new DISPLAYCONFIG_SOURCE_DEVICE_NAME
        {
            header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
            {
                size = (uint)Marshal.SizeOf<DISPLAYCONFIG_SOURCE_DEVICE_NAME>(),
                adapterId = path.sourceInfo.adapterId,
                id = path.sourceInfo.id,
                type = DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME
            }
        };

        int infoResult = _displaySystem.DisplayConfigGetDeviceInfo(ref sourceName);
        if (infoResult == 0)
        {
            return sourceName.viewGdiDeviceName;
        }

        _logger?.LogWarning(
            "Could not resolve source device name for adapter LUID {AdapterId}, source ID {SourceId}. Error: {Error}",
            path.sourceInfo.adapterId,
            path.sourceInfo.id,
            infoResult);

        return null;
    }

    private KnownTargetMode? TryGetPreferredTargetMode(DISPLAYCONFIG_PATH_INFO path)
    {
        var preferredMode = new DISPLAYCONFIG_TARGET_PREFERRED_MODE
        {
            header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
            {
                size = (uint)Marshal.SizeOf<DISPLAYCONFIG_TARGET_PREFERRED_MODE>(),
                adapterId = path.targetInfo.adapterId,
                id = path.targetInfo.id,
                type = DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_PREFERRED_MODE
            }
        };

        int infoResult = _displaySystem.DisplayConfigGetDeviceInfo(ref preferredMode);
        if (infoResult != 0)
        {
            _logger?.LogWarning(
                "Could not resolve preferred target mode for adapter LUID {AdapterId}, target ID {TargetId}. Error: {Error}",
                path.targetInfo.adapterId,
                path.targetInfo.id,
                infoResult);
            return null;
        }

        return new KnownTargetMode(
            preferredMode.width,
            preferredMode.height,
            preferredMode.targetMode.targetVideoSignalInfo.vSyncFreq.Numerator,
            preferredMode.targetMode.targetVideoSignalInfo.vSyncFreq.Denominator,
            preferredMode.targetMode.targetVideoSignalInfo.scanLineOrdering);
    }

    private IReadOnlyList<DisplayMode> EnumerateSupportedSourceModes(DISPLAYCONFIG_PATH_INFO path)
    {
        var sourceDeviceName = TryGetSourceDeviceName(path);
        if (string.IsNullOrWhiteSpace(sourceDeviceName))
        {
            return Array.Empty<DisplayMode>();
        }

        var modes = new List<DisplayMode>();

        for (uint index = 0; ; index++)
        {
            var devMode = new DEVMODE
            {
                dmSize = (ushort)Marshal.SizeOf<DEVMODE>()
            };

            if (!_displaySystem.EnumDisplaySettingsEx(sourceDeviceName, index, ref devMode, 0))
            {
                break;
            }

            if (devMode.dmPelsWidth == 0 || devMode.dmPelsHeight == 0 || devMode.dmDisplayFrequency == 0)
            {
                continue;
            }

            modes.Add(new DisplayMode(
                (int)devMode.dmPelsWidth,
                (int)devMode.dmPelsHeight,
                devMode.dmDisplayFrequency));
        }

        if (modes.Count == 0)
        {
            var currentMode = new DEVMODE
            {
                dmSize = (ushort)Marshal.SizeOf<DEVMODE>()
            };

            if (_displaySystem.EnumDisplaySettingsEx(sourceDeviceName, NativeMethods.ENUM_CURRENT_SETTINGS, ref currentMode, 0))
            {
                modes.Add(new DisplayMode(
                    (int)currentMode.dmPelsWidth,
                    (int)currentMode.dmPelsHeight,
                    currentMode.dmDisplayFrequency));
            }
        }

        return modes;
    }

    private static DisplayMode? ToDisplayMode(DISPLAYCONFIG_MODE_INFO sourceMode, DISPLAYCONFIG_RATIONAL refreshRate)
    {
        if (sourceMode.infoType != DISPLAYCONFIG_MODE_INFO_TYPE.DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE)
        {
            return null;
        }

        return new DisplayMode(
            (int)sourceMode.sourceMode.width,
            (int)sourceMode.sourceMode.height,
            DisplayMapper.CalculateRefreshRate(refreshRate));
    }

    private static KnownTargetMode? ToKnownTargetMode(DISPLAYCONFIG_MODE_INFO? targetMode)
    {
        if (!targetMode.HasValue || targetMode.Value.infoType != DISPLAYCONFIG_MODE_INFO_TYPE.DISPLAYCONFIG_MODE_INFO_TYPE_TARGET)
        {
            return null;
        }

        var signalInfo = targetMode.Value.targetMode.targetVideoSignalInfo;
        return new KnownTargetMode(
            signalInfo.activeSize.cx,
            signalInfo.activeSize.cy,
            signalInfo.vSyncFreq.Numerator,
            signalInfo.vSyncFreq.Denominator,
            signalInfo.scanLineOrdering);
    }

    private static DISPLAYCONFIG_MODE_INFO CreateSourceModeInfo(
        LUID adapterId,
        uint sourceId,
        uint width,
        uint height,
        int positionX,
        int positionY,
        DISPLAYCONFIG_PIXELFORMAT pixelFormat)
    {
        return new DISPLAYCONFIG_MODE_INFO
        {
            infoType = DISPLAYCONFIG_MODE_INFO_TYPE.DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE,
            adapterId = adapterId,
            id = sourceId,
            sourceMode = new DISPLAYCONFIG_SOURCE_MODE
            {
                width = width,
                height = height,
                pixelFormat = pixelFormat,
                position = new POINTL { x = positionX, y = positionY }
            }
        };
    }

    private static DISPLAYCONFIG_MODE_INFO CreateTargetModeInfo(
        LUID adapterId,
        uint targetId,
        uint activeWidth,
        uint activeHeight,
        uint refreshRateNumerator,
        uint refreshRateDenominator,
        DISPLAYCONFIG_SCANLINE_ORDERING scanLineOrdering)
    {
        return new DISPLAYCONFIG_MODE_INFO
        {
            infoType = DISPLAYCONFIG_MODE_INFO_TYPE.DISPLAYCONFIG_MODE_INFO_TYPE_TARGET,
            adapterId = adapterId,
            id = targetId,
            targetMode = new DISPLAYCONFIG_TARGET_MODE
            {
                targetVideoSignalInfo = new DISPLAYCONFIG_VIDEO_SIGNAL_INFO
                {
                    activeSize = new DISPLAYCONFIG_2DREGION
                    {
                        cx = activeWidth,
                        cy = activeHeight
                    },
                    totalSize = new DISPLAYCONFIG_2DREGION
                    {
                        cx = activeWidth,
                        cy = activeHeight
                    },
                    vSyncFreq = new DISPLAYCONFIG_RATIONAL(refreshRateNumerator, refreshRateDenominator),
                    hSyncFreq = new DISPLAYCONFIG_RATIONAL(0, 1),
                    scanLineOrdering = scanLineOrdering
                }
            }
        };
    }

    private static LUID ParseLuid(string value)
    {
        var parts = value.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
        {
            throw new FormatException($"Invalid adapter LUID '{value}'.");
        }

        return new LUID
        {
            HighPart = Convert.ToInt32(parts[0], 16),
            LowPart = Convert.ToUInt32(parts[1], 16)
        };
    }

    private static DISPLAYCONFIG_PIXELFORMAT ParsePixelFormat(string value) =>
        value switch
        {
            "8Bpp" => DISPLAYCONFIG_PIXELFORMAT.DISPLAYCONFIG_PIXELFORMAT_8BPP,
            "16Bpp" => DISPLAYCONFIG_PIXELFORMAT.DISPLAYCONFIG_PIXELFORMAT_16BPP,
            "24Bpp" => DISPLAYCONFIG_PIXELFORMAT.DISPLAYCONFIG_PIXELFORMAT_24BPP,
            "32Bpp" => DISPLAYCONFIG_PIXELFORMAT.DISPLAYCONFIG_PIXELFORMAT_32BPP,
            "NonGdi" => DISPLAYCONFIG_PIXELFORMAT.DISPLAYCONFIG_PIXELFORMAT_NONGDI,
            _ => DISPLAYCONFIG_PIXELFORMAT.DISPLAYCONFIG_PIXELFORMAT_32BPP
        };

    private static DISPLAYCONFIG_ROTATION ParseRotation(string value) =>
        value switch
        {
            "Identity" => DISPLAYCONFIG_ROTATION.DISPLAYCONFIG_ROTATION_IDENTITY,
            "Rotate90" => DISPLAYCONFIG_ROTATION.DISPLAYCONFIG_ROTATION_90,
            "Rotate180" => DISPLAYCONFIG_ROTATION.DISPLAYCONFIG_ROTATION_180,
            "Rotate270" => DISPLAYCONFIG_ROTATION.DISPLAYCONFIG_ROTATION_270,
            _ => DISPLAYCONFIG_ROTATION.DISPLAYCONFIG_ROTATION_IDENTITY
        };

    private static DISPLAYCONFIG_SCALING ParseScaling(string value) =>
        value switch
        {
            "Identity" => DISPLAYCONFIG_SCALING.DISPLAYCONFIG_SCALING_IDENTITY,
            "Centered" => DISPLAYCONFIG_SCALING.DISPLAYCONFIG_SCALING_CENTERED,
            "Stretched" => DISPLAYCONFIG_SCALING.DISPLAYCONFIG_SCALING_STRETCHED,
            "AspectRatioCenteredMax" => DISPLAYCONFIG_SCALING.DISPLAYCONFIG_SCALING_ASPECTRATIORECTANCED,
            "Custom" => DISPLAYCONFIG_SCALING.DISPLAYCONFIG_SCALING_CUSTOM,
            "Preferred" => DISPLAYCONFIG_SCALING.DISPLAYCONFIG_SCALING_PREFERRED,
            _ => DISPLAYCONFIG_SCALING.DISPLAYCONFIG_SCALING_IDENTITY
        };

    private static DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY ParseOutputTechnology(string value) =>
        value switch
        {
            "VGA" => DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_HD15,
            "S-Video" => DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_SVIDEO,
            "Composite" => DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_COMPOSITE_VIDEO,
            "Component" => DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_COMPONENT_VIDEO,
            "DVI" => DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DVI,
            "HDMI" => DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_HDMI,
            "LVDS" => DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_LVDS,
            "D-JPN" => DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_D_JPN,
            "SDI" => DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_SDI,
            "DisplayPort" => DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EXTERNAL,
            "Embedded DisplayPort" => DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EMBEDDED,
            "UDI" => DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_UDI_EXTERNAL,
            "Embedded UDI" => DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_UDI_EMBEDDED,
            "SDTV Dongle" => DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_SDTVDONGLE,
            "Miracast" => DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_MIRACAST,
            "Indirect Wired" => DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INDIRECT_WIRED,
            "Indirect Virtual" => DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INDIRECT_VIRTUAL,
            "DisplayPort USB Tunnel" => DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_USB_TUNNEL,
            "Internal" => DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INTERNAL,
            _ => DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_OTHER
        };

    private static DISPLAYCONFIG_SCANLINE_ORDERING ParseScanlineOrdering(string value) =>
        value switch
        {
            "Progressive" => DISPLAYCONFIG_SCANLINE_ORDERING.DISPLAYCONFIG_SCANLINE_ORDERING_PROGRESSIVE,
            "Interlaced" => DISPLAYCONFIG_SCANLINE_ORDERING.DISPLAYCONFIG_SCANLINE_ORDERING_INTERLACED,
            "InterlacedLowerFieldFirst" => DISPLAYCONFIG_SCANLINE_ORDERING.DISPLAYCONFIG_SCANLINE_ORDERING_INTERLACED_LOWERFIELDFIRST,
            _ => DISPLAYCONFIG_SCANLINE_ORDERING.DISPLAYCONFIG_SCANLINE_ORDERING_UNSPECIFIED
        };

    private static bool ApproximatelyEqual(decimal left, decimal right) =>
        Math.Abs(left - right) <= 0.05m;

    private static DISPLAYCONFIG_MODE_INFO? ResolveMode(
        IReadOnlyList<DISPLAYCONFIG_MODE_INFO> modes,
        uint modeIndex,
        DISPLAYCONFIG_MODE_INFO_TYPE expectedType)
    {
        if (modeIndex == uint.MaxValue || modeIndex >= modes.Count)
        {
            return null;
        }

        var mode = modes[(int)modeIndex];
        return mode.infoType == expectedType
            ? mode
            : null;
    }

    private sealed record DisplayConfigurationQueryResult(
        DISPLAYCONFIG_PATH_INFO[] Paths,
        DISPLAYCONFIG_MODE_INFO[] Modes);

    private sealed record DisplayPathContext(
        DisplayIdentifier Identifier,
        string FriendlyName,
        string DevicePath,
        DISPLAYCONFIG_PATH_INFO Path,
        DISPLAYCONFIG_MODE_INFO? SourceMode,
        DISPLAYCONFIG_MODE_INFO? TargetMode,
        string? SourceDeviceName,
        IReadOnlyList<DisplayMode> SupportedSourceModes)
    {
        public bool IsActive => (Path.flags & NativeMethods.DISPLAYCONFIG_PATH_ACTIVE) != 0;
    }

    private sealed record KnownTargetMode(
        uint ActiveWidth,
        uint ActiveHeight,
        uint RefreshRateNumerator,
        uint RefreshRateDenominator,
        DISPLAYCONFIG_SCANLINE_ORDERING ScanLineOrdering)
    {
        public decimal RefreshRateHz => DisplayMapper.CalculateRefreshRate(
            new DISPLAYCONFIG_RATIONAL(RefreshRateNumerator, RefreshRateDenominator));
    }

    private enum ModePairKind
    {
        Current,
        Preferred,
        AlternateRefresh
    }

    private sealed record KnownModePair(
        DisplayMode SourceMode,
        KnownTargetMode TargetMode,
        ModePairKind Kind);

    private sealed class DisplayModeEqualityComparer : IEqualityComparer<DisplayMode>
    {
        public static DisplayModeEqualityComparer Instance { get; } = new();

        public bool Equals(DisplayMode? x, DisplayMode? y) =>
            x is not null &&
            y is not null &&
            x.Width == y.Width &&
            x.Height == y.Height &&
            ApproximatelyEqual(x.RefreshRateHz, y.RefreshRateHz);

        public int GetHashCode(DisplayMode obj) =>
            HashCode.Combine(obj.Width, obj.Height, decimal.Round(obj.RefreshRateHz, 2));
    }

    private sealed class KnownModePairEqualityComparer : IEqualityComparer<KnownModePair>
    {
        public static KnownModePairEqualityComparer Instance { get; } = new();

        public bool Equals(KnownModePair? x, KnownModePair? y) =>
            x is not null &&
            y is not null &&
            DisplayModeEqualityComparer.Instance.Equals(x.SourceMode, y.SourceMode) &&
            x.TargetMode.ActiveWidth == y.TargetMode.ActiveWidth &&
            x.TargetMode.ActiveHeight == y.TargetMode.ActiveHeight &&
            ApproximatelyEqual(x.TargetMode.RefreshRateHz, y.TargetMode.RefreshRateHz);

        public int GetHashCode(KnownModePair obj) =>
            HashCode.Combine(
                DisplayModeEqualityComparer.Instance.GetHashCode(obj.SourceMode),
                obj.TargetMode.ActiveWidth,
                obj.TargetMode.ActiveHeight,
                decimal.Round(obj.TargetMode.RefreshRateHz, 2));
    }

    private enum BaselineRestoreStrategy
    {
        SingleDisplayDeviceSettings,
        ExactNative,
        BestEffortNative
    }

    private sealed record RestorePlan(
        BaselineRestoreStrategy Strategy,
        (DISPLAYCONFIG_PATH_INFO[] Paths, DISPLAYCONFIG_MODE_INFO[] Modes)? ExactConfiguration,
        (DISPLAYCONFIG_PATH_INFO[] Paths, DISPLAYCONFIG_MODE_INFO[] Modes)? BestEffortConfiguration,
        IReadOnlyList<RestoreMatch> Matches,
        IReadOnlyList<DisplayPathSnapshot> MissingPaths,
        IReadOnlyList<string> Details);

    private sealed record RestoreMatch(
        DisplayPathSnapshot SnapshotPath,
        DisplayPathContext Context);

    private enum RestoreAttemptKind
    {
        SingleDisplayTopologyOnly,
        SingleDisplayTopologyOnlyAfterFallback,
        Exact,
        BestEffort
    }

    private sealed record RestoreVerificationResult(
        bool Succeeded,
        string? Message,
        string? ErrorCode,
        IReadOnlyList<string> Details)
    {
        public static RestoreVerificationResult Success(IReadOnlyList<string> details) =>
            new(true, null, null, details);

        public static RestoreVerificationResult Failure(string message, string errorCode, IReadOnlyList<string> details) =>
            new(false, message, errorCode, details);
    }

    private sealed record ModeSelectionResult(
        bool Succeeded,
        string Message,
        string? ErrorCode,
        DisplayMode? SourceMode,
        KnownTargetMode? TargetMode)
    {
        public static ModeSelectionResult Success(DisplayMode sourceMode, KnownTargetMode targetMode) =>
            new(true, "Display mode selected.", null, sourceMode, targetMode);

        public static ModeSelectionResult Failure(string message, string errorCode) =>
            new(false, message, errorCode, null, null);
    }

    private sealed record SourceModeSelectionResult(
        bool Succeeded,
        string Message,
        string? ErrorCode,
        DisplayMode? SourceMode)
    {
        public static SourceModeSelectionResult Success(DisplayMode sourceMode) =>
            new(true, "Display source mode selected.", null, sourceMode);

        public static SourceModeSelectionResult Failure(string message, string errorCode) =>
            new(false, message, errorCode, null);
    }

    private sealed record DeviceSettingsRestoreResult(
        bool Succeeded,
        string Message,
        string? ErrorCode,
        string Outcome,
        IReadOnlyList<string> Details)
    {
        public static DeviceSettingsRestoreResult Success(string message, string outcome, IReadOnlyList<string> details) =>
            new(true, message, null, outcome, details);

        public static DeviceSettingsRestoreResult Failure(string message, string errorCode, IReadOnlyList<string> details) =>
            new(false, message, errorCode, "native_failed", details);
    }

    internal interface IWindowsDisplaySystem
    {
        int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

        int QueryDisplayConfig(
            uint flags,
            ref uint numPathArrayElements,
            DISPLAYCONFIG_PATH_INFO[] pathArray,
            ref uint numModeInfoArrayElements,
            DISPLAYCONFIG_MODE_INFO[] modeInfoArray,
            IntPtr currentTopologyId);

        int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_TARGET_DEVICE_NAME requestPacket);

        int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_SOURCE_DEVICE_NAME requestPacket);

        int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_TARGET_PREFERRED_MODE requestPacket);

        int SetDisplayConfig(
            uint numPathArrayElements,
            DISPLAYCONFIG_PATH_INFO[] pathArray,
            uint numModeInfoArrayElements,
            DISPLAYCONFIG_MODE_INFO[] modeInfoArray,
            uint flags);

        bool EnumDisplaySettingsEx(string lpszDeviceName, uint iModeNum, ref DEVMODE lpDevMode, uint dwFlags);

        int ChangeDisplaySettingsEx(string lpszDeviceName, DEVMODE lpDevMode, uint dwFlags);

        int CommitDisplaySettings();

        Task<int> RunDisplaySwitchExtendAsync(CancellationToken cancellationToken);
    }

    private sealed class NativeWindowsDisplaySystem : IWindowsDisplaySystem
    {
        public int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements) =>
            NativeMethods.GetDisplayConfigBufferSizes(flags, out numPathArrayElements, out numModeInfoArrayElements);

        public int QueryDisplayConfig(
            uint flags,
            ref uint numPathArrayElements,
            DISPLAYCONFIG_PATH_INFO[] pathArray,
            ref uint numModeInfoArrayElements,
            DISPLAYCONFIG_MODE_INFO[] modeInfoArray,
            IntPtr currentTopologyId) =>
            NativeMethods.QueryDisplayConfig(flags, ref numPathArrayElements, pathArray, ref numModeInfoArrayElements, modeInfoArray, currentTopologyId);

        public int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_TARGET_DEVICE_NAME requestPacket) =>
            NativeMethods.DisplayConfigGetDeviceInfo(ref requestPacket);

        public int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_SOURCE_DEVICE_NAME requestPacket) =>
            NativeMethods.DisplayConfigGetDeviceInfo(ref requestPacket);

        public int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_TARGET_PREFERRED_MODE requestPacket) =>
            NativeMethods.DisplayConfigGetDeviceInfo(ref requestPacket);

        public int SetDisplayConfig(
            uint numPathArrayElements,
            DISPLAYCONFIG_PATH_INFO[] pathArray,
            uint numModeInfoArrayElements,
            DISPLAYCONFIG_MODE_INFO[] modeInfoArray,
            uint flags) =>
            NativeMethods.SetDisplayConfig(numPathArrayElements, pathArray, numModeInfoArrayElements, modeInfoArray, flags);

        public bool EnumDisplaySettingsEx(string lpszDeviceName, uint iModeNum, ref DEVMODE lpDevMode, uint dwFlags) =>
            NativeMethods.EnumDisplaySettingsEx(lpszDeviceName, iModeNum, ref lpDevMode, dwFlags);

        public int ChangeDisplaySettingsEx(string lpszDeviceName, DEVMODE lpDevMode, uint dwFlags) =>
            NativeMethods.ChangeDisplaySettingsEx(lpszDeviceName, ref lpDevMode, IntPtr.Zero, dwFlags, IntPtr.Zero);

        public int CommitDisplaySettings() =>
            NativeMethods.ChangeDisplaySettingsEx(null, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero);

        public async Task<int> RunDisplaySwitchExtendAsync(CancellationToken cancellationToken)
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "DisplaySwitch.exe",
                    Arguments = "/extend",
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            await process.WaitForExitAsync(cancellationToken);
            return process.ExitCode;
        }
    }
}
