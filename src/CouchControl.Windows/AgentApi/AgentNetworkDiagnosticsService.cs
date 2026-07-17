using System.Net;
using CouchControl.Core.Abstractions;

namespace CouchControl.Windows.AgentApi;

public interface IAgentNetworkDiagnosticsService
{
    Task<AgentNetworkDiagnosticsSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);
}

public sealed class AgentNetworkDiagnosticsService : IAgentNetworkDiagnosticsService
{
    private readonly IAgentConfigurationStore configurationStore;
    private readonly ILocalNetworkInterfaceProvider networkInterfaceProvider;
    private readonly IWindowsFirewallRuleManager firewallRuleManager;
    private readonly IAgentApiHealthState apiHealthState;

    public AgentNetworkDiagnosticsService(
        IAgentConfigurationStore configurationStore,
        ILocalNetworkInterfaceProvider networkInterfaceProvider,
        IWindowsFirewallRuleManager firewallRuleManager,
        IAgentApiHealthState apiHealthState)
    {
        this.configurationStore = configurationStore;
        this.networkInterfaceProvider = networkInterfaceProvider;
        this.firewallRuleManager = firewallRuleManager;
        this.apiHealthState = apiHealthState;
    }

    public async Task<AgentNetworkDiagnosticsSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var configuration = await configurationStore.LoadAsync(cancellationToken);
        var bindingPlan = networkInterfaceProvider.CreateBindingPlan(configuration);
        var firewallStatus = firewallRuleManager.GetStatus(configuration.ApiPort);
        var health = apiHealthState.GetSnapshot();
        var profileStatus = networkInterfaceProvider.GetNetworkProfileStatus(bindingPlan.SelectedInterfaceId);

        return new AgentNetworkDiagnosticsSnapshot(
            Dns.GetHostName(),
            configuration.ApiPort,
            string.IsNullOrWhiteSpace(bindingPlan.SelectedInterfaceName) ? bindingPlan.StatusMessage : bindingPlan.SelectedInterfaceName,
            bindingPlan.LanIpv4Addresses,
            bindingPlan.MacAddress,
            firewallStatus.StatusText,
            health.StatusText,
            profileStatus);
    }
}
