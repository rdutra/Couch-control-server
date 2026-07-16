using System.Net;
using System.Net.NetworkInformation;
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
                    NetworkInterfaceType.Ethernet,
                    true,
                    7,
                    [IPAddress.Parse("192.168.1.40")]),
                new NetworkAdapterSnapshot(
                    "vpn",
                    "Tailscale",
                    "Tailscale Tunnel",
                    NetworkInterfaceType.Tunnel,
                    true,
                    9,
                    [IPAddress.Parse("100.90.10.12")]),
                new NetworkAdapterSnapshot(
                    "virtual",
                    "vEthernet",
                    "Hyper-V Virtual Ethernet Adapter",
                    NetworkInterfaceType.Ethernet,
                    true,
                    12,
                    [IPAddress.Parse("192.168.50.1")]),
                new NetworkAdapterSnapshot(
                    "wifi-public",
                    "Wi-Fi",
                    "Intel Wireless",
                    NetworkInterfaceType.Wireless80211,
                    true,
                    8,
                    [IPAddress.Parse("192.168.0.25")])
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
        Assert.Equal(["http://192.168.1.40:47981"], plan.ListenUrls);
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
                    NetworkInterfaceType.Ethernet,
                    true,
                    7,
                    [IPAddress.Parse("192.168.1.40")]),
                new NetworkAdapterSnapshot(
                    "wifi",
                    "Wi-Fi",
                    "Intel Wireless",
                    NetworkInterfaceType.Wireless80211,
                    true,
                    8,
                    [IPAddress.Parse("192.168.0.25")])
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

    private sealed class FakeNetworkInterfaceSystem(
        IReadOnlyList<NetworkAdapterSnapshot> adapters,
        IReadOnlyList<NetworkProfileSnapshot> profiles) : INetworkInterfaceSystem
    {
        public IReadOnlyList<NetworkAdapterSnapshot> GetNetworkAdapters() => adapters;

        public IReadOnlyList<NetworkProfileSnapshot> GetNetworkProfiles() => profiles;
    }
}
