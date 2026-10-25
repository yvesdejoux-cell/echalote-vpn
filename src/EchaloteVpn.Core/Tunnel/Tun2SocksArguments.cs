using System.Text.RegularExpressions;

namespace EchaloteVpn.Core.Tunnel;

/// <summary>
/// Ligne de commande de tun2socks : une carte Wintun dont le trafic part vers le SOCKS5 local de Tor.
/// </summary>
public static partial class Tun2SocksArguments
{
    public static IReadOnlyList<string> Build(string deviceName, int socksPort)
    {
        if (!IsValidDeviceName(deviceName))
        {
            throw new ArgumentException(
                "Nom de carte invalide : lettres, chiffres et tirets seulement (64 au plus).", nameof(deviceName));
        }
        if (socksPort is <= 0 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(socksPort), socksPort, "Port SOCKS invalide.");
        }

        return
        [
            "--device", $"tun://{deviceName}",
            "--proxy", $"socks5://127.0.0.1:{socksPort}",
            "--loglevel", "info",
        ];
    }

    /// <summary>Le nom passe dans une URL (tun://nom) : pas d'espace ni de séparateur.</summary>
    public static bool IsValidDeviceName(string? deviceName) =>
        deviceName is not null && DeviceNamePattern().IsMatch(deviceName);

    [GeneratedRegex("^[A-Za-z0-9-]{1,64}$")]
    private static partial Regex DeviceNamePattern();
}
