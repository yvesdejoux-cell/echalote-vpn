using System.Diagnostics;

namespace EchaloteVpn.App;

/// <summary>
/// Lien « Soutenir le projet » (Ko-fi, dons ponctuels seulement). Masqué tant que le pseudo
/// Ko-fi n'est pas renseigné. Rien n'est envoyé par l'application : le lien s'ouvre dans le
/// navigateur, et c'est tout.
/// </summary>
public static class Donation
{
    /// <summary>Pseudo de la page https://ko-fi.com/&lt;pseudo&gt; ; vide = lien masqué.</summary>
    public const string KofiUser = "ptidej";

    public static bool IsAvailable => KofiUser.Length > 0;

    public static string Url => $"https://ko-fi.com/{KofiUser}";

    public static void Open()
    {
        if (!IsAvailable)
        {
            return;
        }
        Process.Start(new ProcessStartInfo(Url) { UseShellExecute = true });
    }
}
