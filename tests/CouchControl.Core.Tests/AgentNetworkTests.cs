using System.Net;
using System.Net.NetworkInformation;
using System.Buffers.Binary;
using System.Text;
using CouchControl.Core.Abstractions;
using CouchControl.Core.Models;
using CouchControl.Windows.AgentApi;

namespace CouchControl.Core.Tests;

public sealed class AgentNetworkTests
{
    [Fact]
    public void CreateBindingPlan_PrefersPrivatePhysicalInterface_OverVirtualOrPublicCandidates()
    {
        var provider = new LocalNetworkInterfaceProvider(new FakeNetworkInterfaceSystem(
            [
                new NetworkAdapterSnapshot(
                    "ethernet",
                    "Ethernet",
                    "Intel Ethernet Adapter",
                    PhysicalAddress.Parse("001122334455"),
                    NetworkInterfaceType.Ethernet,
                    true,
                    7,
                    [Ipv4("192.168.1.40", "255.255.255.0")]),
                new NetworkAdapterSnapshot(
                    "vpn",
                    "Tailscale",
                    "Tailscale Tunnel",
                    PhysicalAddress.Parse("001122334466"),
                    NetworkInterfaceType.Tunnel,
                    true,
                    9,
                    [Ipv4("100.90.10.12", "255.255.255.0")]),
                new NetworkAdapterSnapshot(
                    "virtual",
                    "vEthernet",
                    "Hyper-V Virtual Ethernet Adapter",
                    PhysicalAddress.Parse("001122334477"),
                    NetworkInterfaceType.Ethernet,
                    true,
                    12,
                    [Ipv4("192.168.50.1", "255.255.255.0")]),
                new NetworkAdapterSnapshot(
                    "wifi-public",
                    "Wi-Fi",
                    "Intel Wireless",
                    PhysicalAddress.Parse("001122334488"),
                    NetworkInterfaceType.Wireless80211,
                    true,
                    8,
                    [Ipv4("192.168.0.25", "255.255.255.0")])
            ],
            [
                new NetworkProfileSnapshot(7, "Ethernet", NetworkCategory.Private),
                new NetworkProfileSnapshot(8, "Wi-Fi", NetworkCategory.Public),
                new NetworkProfileSnapshot(9, "Tailscale", NetworkCategory.Private),
                new NetworkProfileSnapshot(12, "vEthernet", NetworkCategory.Private)
            ]));

        var plan = provider.CreateBindingPlan(new AgentConfiguration { ApiPort = 47981 });

        Assert.Equal("ethernet", plan.SelectedInterfaceId);
        Assert.Equal(["192.168.1.40"], plan.LanIpv4Addresses);
        Assert.Equal(["255.255.255.0"], plan.LanIpv4SubnetMasks);
        Assert.Equal("192.168.1.255", plan.PreferredWakeOnLanBroadcastAddress);
        Assert.Equal(["192.168.1.255", "255.255.255.255"], plan.WakeOnLanBroadcastAddresses);
        Assert.Equal(["http://192.168.1.40:47981"], plan.ListenUrls);
        Assert.Equal("00:11:22:33:44:55", plan.MacAddress);
    }

