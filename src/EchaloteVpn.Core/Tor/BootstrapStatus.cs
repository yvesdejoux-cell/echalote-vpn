using System.Globalization;
using System.Text.RegularExpressions;

namespace EchaloteVpn.Core.Tor;

/// <summary>Avancement de la connexion de Tor au réseau (100 % = prêt).</summary>
public sealed record BootstrapStatus(int Progress, string Tag, string Summary)
{
    private static readonly Regex Pair = new("""(\w+)=("(?:[^"\\]|\\.)*"|\S+)""", RegexOptions.CultureInvariant);

    public bool IsDone => Progress >= 100;

    /// <summary>Lit une valeur comme <c>NOTICE BOOTSTRAP PROGRESS=100 TAG=done SUMMARY="Done"</c>.</summary>
    public static BootstrapStatus Parse(string phase)
    {
        var progress = 0;
        var tag = string.Empty;
        var summary = string.Empty;

        foreach (Match match in Pair.Matches(phase))
        {
            var value = match.Groups[2].Value.Trim('"');
            switch (match.Groups[1].Value)
            {
                case "PROGRESS":
                    _ = int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out progress);
                    break;
                case "TAG":
                    tag = value;
                    break;
                case "SUMMARY":
                    summary = value;
                    break;
            }
        }

        return new BootstrapStatus(progress, tag, summary);
    }
}
