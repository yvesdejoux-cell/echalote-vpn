using EchaloteVpn.Core.Configuration;

namespace EchaloteVpn.Core.Ipc;

public enum MainAction
{
    None,
    Connect,
    Disconnect,
}

/// <summary>Ce que la fenêtre affiche pour un état du service.</summary>
public sealed record StatusView(
    string Title,
    string Detail,
    string ButtonLabel,
    MainAction Action,
    bool ShowProgress = false,
    int Progress = 0);

public static class StatusPresenter
{
    /// <param name="status">null quand le service ne répond pas.</param>
    public static StatusView Present(ServiceStatus? status)
    {
        if (status is null)
        {
            return new(
                "Service absent",
                "Le service Échalote VPN ne répond pas : redémarrer le PC ou réinstaller Échalote VPN (en développement : le lancer en administrateur).",
                "Connecter",
                MainAction.None);
        }

        return status.State switch
        {
            ConnectionState.Disconnected => new(
                "Déconnecté",
                status.LastError ?? "Le trafic ne passe pas par Tor.",
                "Connecter",
                MainAction.Connect),
            ConnectionState.Connecting => new(
                "Connexion…",
                status.LastError ?? (status.BootstrapPercent < 100
                    ? $"Démarrage de Tor : {status.BootstrapPercent} %"
                    : "Mise en place du tunnel…"),
                "Déconnecter",
                MainAction.Disconnect,
                ShowProgress: true,
                Progress: status.BootstrapPercent),
            ConnectionState.Connected => new(
                "Connecté via Tor",
                status.ExitCountry is null ? "Tout le trafic passe par Tor." : $"Sortie : {CountryName(status.ExitCountry)} (dernier circuit)",
                "Déconnecter",
                MainAction.Disconnect),
            ConnectionState.Disconnecting => new(
                "Déconnexion…",
                "Arrêt du tunnel et de Tor.",
                "Déconnecter",
                MainAction.None),
            _ => new(
                "Erreur",
                status.LastError ?? "Erreur inconnue.",
                "Réessayer",
                MainAction.Connect),
        };
    }

    /// <summary>« Allemagne (DE) » pour les pays connus, le code seul sinon.</summary>
    public static string CountryName(string code)
    {
        var upper = code.ToUpperInvariant();
        return ExitCountries.Names.TryGetValue(upper, out var name) ? $"{name} ({upper})" : upper;
    }
}