    [Fact]
    public void CreateBindingPlan_UsesRequestedInterface_WhenItHasEligibleLanAddress()
    {
        var provider = new LocalNetworkInterfaceProvider(new FakeNetworkInterfaceSystem(
            [
                new NetworkAdapterSnapshot(
                    "ethernet",
                    "Ethernet",
                    "Intel Ethernet Adapter",
                    PhysicalAddress.Parse("001122334455"),
                    NetworkInterfaceType.Ethernet,
                    true,
                    7,
                    [Ipv4("192.168.1.40", "255.255.255.0")]),
                new NetworkAdapterSnapshot(
                    "wifi",
                    "Wi-Fi",
                    "Intel Wireless",
                    PhysicalAddress.Parse("AABBCCDDEEFF"),
                    NetworkInterfaceType.Wireless80211,
                    true,
                    8,
                    [Ipv4("192.168.0.25", "255.255.255.0")])
            ],
            [
                new NetworkProfileSnapshot(7, "Ethernet", NetworkCategory.Private),
                new NetworkProfileSnapshot(8, "Wi-Fi", NetworkCategory.Private)
            ]));

        var plan = provider.CreateBindingPlan(new AgentConfiguration
        {
            ApiPort = 47981,
            ApiListeningInterfaceId = "wifi"
        });

        Assert.Equal("wifi", plan.SelectedInterfaceId);
        Assert.Equal(["192.168.0.25"], plan.LanIpv4Addresses);
        Assert.Equal(["255.255.255.0"], plan.LanIpv4SubnetMasks);
        Assert.Equal("192.168.0.255", plan.PreferredWakeOnLanBroadcastAddress);
        Assert.Equal(["192.168.0.255", "255.255.255.255"], plan.WakeOnLanBroadcastAddresses);
        Assert.Equal("AA:BB:CC:DD:EE:FF", plan.MacAddress);
    }

    [Fact]
    public void CreateBindingPlan_UsesSubnetMask_WhenComputingWakeOnLanBroadcastAddress()
    {
        var provider = new LocalNetworkInterfaceProvider(new FakeNetworkInterfaceSystem(
            [
                new NetworkAdapterSnapshot(
                    "ethernet",
                    "Ethernet",
                    "Intel Ethernet Adapter",
                    PhysicalAddress.Parse("001122334455"),
                    NetworkInterfaceType.Ethernet,
                    true,
                    7,
                    [Ipv4("192.168.1.40", "255.255.252.0")])
            ],
            [
                new NetworkProfileSnapshot(7, "Ethernet", NetworkCategory.Private)
            ]));

        var plan = provider.CreateBindingPlan(new AgentConfiguration { ApiPort = 47981 });

        Assert.Equal(["255.255.252.0"], plan.LanIpv4SubnetMasks);
        Assert.Equal("192.168.3.255", plan.PreferredWakeOnLanBroadcastAddress);
        Assert.Equal(["192.168.3.255", "255.255.255.255"], plan.WakeOnLanBroadcastAddresses);
    }

    [Fact]
    public async Task RuntimeOptionsProvider_RefreshesTheSelectedInterfaceAfterItsAddressChanges()
    {
        var networkSystem = new MutableNetworkInterfaceSystem(
            [new NetworkAdapterSnapshot(
                "wifi",
                "Wi-Fi",
                "Intel Wireless",
                PhysicalAddress.Parse("AABBCCDDEEFF"),
                NetworkInterfaceType.Wireless80211,
                true,
                8,
                [Ipv4("192.168.1.40", "255.255.255.0")])],
            [new NetworkProfileSnapshot(8, "Wi-Fi", NetworkCategory.Private)]);
        var provider = new LocalNetworkInterfaceProvider(networkSystem);
        var configuration = new AgentConfiguration
        {
            ApiPort = 47981,
            ApiListeningInterfaceId = "wifi"
        };
        var runtimeOptions = new AgentApiRuntimeOptionsProvider(
            new FixedConfigurationStore(configuration),
            provider);

        await runtimeOptions.LoadAsync();
        networkSystem.Adapters =
        [
            new NetworkAdapterSnapshot(
                "wifi",
                "Wi-Fi",
                "Intel Wireless",
                PhysicalAddress.Parse("AABBCCDDEEFF"),
                NetworkInterfaceType.Wireless80211,
                true,
                8,
                [Ipv4("192.168.1.75", "255.255.255.0")])
        ];

        Assert.True(runtimeOptions.RefreshNetworkBindingPlan());
        Assert.Equal("wifi", runtimeOptions.BindingPlan.SelectedInterfaceId);
        Assert.Equal(["192.168.1.75"], runtimeOptions.BindingPlan.LanIpv4Addresses);
        Assert.Equal(["http://192.168.1.75:47981"], runtimeOptions.BindingPlan.ListenUrls);
        Assert.False(runtimeOptions.RefreshNetworkBindingPlan());
    }

