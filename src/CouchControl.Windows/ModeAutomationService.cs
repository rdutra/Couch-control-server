using System.Diagnostics;
using CouchControl.Core.Abstractions;
using CouchControl.Core.Models;
using Microsoft.Extensions.Logging;

namespace CouchControl.Windows;

public sealed class ModeAutomationService(
    IAudioDeviceService audioDeviceService,
    ILogger<ModeAutomationService>? logger = null) : IModeAutomationService
{
    private static readonly TimeSpan AudioCommandTimeout = TimeSpan.FromSeconds(15);

    public async Task<OperationResult> RunPostActivationAsync(
        AgentMode mode,
        AgentConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        var configuredDeviceId = mode == AgentMode.Couch
            ? configuration.CouchAudioDeviceId
            : configuration.DesktopAudioDeviceId;
        if (!string.IsNullOrWhiteSpace(configuredDeviceId))
        {
            return await audioDeviceService.SetDefaultPlaybackDeviceAsync(configuredDeviceId, cancellationToken);
        }

        var command = mode == AgentMode.Couch
            ? configuration.CouchAudioCommand
            : configuration.DesktopAudioCommand;

        if (string.IsNullOrWhiteSpace(command))
        {
            return OperationResult.Success("No audio switch command configured.");
        }

        logger?.LogInformation("Running post-activation audio command for {Mode} mode.", mode);

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
            ?? throw new InvalidOperationException("Failed to start the configured audio switch command.");

        var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        var details = new List<string>();

        try
        {
            using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCancellation.CancelAfter(AudioCommandTimeout);
            await process.WaitForExitAsync(timeoutCancellation.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Failed to terminate timed-out audio command for {Mode} mode.", mode);
            }

            return OperationResult.Failure(
                $"Audio switch command timed out for {mode} mode after {(int)AudioCommandTimeout.TotalSeconds} seconds.",
                "audio_switch_command_timeout",
                outcome: "Failure");
        }

        var standardOutput = (await standardOutputTask).Trim();
        var standardError = (await standardErrorTask).Trim();

        if (!string.IsNullOrWhiteSpace(standardOutput))
        {
            details.Add(standardOutput);
        }

        if (process.ExitCode != 0)
        {
            if (!string.IsNullOrWhiteSpace(standardError))
            {
                details.Add(standardError);
            }

            return OperationResult.Failure(
                $"Audio switch command failed for {mode} mode with exit code {process.ExitCode}.",
                "audio_switch_command_failed",
                outcome: "Failure",
                details: details);
        }

        return OperationResult.Success(
            $"Audio output switched for {mode} mode.",
            outcome: "Success",
            details: details);
    }
}
