namespace EchaloteVpn.Core.Tunnel;

/// <summary>
/// Adresse de la carte Wintun et routes qui y envoient le trafic IPv4.
/// La route par défaut de Windows (0.0.0.0/0) n'est pas touchée : nos deux moitiés,
/// plus précises, passent devant, et les retirer remet Windows dans son état d'avant.
/// </summary>
public sealed class TunnelRoutePlan
{
    public static TunnelRoutePlan Default { get; } = new(Ipv4Prefix.Parse("10.66.0.1/30"));

    public TunnelRoutePlan(Ipv4Prefix localAddress)
    {
        if (localAddress.Length > 30)
        {
            throw new ArgumentException("Le réseau de la carte doit être un /30 ou plus large.", nameof(localAddress));
        }
        if (localAddress.Address.Equals(localAddress.Network) || localAddress.Address.Equals(localAddress.Broadcast))
        {
            throw new ArgumentException("L'adresse de la carte ne peut être ni le réseau, ni la diffusion.", nameof(localAddress));
        }
        LocalAddress = localAddress;
    }

    /// <summary>Adresse posée sur la carte Wintun.</summary>
    public Ipv4Prefix LocalAddress { get; }

    /// <summary>
    /// Les deux moitiés de l'espace IPv4. Elles sont moins précises que 127.0.0.0/8 :
    /// le trafic local reste en local.
    /// </summary>
    public IReadOnlyList<Ipv4Prefix> Routes { get; } =
    [
        Ipv4Prefix.Parse("0.0.0.0/1"),
        Ipv4Prefix.Parse("128.0.0.0/1"),
    ];

    /// <summary>Métrique des routes, ajoutée par Windows à celle de la carte.</summary>
    public uint RouteMetric => 0;
}
