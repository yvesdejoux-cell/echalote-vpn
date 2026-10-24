using System.ComponentModel;
using System.Net;
using OnionGuard.Core.Tunnel;

namespace OnionGuard.Service.Tunnel;

/// <summary>
/// Pose l'adresse de la carte Wintun et les routes qui y envoient le trafic IPv4, puis les retire.
/// Si le service plante, Windows supprime ces routes avec la carte. Les routes /32 vers les ponts
/// passent par la carte physique : lyrebird.exe rejoint les ponts hors du tunnel.
/// </summary>
public sealed class RouteManager(TunnelOptions options, ILogger<RouteManager> logger)
{
    private readonly List<Ipv4Prefix> appliedRoutes = [];
    private readonly List<Ipv4Prefix> appliedBridgeRoutes = [];
    private PhysicalInterface? bridgeInterface;
    private ulong? interfaceLuid;
    private bool addressApplied;

    public bool IsApplied => interfaceLuid is not null;

    /// <summary>Pose l'adresse puis les routes. En cas d'échec, ce qui a été fait est défait.</summary>
    /// <param name="physical">Carte physique, pour les routes vers les ponts.</param>
    /// <param name="bridges">Adresses des ponts (aucune sans pont).</param>
    public void Apply(PhysicalInterface physical, IReadOnlyCollection<IPAddress> bridges)
    {
        if (IsApplied)
        {
            throw new InvalidOperationException("Les routes du tunnel sont déjà en place.");
        }

        var plan = options.RoutePlan;
        interfaceLuid = IpHelper.GetInterfaceLuid(options.DeviceName);
        try
        {
            IpHelper.AddAddress(interfaceLuid.Value, plan.LocalAddress);
            addressApplied = true;
            foreach (var route in plan.Routes)
            {
                IpHelper.AddRoute(interfaceLuid.Value, route, plan.RouteMetric);
                appliedRoutes.Add(route);
            }
            bridgeInterface = physical;
            foreach (var bridge in bridges)
            {
                var host = new Ipv4Prefix(bridge, 32);
                IpHelper.AddRoute(physical.InterfaceLuid, host, plan.RouteMetric, physical.Gateway);
                appliedBridgeRoutes.Add(host);
            }
        }
        catch
        {
            Remove();
            throw;
        }
        logger.LogInformation(
            "Carte {Device} : adresse {Address}, routes {Routes}.",
            options.DeviceName, plan.LocalAddress, string.Join(", ", plan.Routes));
        if (appliedBridgeRoutes.Count > 0)
        {
            logger.LogInformation(
                "Routes vers {Count} pont(s) par la passerelle {Gateway} (hors tunnel).",
                appliedBridgeRoutes.Count, physical.Gateway);
        }
    }

    /// <summary>Retire les routes puis l'adresse. Rien à faire si rien n'est en place.</summary>
    public void Remove()
    {
        if (interfaceLuid is not { } luid)
        {
            return;
        }

        var plan = options.RoutePlan;
        if (bridgeInterface is { } bridgeCard)
        {
            foreach (var host in appliedBridgeRoutes)
            {
                Try(() => IpHelper.DeleteRoute(bridgeCard.InterfaceLuid, host, plan.RouteMetric, bridgeCard.Gateway));
            }
        }
        appliedBridgeRoutes.Clear();
        bridgeInterface = null;
        for (var i = appliedRoutes.Count - 1; i >= 0; i--)
        {
            Try(() => IpHelper.DeleteRoute(luid, appliedRoutes[i], plan.RouteMetric));
        }
        appliedRoutes.Clear();
        if (addressApplied)
        {
            Try(() => IpHelper.DeleteAddress(luid, plan.LocalAddress));
            addressApplied = false;
        }
        interfaceLuid = null;
        logger.LogInformation("Routes du tunnel retirées.");
    }

    // Le retrait continue malgré une erreur : la carte a pu disparaître, emportant ses routes.
    private void Try(Action action)
    {
        try
        {
            action();
        }
        catch (Win32Exception ex)
        {
            logger.LogWarning("{Message}", ex.Message);
        }
    }
}
