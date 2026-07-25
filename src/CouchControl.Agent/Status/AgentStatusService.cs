using CouchControl.Core.Abstractions;
using CouchControl.Core.Models;
using CouchControl.Windows.AgentApi;

namespace CouchControl.Agent.Status;

public interface IAgentStatusService
{
    Task<AgentStatusSnapshot> GetStatusAsync(CancellationToken cancellationToken = default);
}

public sealed class AgentStatusService : IAgentStatusService
{
    private readonly IAgentConfigurationStore configurationStore;
    private readonly IDisplayManager displayManager;
    private readonly IProfileOrchestrator profileOrchestrator;
    private readonly ISteamLauncher steamLauncher;
    private readonly IAgentNetworkDiagnosticsService diagnosticsService;

    public AgentStatusService(
        IAgentConfigurationStore configurationStore,
        IDisplayManager displayManager,
        IProfileOrchestrator profileOrchestrator,
        ISteamLauncher steamLauncher,
        IAgentNetworkDiagnosticsService diagnosticsService)
    {
        this.configurationStore = configurationStore;
        this.displayManager = displayManager;
        this.profileOrchestrator = profileOrchestrator;
        this.steamLauncher = steamLauncher;
        this.diagnosticsService = diagnosticsService;
    }

    public async Task<AgentStatusSnapshot> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var operationStatus = profileOrchestrator.GetStatus();
        var configuration = await configurationStore.LoadAsync(cancellationToken);
        var displays = await displayManager.GetDisplaysAsync(cancellationToken);
        var diagnostics = await diagnosticsService.GetSnapshotAsync(cancellationToken);

        bool isTvConfigured = configuration.CouchDisplayIdentifier is not null;
        var matchedTv = isTvConfigured
            ? displays.FirstOrDefault(display => display.Identifier.Matches(configuration.CouchDisplayIdentifier!))
            : null;

        string configuredTv = configuration.CouchDisplayIdentity is not null
            ? $"{configuration.CouchDisplayIdentity.FriendlyName} ({configuration.CouchDisplayIdentity.StableId})"
            : configuration.CouchDisplayIdentifier?.Value ?? "Not configured";

        string tvConnectionStatus = !isTvConfigured
            ? "Not configured"
            : matchedTv is null
                ? "Disconnected"
                : matchedTv.IsActive
                    ? "Connected and active"
                    : "Connected";

        string steamStatus = steamLauncher.IsInstalled(configuration)
            ? steamLauncher.IsRunning()
                ? "Installed and running"
                : "Installed and not running"
            : "Not installed";

        return new AgentStatusSnapshot(
            CurrentMode: FormatMode(operationStatus.CurrentMode),
            CurrentOperation: FormatOperation(operationStatus.CurrentOperation),
            CurrentStep: FormatStep(operationStatus.CurrentStep, operationStatus.State),
            ConfiguredTv: configuredTv,
            TvConnectionStatus: tvConnectionStatus,
            ListeningAddresses: diagnostics.LanIpv4Addresses.Count == 0
                ? diagnostics.ApiHealthStatus
                : string.Join(", ", diagnostics.LanIpv4Addresses),
            MacAddress: diagnostics.MacAddress ?? "Unavailable",
            SteamStatus: steamStatus,
            LastResult: FormatLastResult(operationStatus));
    }

    private static string FormatMode(AgentMode? mode) => mode switch
    {
        AgentMode.Couch => "Couch Mode",
        AgentMode.Desktop => "Desktop Mode",
        _ => "Unknown"
    };

    private static string FormatOperation(ProfileOperationType operationType) => operationType switch
    {
        ProfileOperationType.ActivateCouchMode => "Activate Couch Mode",
        ProfileOperationType.ActivateDesktopMode => "Restore Desktop Mode",
        _ => "Idle"
    };

    private static string FormatStep(ProfileOperationStep step, AgentOperationState state)
    {
        if (state == AgentOperationState.Idle || step == ProfileOperationStep.None)
        {
            return "Idle";
        }

        if (step == ProfileOperationStep.Completed)
        {
            return "Completed";
        }

        return step switch
        {
            ProfileOperationStep.Validating => "Validating",
            ProfileOperationStep.LoadingConfiguration => "Loading configuration",
            ProfileOperationStep.MatchingDisplay => "Matching display",
            ProfileOperationStep.CapturingSnapshot => "Capturing snapshot",
            ProfileOperationStep.PersistingSnapshot => "Persisting snapshot",
            ProfileOperationStep.ActivatingDisplay => "Activating display",
            ProfileOperationStep.LaunchingLauncher => "Launching game launcher",
            ProfileOperationStep.LoadingSnapshot => "Loading snapshot",
            ProfileOperationStep.RestoringDesktop => "Restoring desktop",
            _ => "Working"
        };
    }

    private static string FormatLastResult(AgentOperationStatus status)
    {
        if (status.LastOperationResult is null)
        {
            return "No completed operation yet";
        }

        string outcome = string.IsNullOrWhiteSpace(status.LastOperationResult.Outcome)
            ? status.State.ToString()
            : status.LastOperationResult.Outcome!;

        return $"{outcome}: {status.LastOperationResult.Message}";
    }
}
