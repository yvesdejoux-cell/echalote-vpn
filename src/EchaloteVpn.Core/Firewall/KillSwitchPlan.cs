using System.Net;
using System.Net.Sockets;
using EchaloteVpn.Core.Configuration;
using EchaloteVpn.Core.Tunnel;

namespace EchaloteVpn.Core.Firewall;

/// <summary>Couches WFP où Windows autorise ou refuse une connexion (ALE).</summary>
public enum WfpLayer
{
    ConnectV4,
    ConnectV6,
    AcceptV4,
    AcceptV6,
}

public enum FilterAction
{
    Permit,
    Block,
}

/// <summary>Condition d'un filtre. Plusieurs conditions d'un même filtre doivent toutes être vraies.</summary>
public abstract record FilterCondition;

/// <summary>Trafic de la boucle locale (127.0.0.0/8, ::1).</summary>
public sealed record LoopbackCondition : FilterCondition;

/// <summary>Programme tor.exe du service, reconnu par son chemin.</summary>
public sealed record TorProgramCondition : FilterCondition;

/// <summary>Programme lyrebird.exe du service (transport des ponts obfs4), reconnu par son chemin.</summary>
public sealed record TransportProgramCondition : FilterCondition;

public sealed record LocalAddressCondition(IPAddress Address) : FilterCondition;

public sealed record ProtocolCondition(ProtocolType Protocol) : FilterCondition;

public sealed record LocalPortCondition(ushort Port) : FilterCondition;

public sealed record RemotePortCondition(ushort Port) : FilterCondition;

/// <summary>Adresse distante dans un préfixe, IPv4 ou IPv6.</summary>
public sealed record RemotePrefixCondition(IPAddress Network, int Length) : FilterCondition
{
    public override string ToString() => $"{Network}/{Length}";
}

/// <summary>Un filtre WFP. Le poids (0 à 15) départage les filtres de notre sous-couche : le plus lourd gagne.</summary>
public sealed record FilterSpec(string Name, WfpLayer Layer, FilterAction Action, byte Weight, IReadOnlyList<FilterCondition> Conditions);

/// <summary>
/// Filtres du kill switch : seul tor.exe sort directement, le reste passe par le tunnel en TCP,
/// et tout ce qui reste est bloqué (IPv6, UDP, DNS vers la box, sorties pendant une coupure).
/// </summary>
public static class KillSwitchPlan
{
    public const byte BlockWeight = 0;

    public static readonly IReadOnlyList<WfpLayer> AllLayers =
        [WfpLayer.ConnectV4, WfpLayer.ConnectV6, WfpLayer.AcceptV4, WfpLayer.AcceptV6];

    /// <summary>Réseaux privés, liens locaux, multidiffusion et diffusion.</summary>
    public static readonly IReadOnlyList<RemotePrefixCondition> LocalNetworksV4 =
    [
        new(IPAddress.Parse("10.0.0.0"), 8),
        new(IPAddress.Parse("172.16.0.0"), 12),
        new(IPAddress.Parse("192.168.0.0"), 16),
        new(IPAddress.Parse("169.254.0.0"), 16),
        new(IPAddress.Parse("224.0.0.0"), 4),
        new(IPAddress.Parse("255.255.255.255"), 32),
    ];

    public static readonly IReadOnlyList<RemotePrefixCondition> LocalNetworksV6 =
    [
        new(IPAddress.Parse("fe80::"), 10),
        new(IPAddress.Parse("fc00::"), 7),
        new(IPAddress.Parse("ff00::"), 8),
    ];

    public static IReadOnlyList<FilterSpec> Build(EchaloteVpnSettings settings, TunnelRoutePlan tunnel)
    {
        var filters = new List<FilterSpec>();

        foreach (var layer in AllLayers)
        {
            filters.Add(new("Boucle locale", layer, FilterAction.Permit, 15, [new LoopbackCondition()]));
        }

        filters.Add(new("tor.exe", WfpLayer.ConnectV4, FilterAction.Permit, 14, [new TorProgramCondition()]));
        if (settings.Bridges.Count > 0)
        {
            // lyrebird.exe rejoint les ponts lui-même, par la carte physique.
            filters.Add(new("lyrebird.exe", WfpLayer.ConnectV4, FilterAction.Permit, 14, [new TransportProgramCondition()]));
        }

        filters.Add(new(
            "Tunnel TCP",
            WfpLayer.ConnectV4,
            FilterAction.Permit,
            13,
            [new LocalAddressCondition(tunnel.LocalAddress.Address), new ProtocolCondition(ProtocolType.Tcp)]));

        foreach (var layer in new[] { WfpLayer.ConnectV4, WfpLayer.AcceptV4 })
        {
            filters.Add(new(
                "DHCP",
                layer,
                FilterAction.Permit,
                12,
                [new ProtocolCondition(ProtocolType.Udp), new LocalPortCondition(68), new RemotePortCondition(67)]));
        }

        if (settings.AllowLocalNetwork)
        {
            foreach (var layer in AllLayers)
            {
                var networks = layer is WfpLayer.ConnectV4 or WfpLayer.AcceptV4 ? LocalNetworksV4 : LocalNetworksV6;
                foreach (var network in networks)
                {
                    filters.Add(new($"Réseau local {network}", layer, FilterAction.Permit, 11, [network]));
                }
            }
        }

        foreach (var layer in AllLayers)
        {
            filters.Add(new("Tout le reste", layer, FilterAction.Block, BlockWeight, []));
        }

        return filters;
    }

    public static bool IsIpv6(WfpLayer layer) => layer is WfpLayer.ConnectV6 or WfpLayer.AcceptV6;

    public static bool IsIpv6(this RemotePrefixCondition prefix) => prefix.Network.AddressFamily == AddressFamily.InterNetworkV6;
}
