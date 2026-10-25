using System.Globalization;
using System.Net;

namespace EchaloteVpn.Core.Tor;

/// <summary>
/// Lecture des réponses GETINFO utiles pour afficher le pays de sortie : circuit-status,
/// ns/id/&lt;empreinte&gt; et ip-to-country/&lt;adresse&gt;.
/// </summary>
public static class TorCircuits
{
    /// <summary>
    /// Empreinte du relais de sortie du dernier circuit construit pour le trafic ordinaire
    /// (PURPOSE=GENERAL, pas interne), ou null s'il n'y en a pas.
    /// </summary>
    /// <param name="lines">Une ligne par circuit : « 12 BUILT $AAA~a,$BBB~b,$CCC~c PURPOSE=GENERAL … ».</param>
    public static string? FindExitFingerprint(IEnumerable<string> lines)
    {
        string? exit = null;
        var newest = -1L;
        foreach (var line in lines)
        {
            var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 3
                || fields[1] != "BUILT"
                || !long.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out var id)
                || id <= newest
                || !fields.Contains("PURPOSE=GENERAL")
                || fields.Any(f => f.StartsWith("BUILD_FLAGS=", StringComparison.Ordinal)
                    && (f.Contains("IS_INTERNAL", StringComparison.Ordinal) || f.Contains("ONEHOP_TUNNEL", StringComparison.Ordinal))))
            {
                continue;
            }

            var hops = fields[2].Split(',');
            if (hops.Length < 3)
            {
                continue;
            }
            var last = hops[^1].TrimStart('$');
            var fingerprint = last.Split('~', '=')[0];
            if (fingerprint.Length == 40 && fingerprint.All(char.IsAsciiHexDigit))
            {
                exit = fingerprint.ToUpperInvariant();
                newest = id;
            }
        }
        return exit;
    }

    /// <summary>Adresse IPv4 d'un relais, lue sur la ligne « r » de son entrée ns/id.</summary>
    /// <param name="lines">
    /// « r nom identité [condensé] date heure IP portOR portDir » : le condensé manque dans le
    /// consensus des microdescripteurs, d'où une adresse cherchée par sa forme, pas par sa place.
    /// </param>
    public static IPAddress? FindRouterAddress(IEnumerable<string> lines)
    {
        foreach (var line in lines)
        {
            var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 6 || fields[0] != "r")
            {
                continue;
            }
            foreach (var field in fields.Skip(3))
            {
                if (field.Count(c => c == '.') == 3
                    && IPAddress.TryParse(field, out var address)
                    && address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                {
                    return address;
                }
            }
        }
        return null;
    }

    /// <summary>Code pays en majuscules (« DE »), ou null si Tor ne le connaît pas (« ?? »).</summary>
    public static string? ParseCountry(string value)
    {
        var code = value.Trim();
        return code.Length == 2 && code.All(char.IsAsciiLetter) ? code.ToUpperInvariant() : null;
    }
}
