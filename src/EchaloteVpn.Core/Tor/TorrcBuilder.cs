using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using EchaloteVpn.Core.Configuration;

namespace EchaloteVpn.Core.Tor;

/// <summary>
/// Génère le fichier torrc. Tous les ports n'écoutent qu'en local et le port de
/// contrôle est protégé par cookie.
/// </summary>
public static class TorrcBuilder
{
    /// <param name="outboundBindAddress">
    /// Adresse IPv4 de la carte physique : les connexions de Tor sortent par elle, pas par le tunnel.
    /// </param>
    public static string Build(EchaloteVpnSettings settings, TorPaths paths, int? owningProcessId = null, IPAddress? outboundBindAddress = null)
    {
        if (!settings.HasValidPorts())
        {
            throw new ArgumentException("Ports invalides ou en double.", nameof(settings));
        }
        if (!settings.HasValidDnsAddress())
        {
            throw new ArgumentException("Adresse DNS invalide : boucle locale IPv4 attendue.", nameof(settings));
        }

        var lines = new List<string>
        {
            Line($"SocksPort 127.0.0.1:{settings.SocksPort}"),
            Line($"ControlPort 127.0.0.1:{settings.ControlPort}"),
            Line($"DNSPort {settings.DnsAddress}:{settings.DnsPort}"),
            "CookieAuthentication 1",
            $"DataDirectory {Quote(paths.DataDirectory)}",
            $"GeoIPFile {Quote(paths.GeoIpFile)}",
            $"GeoIPv6File {Quote(paths.GeoIpv6File)}",
            "ClientOnly 1",
            "AvoidDiskWrites 1",
            // Les noms résolus par le DNSPort reçoivent une adresse virtuelle que Tor reconnaît ensuite.
            "AutomapHostsOnResolve 1",
            "VirtualAddrNetworkIPv4 10.192.0.0/10",
            "Log notice stdout",
        };

        if (owningProcessId is { } pid)
        {
            // Tor s'arrête de lui-même si le service disparaît.
            lines.Add(Line($"__OwningControllerProcess {pid}"));
        }

        if (outboundBindAddress is not null)
        {
            if (outboundBindAddress.AddressFamily != AddressFamily.InterNetwork)
            {
                throw new ArgumentException("OutboundBindAddress : adresse IPv4 attendue.", nameof(outboundBindAddress));
            }
            lines.Add($"OutboundBindAddress {outboundBindAddress}");
        }

        if (settings.Bridges.Count > 0)
        {
            var plugin = paths.TransportPlugin
                ?? throw new ArgumentException("Ponts demandés sans lyrebird.exe.", nameof(paths));
            if (plugin.Any(c => char.IsWhiteSpace(c) || c == '"'))
            {
                throw new ArgumentException("Le chemin de lyrebird.exe ne doit contenir ni espace ni guillemet.", nameof(paths));
            }
            lines.Add("UseBridges 1");
            lines.Add($"ClientTransportPlugin {BridgeLine.Transport} exec {plugin}");
            lines.AddRange(settings.Bridges.Select(bridge => $"Bridge {bridge}"));
        }

        if (settings.ExitCountries.Count > 0)
        {
            var countries = settings.ExitCountries.Select(NormalizeCountry);
            lines.Add($"ExitNodes {string.Join(',', countries.Select(c => $"{{{c}}}"))}");
            lines.Add("StrictNodes 1");
        }

        var builder = new StringBuilder();
        foreach (var line in lines)
        {
            builder.Append(line).Append('\n');
        }
        return builder.ToString();
    }

    private static string Line(FormattableString line) => line.ToString(CultureInfo.InvariantCulture);

    /// <summary>Valeur entre guillemets, avec les échappements de type C attendus par Tor.</summary>
    private static string Quote(string value) =>
        "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static string NormalizeCountry(string code)
    {
        if (code.Length != 2 || !code.All(char.IsAsciiLetter))
        {
            throw new ArgumentException($"Code pays invalide : « {code} » (deux lettres attendues).", nameof(code));
        }
        return code.ToLowerInvariant();
    }
}
