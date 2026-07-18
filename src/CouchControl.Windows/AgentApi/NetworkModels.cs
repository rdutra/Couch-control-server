using System.Net.NetworkInformation;

namespace CouchControl.Windows.AgentApi;

public static class AgentApiListeningInterface
{
    public const string Automatic = "auto";
}

public sealed record AgentNetworkInterface(
    string Id,
    string Name,
    string Description,
    string? MacAddress,
    NetworkInterfaceType Type,
    int? InterfaceIndex,
    bool IsUp,
    bool IsVirtual,
    bool IsVpn,
    bool IsLoopback,
    bool IsPrivateProfile,
    bool IsPublicProfile,
    IReadOnlyList<string> LanIpv4Addresses,
    IReadOnlyList<string> LanIpv4SubnetMasks,
    IReadOnlyList<string> WakeOnLanBroadcastAddresses,
    bool IsRecommended);

public sealed record AgentApiBindingPlan(
    int Port,
    string RequestedInterfaceId,
    string SelectedInterfaceId,
    string SelectedInterfaceName,
    string? MacAddress,
    IReadOnlyList<string> ListenUrls,
    IReadOnlyList<string> LanIpv4Addresses,
    IReadOnlyList<string> LanIpv4SubnetMasks,
    string? PreferredWakeOnLanBroadcastAddress,
    IReadOnlyList<string> WakeOnLanBroadcastAddresses,
    bool IsPrivateNetwork,
    string StatusMessage);

public sealed record FirewallRuleStatus(
    bool Exists,
    bool MatchesExpectedConfiguration,
    string StatusText,
    bool RequiresElevationForChanges,
    string RuleName);

public sealed record FirewallRuleChangeResult(
    bool Succeeded,
    string Message,
    bool ElevationRequired,
    bool Cancelled);

public sealed record AgentApiHealthSnapshot(
    bool IsListening,
    string StatusText,
    IReadOnlyList<string> ListenUrls);

public sealed record AgentNetworkDiagnosticsSnapshot(
    string HostName,
    int Port,
    string ListeningInterface,
    IReadOnlyList<string> LanIpv4Addresses,
    string? MacAddress,
    string FirewallRuleStatus,
    string ApiHealthStatus,
    string NetworkProfileStatus);
