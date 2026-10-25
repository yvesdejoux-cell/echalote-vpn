using System.Net.Sockets;
using EchaloteVpn.Core.Configuration;
using EchaloteVpn.Core.Firewall;
using EchaloteVpn.Core.Tor;
using EchaloteVpn.Core.Tunnel;

namespace EchaloteVpn.Core.Tests;

public class KillSwitchPlanTests
{
    private static IReadOnlyList<FilterSpec> Build(bool allowLocalNetwork = false) =>
        KillSwitchPlan.Build(new EchaloteVpnSettings { AllowLocalNetwork = allowLocalNetwork }, TunnelRoutePlan.Default);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EachLayer_EndsWithUnconditionalBlock(bool allowLocalNetwork)
    {
        var filters = Build(allowLocalNetwork);

        foreach (var layer in KillSwitchPlan.AllLayers)
        {
            var block = Assert.Single(filters, f => f.Layer == layer && f.Action == FilterAction.Block);
            Assert.Empty(block.Conditions);
            Assert.Equal(KillSwitchPlan.BlockWeight, block.Weight);
        }
    }

    [Fact]
    public void Permits_OutweighTheBlock()
    {
        var filters = Build(allowLocalNetwork: true);

        Assert.All(
            filters.Where(f => f.Action == FilterAction.Permit),
            f => Assert.True(f.Weight > KillSwitchPlan.BlockWeight && f.Weight <= 15));
    }

    [Fact]
    public void ByDefault_NoLocalNetworkPermit()
    {
        Assert.DoesNotContain(Build(), f => f.Conditions.Any(c => c is RemotePrefixCondition));
    }

    [Fact]
    public void AllowLocalNetwork_PermitsPrivateRangesOnEachLayer()
    {
        var filters = Build(allowLocalNetwork: true);

        Assert.Contains(filters, f => f.Layer == WfpLayer.ConnectV4
            && f.Conditions.SequenceEqual([KillSwitchPlan.LocalNetworksV4[2]]));
        Assert.Contains(filters, f => f.Layer == WfpLayer.AcceptV6
            && f.Conditions.SequenceEqual([KillSwitchPlan.LocalNetworksV6[0]]));
        // Jamais un préfixe IPv4 sur une couche IPv6, ni l'inverse.
        Assert.All(
            filters.Where(f => f.Conditions.Any(c => c is RemotePrefixCondition)),
            f => Assert.Equal(KillSwitchPlan.IsIpv6(f.Layer), ((RemotePrefixCondition)f.Conditions[0]).IsIpv6()));
    }

    [Fact]
    public void Ipv6_OnlyLoopbackIsPermittedByDefault()
    {
        var permits = Build().Where(f => KillSwitchPlan.IsIpv6(f.Layer) && f.Action == FilterAction.Permit);

        Assert.All(permits, f => Assert.IsType<LoopbackCondition>(Assert.Single(f.Conditions)));
    }

    [Fact]
    public void TorProgram_IsPermittedOnlyOutboundIpv4()
    {
        var tor = Assert.Single(Build(), f => f.Conditions.Any(c => c is TorProgramCondition));

        Assert.Equal(WfpLayer.ConnectV4, tor.Layer);
        Assert.Equal(FilterAction.Permit, tor.Action);
    }

    [Fact]
    public void Tunnel_PermitsOnlyTcpFromTheTunnelAddress()
    {
        var tunnel = Assert.Single(Build(), f => f.Conditions.Any(c => c is LocalAddressCondition));

        Assert.Equal(WfpLayer.ConnectV4, tunnel.Layer);
        Assert.Contains(new LocalAddressCondition(TunnelRoutePlan.Default.LocalAddress.Address), tunnel.Conditions);
        Assert.Contains(new ProtocolCondition(ProtocolType.Tcp), tunnel.Conditions);
    }

    [Fact]
    public void OnlyUdpPermit_IsDhcp()
    {
        var udp = Build().Where(f => f.Conditions.Contains(new ProtocolCondition(ProtocolType.Udp))).ToList();

        Assert.Equal(2, udp.Count);
        Assert.All(udp, f =>
        {
            Assert.Contains(new LocalPortCondition(68), f.Conditions);
            Assert.Contains(new RemotePortCondition(67), f.Conditions);
        });
    }

    [Fact]
    public void TransportProgram_IsPermittedOnlyWithBridges()
    {
        BridgeLine.TryParse("obfs4 37.218.245.14:38224 D9A82D2F9C2F65A18407B1D2B764F130847F8B5D cert=bjRaMrr1BRiAW8IE9U5z27fQaYgOhX1UCmOpg2pFpoMvo6ZgQMzLsaTzzQNTlm7hNcb+Sg iat-mode=0", out var bridge);

        var withBridges = KillSwitchPlan.Build(new EchaloteVpnSettings { Bridges = [bridge!] }, TunnelRoutePlan.Default);
        var transport = Assert.Single(withBridges, f => f.Conditions.Any(c => c is TransportProgramCondition));

        Assert.Equal(WfpLayer.ConnectV4, transport.Layer);
        Assert.Equal(FilterAction.Permit, transport.Action);
        Assert.DoesNotContain(Build(), f => f.Conditions.Any(c => c is TransportProgramCondition));
    }
}
