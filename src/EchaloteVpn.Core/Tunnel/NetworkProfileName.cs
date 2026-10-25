using System.Text.RegularExpressions;

namespace EchaloteVpn.Core.Tunnel;

/// <summary>Noms que Windows donne aux profils réseau de la carte : « EchaloteVpn », « EchaloteVpn 2 »…</summary>
public static class NetworkProfileName
{
    /// <summary>Le nom exact de la carte, seul ou suivi d'un numéro ; jamais un autre réseau au nom proche.</summary>
    public static bool IsTunnelProfile(string profileName, string deviceName) =>
        Regex.IsMatch(profileName, $@"^{Regex.Escape(deviceName)}( [0-9]+)?$", RegexOptions.CultureInvariant);
}
