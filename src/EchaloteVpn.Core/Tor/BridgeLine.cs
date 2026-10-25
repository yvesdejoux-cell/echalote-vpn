using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EchaloteVpn.Core.Tor;

/// <summary>
/// Un pont obfs4 : « obfs4 IP:port empreinte cert=… iat-mode=N ». La forme est vérifiée en entier :
/// une ligne collée par l'utilisateur ne doit jamais pouvoir ajouter une directive au torrc.
/// </summary>
public sealed partial record BridgeLine(IPEndPoint Endpoint, string Fingerprint, string Certificate, int IatMode)
{
    public const string Transport = "obfs4";

    public static bool TryParse(string? text, out BridgeLine? bridge)
    {
        bridge = null;
        var match = Obfs4Pattern().Match(text ?? string.Empty);
        if (!match.Success
            || !IPAddress.TryParse(match.Groups["ip"].Value, out var address)
            || address.GetAddressBytes().Length != 4
            || !int.TryParse(match.Groups["port"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var port)
            || port is < 1 or > 65535)
        {
            return false;
        }
        bridge = new BridgeLine(
            new IPEndPoint(address, port),
            match.Groups["fingerprint"].Value.ToUpperInvariant(),
            match.Groups["cert"].Value,
            match.Groups["iat"].Value[0] - '0');
        return true;
    }

    /// <summary>Forme canonique, celle écrite dans le torrc après « Bridge ».</summary>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Transport} {Endpoint} {Fingerprint} cert={Certificate} iat-mode={IatMode}");

    /// <summary>Ponts obfs4 intégrés au Tor Expert Bundle (pt_config.json, section « bridges »).</summary>
    public static IReadOnlyList<BridgeLine> ParseBuiltIn(string ptConfigJson)
    {
        using var json = JsonDocument.Parse(ptConfigJson);
        if (!json.RootElement.TryGetProperty("bridges", out var bridges)
            || !bridges.TryGetProperty(Transport, out var lines)
            || lines.ValueKind != JsonValueKind.Array)
        {
            return [];
        }
        var result = new List<BridgeLine>();
        foreach (var line in lines.EnumerateArray())
        {
            if (TryParse(line.GetString(), out var bridge))
            {
                result.Add(bridge!);
            }
        }
        return result;
    }

    // Le préfixe « Bridge » est accepté : c'est ainsi que les lignes figurent dans un torrc.
    [GeneratedRegex(
        @"^\s*(?:Bridge\s+)?obfs4\s+(?<ip>[0-9]{1,3}(?:\.[0-9]{1,3}){3}):(?<port>[0-9]{1,5})\s+(?<fingerprint>[0-9A-Fa-f]{40})\s+cert=(?<cert>[A-Za-z0-9+/]{20,120}={0,2})\s+iat-mode=(?<iat>[0-2])\s*$",
        RegexOptions.CultureInvariant)]
    private static partial Regex Obfs4Pattern();
}