    [Fact]
    public void FirewallCommandBuilder_GeneratesPrivateTcpRuleCommands()
    {
        var create = FirewallCommandBuilder.BuildCreateCommand("CouchControl Rule", 47981);
        var remove = FirewallCommandBuilder.BuildRemoveCommand("CouchControl Rule");
        var query = FirewallCommandBuilder.BuildQueryCommand("CouchControl Rule");

        Assert.Equal("powershell.exe", create.FileName);
        Assert.Contains("-Profile Private", create.Arguments, StringComparison.Ordinal);
        Assert.Contains("-Protocol TCP", create.Arguments, StringComparison.Ordinal);
        Assert.Contains("-LocalPort 47981", create.Arguments, StringComparison.Ordinal);
        Assert.True(create.Elevate);

        Assert.Equal("powershell.exe", remove.FileName);
        Assert.Contains("Remove-NetFirewallRule", remove.Arguments, StringComparison.Ordinal);
        Assert.True(remove.Elevate);

        Assert.Equal("powershell.exe", query.FileName);
        Assert.Contains("Get-NetFirewallRule", query.Arguments, StringComparison.Ordinal);
        Assert.False(query.Elevate);
    }

    [Fact]
    public void FirewallCommandBuilder_ParsesFirewallRuleJson()
    {
        const string json = """
            {"Enabled":true,"Direction":"Inbound","Action":"Allow","Profile":"Private","Protocol":"TCP","LocalPort":"47981"}
            """;

        var rules = FirewallCommandBuilder.ParseFirewallRules(json);

        var rule = Assert.Single(rules);
        Assert.True(rule.Enabled);
        Assert.Equal("Inbound", rule.Direction);
        Assert.Equal("Allow", rule.Action);
        Assert.Equal("Private", rule.Profile);
        Assert.Equal("TCP", rule.Protocol);
        Assert.Equal("47981", rule.LocalPort);
    }

    [Fact]
    public void MdnsPacketBuilder_MatchesCouchControlServiceQueries()
    {
        var advertisement = new MdnsAdvertisement(
            "Living Room Gaming PC._couchcontrol._tcp.local.",
            "couch-pc.local.",
            47981,
            IPAddress.Parse("192.168.1.40"),
            ["api=/api/v1", "version=1"]);

        var query = CreateMdnsQuery(AgentMdnsAdvertisementService.ServiceType, recordType: 12);

        Assert.True(MdnsPacketBuilder.QueryMatches(query, advertisement));
    }

    [Fact]
    public void MdnsPacketBuilder_ResponseAdvertisesAgentEndpoint()
    {
        var advertisement = new MdnsAdvertisement(
            "Living Room Gaming PC._couchcontrol._tcp.local.",
            "couch-pc.local.",
            47981,
            IPAddress.Parse("192.168.1.40"),
            ["api=/api/v1", "version=1"]);

        var response = MdnsPacketBuilder.BuildResponse(advertisement, transactionId: 0);
        var responseText = Encoding.UTF8.GetString(response);

        Assert.Contains("_couchcontrol", responseText);
        Assert.Contains("Living Room Gaming PC", responseText);
        Assert.Contains("api=/api/v1", responseText);
        Assert.True(ContainsSequence(response, [192, 168, 1, 40]));
    }

