using System.ComponentModel;
using System.Net;

namespace OnionGuard.Service.Tunnel;

/// <summary>
/// Carte réseau par laquelle Windows sort vers Internet, hors tunnel. Son adresse sert à
/// <c>OutboundBindAddress</c> : les connexions de tor.exe partent par elle et ne bouclent pas.
/// Sa passerelle sert aux routes vers les ponts, que lyrebird.exe rejoint hors du tunnel.
/// </summary>
public sealed record PhysicalInterface(ulong InterfaceLuid, uint InterfaceIndex, IPAddress SourceAddress, IPAddress Gateway)
{
    // Adresse publique quelconque : on consulte seulement la table de routage.
    private static readonly IPAddress Probe = IPAddress.Parse("1.1.1.1");

    /// <summary>
    /// À appeler avant de poser les routes du tunnel : ensuite, la meilleure route est la carte Wintun.
    /// </summary>
    public static PhysicalInterface Find(ulong? tunnelLuid = null)
    {
        var (luid, index, source, gateway) = IpHelper.GetBestRoute(Probe);
        if (luid == tunnelLuid)
        {
            throw new InvalidOperationException(
                "La meilleure route passe déjà par la carte du tunnel : chercher la carte physique avant de poser les routes.");
        }
        return new PhysicalInterface(luid, index, source, gateway);
    }

    /// <summary>
    /// Vrai si cette carte sort toujours vers Internet avec la même adresse. Utilisable routes
    /// du tunnel en place : seules les routes de cette carte sont consultées.
    /// </summary>
    public bool IsStillCurrent()
    {
        try
        {
            return IpHelper.GetSourceAddress(InterfaceLuid, Probe).Equals(SourceAddress);
        }
        catch (Win32Exception)
        {
            return false;
        }
    }
}
