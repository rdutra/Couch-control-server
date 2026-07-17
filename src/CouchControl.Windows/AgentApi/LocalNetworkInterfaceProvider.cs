using System.Net;
using System.Net.NetworkInformation;
using CouchControl.Core.Models;

namespace CouchControl.Windows.AgentApi;

public interface ILocalNetworkInterfaceProvider
{
    IReadOnlyList<AgentNetworkInterface> GetInterfaces();

    AgentApiBindingPlan CreateBindingPlan(AgentConfiguration configuration);

    string GetNetworkProfileStatus(string? selectedInterfaceId = null);
}

public sealed class LocalNetworkInterfaceProvider : ILocalNetworkInterfaceProvider
{
    private static readonly string[] VirtualKeywords =
    [
        "virtual",
        "vmware",
        "hyper-v",
        "vethernet",
        "loopback",
        "docker",
        "wsl",
        "vethernet",
        "virtualbox"
    ];

    private static readonly string[] VpnKeywords =
    [
        "vpn",
        "wireguard",
        "tailscale",
        "zerotier",
        "openvpn",
        "hamachi",
        "tap-",
        "tun",
        "ppp"
    ];

    private readonly INetworkInterfaceSystem networkSystem;

    public LocalNetworkInterfaceProvider()
        : this(new NetworkInterfaceSystem())
    {
    }

    internal LocalNetworkInterfaceProvider(INetworkInterfaceSystem networkSystem)
    {
        this.networkSystem = networkSystem;
    }