    [Fact]
    public void MdnsPacketBuilder_GoodbyeWithdrawsCachedRecordsImmediately()
    {
        var advertisement = new MdnsAdvertisement(
            "Living Room Gaming PC._couchcontrol._tcp.local.",
            "couch-pc.local.",
            47981,
            IPAddress.Parse("192.168.1.40"),
            ["api=/api/v1", "version=1"]);

        var goodbye = MdnsPacketBuilder.BuildResponse(advertisement, transactionId: 0, ttlSeconds: 0);

        Assert.Equal(4, ReadRecordTtls(goodbye).Count);
        Assert.All(ReadRecordTtls(goodbye), static ttl => Assert.Equal(0u, ttl));
    }

    private static IReadOnlyList<uint> ReadRecordTtls(byte[] packet)
    {
        var recordCount = BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(6, 2));
        var offset = 12;
        var ttls = new List<uint>(recordCount);
        for (var index = 0; index < recordCount; index++)
        {
            while (offset < packet.Length)
            {
                var labelLength = packet[offset++];
                if (labelLength == 0)
                {
                    break;
                }

                if ((labelLength & 0xC0) == 0xC0)
                {
                    offset++;
                    break;
                }

                offset += labelLength;
            }

            offset += 4;
            ttls.Add(BinaryPrimitives.ReadUInt32BigEndian(packet.AsSpan(offset, 4)));
            offset += 4;
            var dataLength = BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(offset, 2));
            offset += 2 + dataLength;
        }

        return ttls;
    }

    private sealed class FakeNetworkInterfaceSystem(
        IReadOnlyList<NetworkAdapterSnapshot> adapters,
        IReadOnlyList<NetworkProfileSnapshot> profiles) : INetworkInterfaceSystem
    {
        public IReadOnlyList<NetworkAdapterSnapshot> GetNetworkAdapters() => adapters;

        public IReadOnlyList<NetworkProfileSnapshot> GetNetworkProfiles() => profiles;
    }

    private sealed class MutableNetworkInterfaceSystem(
        IReadOnlyList<NetworkAdapterSnapshot> adapters,
        IReadOnlyList<NetworkProfileSnapshot> profiles) : INetworkInterfaceSystem
    {
        public IReadOnlyList<NetworkAdapterSnapshot> Adapters { get; set; } = adapters;

        public IReadOnlyList<NetworkProfileSnapshot> Profiles { get; } = profiles;

        public IReadOnlyList<NetworkAdapterSnapshot> GetNetworkAdapters() => Adapters;

        public IReadOnlyList<NetworkProfileSnapshot> GetNetworkProfiles() => Profiles;
    }

    private sealed class FixedConfigurationStore(AgentConfiguration configuration) : IAgentConfigurationStore
    {
        public Task<AgentConfiguration> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(configuration);

        public Task SaveAsync(AgentConfiguration configuration, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private static NetworkIpv4AddressSnapshot Ipv4(string address, string mask) =>
        new(IPAddress.Parse(address), IPAddress.Parse(mask));

    private static byte[] CreateMdnsQuery(string name, ushort recordType)
    {
        using var stream = new MemoryStream();
        WriteUInt16(stream, 0);
        WriteUInt16(stream, 0);
        WriteUInt16(stream, 1);
        WriteUInt16(stream, 0);
        WriteUInt16(stream, 0);
        WriteUInt16(stream, 0);
        foreach (var label in name.TrimEnd('.').Split('.'))
        {
            var bytes = Encoding.UTF8.GetBytes(label);
            stream.WriteByte((byte)bytes.Length);
            stream.Write(bytes);
        }

        stream.WriteByte(0);
        WriteUInt16(stream, recordType);
        WriteUInt16(stream, 1);
        return stream.ToArray();
    }

    private static void WriteUInt16(MemoryStream stream, ushort value)
    {
        Span<byte> buffer = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(buffer, value);
        stream.Write(buffer);
    }

    private static bool ContainsSequence(byte[] source, byte[] expected)
    {
        for (var index = 0; index <= source.Length - expected.Length; index++)
        {
            if (source.AsSpan(index, expected.Length).SequenceEqual(expected))
            {
                return true;
            }
        }

        return false;
    }
}
