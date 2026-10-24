using System.Net;
using System.Net.Sockets;

namespace OnionGuard.Core.Configuration;

/// <summary>
/// Réglages du client. Les ports sont ceux qu'utilisera tor.exe en local.
/// </summary>
public sealed record OnionGuardSettings
{
    /// <summary>Port SOCKS5 de Tor, vers lequel tun2socks renverra le trafic.</summary>
    public int SocksPort { get; init; } = 9050;

    /// <summary>Port de contrôle de Tor (bootstrap, NEWNYM, état des circuits).</summary>
    public int ControlPort { get; init; } = 9051;

    /// <summary>
    /// Adresse du DNSPort de Tor, vers laquelle une règle NRPT envoie tout le DNS de Windows.
    /// Une adresse de boucle locale à part, pour ne gêner aucun autre serveur sur 127.0.0.1:53.
    /// </summary>
    public string DnsAddress { get; init; } = "127.66.0.1";

    /// <summary>Port du DNSPort de Tor : 53, le seul que Windows sait interroger.</summary>
    public int DnsPort { get; init; } = 53;

    /// <summary>Codes pays ISO des nœuds de sortie souhaités (vide = au choix de Tor).</summary>
    public IReadOnlyList<string> ExitCountries { get; init; } = [];

    /// <summary>Bloque tout le trafic hors Tor, y compris quand Tor est coupé.</summary>
    public bool KillSwitchEnabled { get; init; } = true;

    /// <summary>Laisse passer le réseau local (box, imprimante, NAS) quand le kill switch est actif.</summary>
    public bool AllowLocalNetwork { get; init; }

    /// <summary>Ponts obfs4 à utiliser (vide = connexion directe au réseau Tor).</summary>
    public IReadOnlyList<Tor.BridgeLine> Bridges { get; init; } = [];

    /// <summary>Vérifie que les ports sont valides et distincts.</summary>
    public bool HasValidPorts()
    {
        int[] ports = [SocksPort, ControlPort, DnsPort];
        return ports.All(p => p is > 0 and <= 65535) && ports.Distinct().Count() == ports.Length;
    }

    /// <summary>Le DNSPort doit rester en boucle locale IPv4 : il ne doit jamais écouter sur le réseau.</summary>
    public bool HasValidDnsAddress() =>
        IPAddress.TryParse(DnsAddress, out var address)
        && address.AddressFamily == AddressFamily.InterNetwork
        && IPAddress.IsLoopback(address);
}