    public IReadOnlyList<AgentNetworkInterface> GetInterfaces()
    {
        var profiles = networkSystem.GetNetworkProfiles();
        return networkSystem.GetNetworkAdapters()
            .Select(adapter => ToInterface(adapter, profiles))
            .Where(static adapter => adapter.IsUp && adapter.LanIpv4Addresses.Count > 0)
            .OrderByDescending(static adapter => adapter.IsRecommended)
            .ThenBy(static adapter => GetPriority(adapter.Type))
            .ThenBy(static adapter => adapter.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public AgentApiBindingPlan CreateBindingPlan(AgentConfiguration configuration)
    {
        var interfaces = GetInterfaces();
        var requestedId = string.IsNullOrWhiteSpace(configuration.ApiListeningInterfaceId)
            ? AgentApiListeningInterface.Automatic
            : configuration.ApiListeningInterfaceId.Trim();

        var selected = ResolveSelectedInterface(requestedId, interfaces);
        if (selected is null)
        {
            return new AgentApiBindingPlan(
                configuration.ApiPort,
                requestedId,
                string.Empty,
                "No eligible LAN interface",
                null,
                Array.Empty<string>(),
                Array.Empty<string>(),
                false,
                "No active private LAN IPv4 interface is available for the agent API.");
        }

        var urls = selected.LanIpv4Addresses
            .Select(address => $"http://{address}:{configuration.ApiPort}")
            .ToArray();

        return new AgentApiBindingPlan(
            configuration.ApiPort,
            requestedId,
            selected.Id,
            selected.Name,
            selected.MacAddress,
            urls,
            selected.LanIpv4Addresses,
            selected.IsPrivateProfile,
            $"{selected.Name}: {string.Join(", ", selected.LanIpv4Addresses)}");
    }

    public string GetNetworkProfileStatus(string? selectedInterfaceId = null)
    {
        var interfaces = GetInterfaces();
        if (!string.IsNullOrWhiteSpace(selectedInterfaceId) &&
            !string.Equals(selectedInterfaceId, AgentApiListeningInterface.Automatic, StringComparison.OrdinalIgnoreCase))
        {
            var selected = interfaces.FirstOrDefault(adapter => string.Equals(adapter.Id, selectedInterfaceId, StringComparison.OrdinalIgnoreCase));
            return selected is null ? "Unknown" : GetProfileLabel(selected);
        }

        var profileValues = interfaces
            .Select(GetProfileLabel)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return profileValues.Length switch
        {
            0 => "Unknown",
            1 => profileValues[0],
            _ => "Mixed"
        };
    }

    private static AgentNetworkInterface? ResolveSelectedInterface(string requestedId, IReadOnlyList<AgentNetworkInterface> interfaces)
    {
        if (!string.Equals(requestedId, AgentApiListeningInterface.Automatic, StringComparison.OrdinalIgnoreCase))
        {
            var selected = interfaces.FirstOrDefault(adapter => string.Equals(adapter.Id, requestedId, StringComparison.OrdinalIgnoreCase));
            if (selected is not null)
            {
                return selected;
            }
        }

        return interfaces.FirstOrDefault(static adapter => adapter.IsRecommended);
    }

    private static AgentNetworkInterface ToInterface(NetworkAdapterSnapshot adapter, IReadOnlyList<NetworkProfileSnapshot> profiles)
    {
        var text = $"{adapter.Name} {adapter.Description}";
        var isVirtual = adapter.Type is NetworkInterfaceType.Loopback
            || ContainsKeyword(text, VirtualKeywords);
        var isVpn = adapter.Type is NetworkInterfaceType.Ppp or NetworkInterfaceType.Tunnel
            || ContainsKeyword(text, VpnKeywords);
        var profile = FindProfile(adapter, profiles);
        var addresses = adapter.UnicastAddresses
            .Where(static address => IsLanIpv4(address))
            .Select(static address => address.ToString())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var isRecommended = adapter.IsUp &&
            addresses.Length > 0 &&
            !isVirtual &&
            !isVpn &&
            profile?.Category != NetworkCategory.Public;

        return new AgentNetworkInterface(
            adapter.Id,
            string.IsNullOrWhiteSpace(adapter.Name) ? adapter.Description : adapter.Name,
            adapter.Description,
            FormatMacAddress(adapter.PhysicalAddress),
            adapter.Type,
            adapter.InterfaceIndex,
            adapter.IsUp,
            isVirtual,
            isVpn,
            adapter.Type == NetworkInterfaceType.Loopback,
            profile?.Category == NetworkCategory.Private,
            profile?.Category == NetworkCategory.Public,
            addresses,
            isRecommended);
    }

    private static NetworkProfileSnapshot? FindProfile(NetworkAdapterSnapshot adapter, IReadOnlyList<NetworkProfileSnapshot> profiles) =>
        profiles.FirstOrDefault(profile =>
            (adapter.InterfaceIndex is not null && profile.InterfaceIndex == adapter.InterfaceIndex) ||
            string.Equals(profile.InterfaceAlias, adapter.Name, StringComparison.OrdinalIgnoreCase));

    private static bool ContainsKeyword(string value, IReadOnlyList<string> keywords) =>
        keywords.Any(keyword => value.Contains(keyword, StringComparison.OrdinalIgnoreCase));

    private static bool IsLanIpv4(IPAddress address)
    {
        if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
        {
            return false;
        }

        var bytes = address.GetAddressBytes();
        return bytes[0] switch
        {
            10 => true,
            172 when bytes[1] is >= 16 and <= 31 => true,
            192 when bytes[1] == 168 => true,
            _ => false
        };
    }

    private static int GetPriority(NetworkInterfaceType type) => type switch
    {
        NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetFx or NetworkInterfaceType.FastEthernetT => 0,
        NetworkInterfaceType.Wireless80211 => 1,
        _ => 2
    };

    private static string GetProfileLabel(AgentNetworkInterface adapter)
    {
        if (adapter.IsPrivateProfile)
        {
            return "Private";
        }

        if (adapter.IsPublicProfile)
        {
            return "Public";
        }

        return "Unknown";
    }

    private static string? FormatMacAddress(PhysicalAddress? physicalAddress)
    {
        if (physicalAddress is null)
        {
            return null;
        }

        var bytes = physicalAddress.GetAddressBytes();
        return bytes.Length == 0
            ? null
            : string.Join(":", bytes.Select(static value => value.ToString("X2")));
    }
}

internal interface INetworkInterfaceSystem
{
    IReadOnlyList<NetworkAdapterSnapshot> GetNetworkAdapters();

    IReadOnlyList<NetworkProfileSnapshot> GetNetworkProfiles();
}

internal sealed class NetworkInterfaceSystem : INetworkInterfaceSystem
{
    private readonly INetworkProfileReader profileReader;

    public NetworkInterfaceSystem()
        : this(new PowerShellNetworkProfileReader())
    {
    }

    internal NetworkInterfaceSystem(INetworkProfileReader profileReader)
    {
        this.profileReader = profileReader;
    }

    public IReadOnlyList<NetworkAdapterSnapshot> GetNetworkAdapters() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Select(static adapter =>
            {
                IPv4InterfaceProperties? ipv4 = null;
                try
                {
                    ipv4 = adapter.GetIPProperties().GetIPv4Properties();
                }
                catch
                {
                }

                return new NetworkAdapterSnapshot(
                    adapter.Id,
                    adapter.Name,
                    adapter.Description,
                    adapter.GetPhysicalAddress(),
                    adapter.NetworkInterfaceType,
                    adapter.OperationalStatus == OperationalStatus.Up,
                    ipv4?.Index,
                    adapter.GetIPProperties().UnicastAddresses.Select(static address => address.Address).ToArray());
            })
            .ToArray();

    public IReadOnlyList<NetworkProfileSnapshot> GetNetworkProfiles() => profileReader.ReadProfiles();
}

internal sealed record NetworkAdapterSnapshot(
    string Id,
    string Name,
    string Description,
    PhysicalAddress? PhysicalAddress,
    NetworkInterfaceType Type,
    bool IsUp,
    int? InterfaceIndex,
    IReadOnlyList<IPAddress> UnicastAddresses);

internal enum NetworkCategory
{
    Unknown,
    Private,
    Public
}

internal sealed record NetworkProfileSnapshot(
    int InterfaceIndex,
    string InterfaceAlias,
    NetworkCategory Category);
