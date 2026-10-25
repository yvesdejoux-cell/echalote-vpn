namespace EchaloteVpn.Core.Configuration;

/// <summary>
/// Pays proposés dans l'interface : ceux qui comptent le plus de relais de sortie Tor, avec leur
/// nom français (RegionInfo donne des noms dans la langue de chaque pays).
/// </summary>
public static class ExitCountries
{
    public static readonly IReadOnlyDictionary<string, string> Names = new Dictionary<string, string>
    {
        ["DE"] = "Allemagne",
        ["NL"] = "Pays-Bas",
        ["US"] = "États-Unis",
        ["FR"] = "France",
        ["CH"] = "Suisse",
        ["SE"] = "Suède",
        ["FI"] = "Finlande",
        ["NO"] = "Norvège",
        ["AT"] = "Autriche",
        ["LU"] = "Luxembourg",
        ["IS"] = "Islande",
        ["RO"] = "Roumanie",
        ["PL"] = "Pologne",
        ["CZ"] = "Tchéquie",
        ["GB"] = "Royaume-Uni",
        ["CA"] = "Canada",
        ["DK"] = "Danemark",
        ["BE"] = "Belgique",
        ["ES"] = "Espagne",
        ["IT"] = "Italie",
        ["BG"] = "Bulgarie",
        ["MD"] = "Moldavie",
        ["SG"] = "Singapour",
        ["JP"] = "Japon",
        ["HK"] = "Hong Kong",
    };

    public static IEnumerable<string> Codes => Names.Keys;
}
