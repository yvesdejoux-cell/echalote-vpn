using OnionGuard.Core.Tor;

namespace OnionGuard.Core.Configuration;

/// <summary>Ponts obfs4 : aucun, ceux intégrés au bundle de Tor, ou des lignes collées par l'utilisateur.</summary>
public enum BridgeMode
{
    None,
    BuiltIn,
    Custom,
}

/// <summary>
/// Réglages que l'utilisateur change depuis l'interface. Ils s'appliquent à la prochaine connexion.
/// </summary>
public sealed record UserSettings
{
    public const int MaxExitCountries = 50;

    public const int MaxCustomBridges = 20;

    /// <summary>Codes pays ISO à deux lettres, en majuscules (vide = au choix de Tor).</summary>
    public IReadOnlyList<string> ExitCountries { get; init; } = [];

    public bool KillSwitchEnabled { get; init; } = true;

    public bool AllowLocalNetwork { get; init; }

    public BridgeMode Bridges { get; init; } = BridgeMode.None;

    /// <summary>Lignes obfs4 de l'utilisateur, en forme canonique ; gardées même hors du mode Custom.</summary>
    public IReadOnlyList<string> CustomBridges { get; init; } = [];

    /// <summary>Codes pays et ponts vérifiés, en forme canonique et sans doublon.</summary>
    /// <exception cref="ArgumentException">Un code pays ou une ligne de pont est invalide, ou il y en a trop.</exception>
    public UserSettings Normalize()
    {
        var bridges = new List<string>();
        foreach (var line in (CustomBridges ?? []).Where(l => !string.IsNullOrWhiteSpace(l)))
        {
            if (!BridgeLine.TryParse(line, out var bridge))
            {
                throw new ArgumentException($"Ligne de pont invalide (obfs4 attendu) : {Shorten(line)}");
            }
            bridges.Add(bridge!.ToString());
        }
        bridges = bridges.Distinct().ToList();
        if (bridges.Count > MaxCustomBridges)
        {
            throw new ArgumentException($"Trop de ponts ({MaxCustomBridges} au plus).");
        }
        if (!Enum.IsDefined(Bridges))
        {
            throw new ArgumentException("Mode de ponts inconnu.");
        }
        if (Bridges == BridgeMode.Custom && bridges.Count == 0)
        {
            throw new ArgumentException("Ponts personnalisés choisis, mais aucune ligne de pont.");
        }

        var countries = (ExitCountries ?? []).Select(code => (code ?? string.Empty).Trim()).ToList();
        if (countries.Any(code => code.Length != 2 || !code.All(char.IsAsciiLetter)))
        {
            throw new ArgumentException("Code pays invalide : deux lettres attendues (par exemple DE).");
        }
        if (countries.Count > MaxExitCountries)
        {
            throw new ArgumentException($"Trop de pays de sortie ({MaxExitCountries} au plus).");
        }
        return this with
        {
            ExitCountries = countries.Select(code => code.ToUpperInvariant()).Distinct().ToList(),
            CustomBridges = bridges,
        };
    }

    /// <param name="builtInBridges">Ponts obfs4 du bundle de Tor, pour le mode BuiltIn.</param>
    /// <exception cref="InvalidOperationException">Ponts intégrés choisis, mais introuvables.</exception>
    public OnionGuardSettings ToOnionGuardSettings(IReadOnlyList<BridgeLine>? builtInBridges = null) => new()
    {
        ExitCountries = ExitCountries,
        KillSwitchEnabled = KillSwitchEnabled,
        AllowLocalNetwork = AllowLocalNetwork,
        Bridges = Bridges switch
        {
            BridgeMode.BuiltIn when builtInBridges is { Count: > 0 } => builtInBridges,
            BridgeMode.BuiltIn => throw new InvalidOperationException("Ponts intégrés introuvables dans le bundle de Tor."),
            BridgeMode.Custom => CustomBridges.Select(line => BridgeLine.TryParse(line, out var bridge) ? bridge! : throw new InvalidOperationException($"Ligne de pont invalide : {Shorten(line)}")).ToList(),
            _ => [],
        },
    };

    private static string Shorten(string line) => line.Length <= 40 ? line : line[..40] + "…";
}
