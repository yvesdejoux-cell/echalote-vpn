using OnionGuard.Core.Tunnel;

namespace OnionGuard.Service.Tunnel;

/// <summary>Emplacement de tun2socks et réglages de la carte Wintun.</summary>
public sealed class TunnelOptions
{
    /// <summary>tun2socks.exe et wintun.dll, copiés côte à côte à la compilation.</summary>
    public string Directory { get; init; } = Path.Combine(AppContext.BaseDirectory, "tunnel");

    /// <summary>Nom de la carte Wintun, tel qu'il apparaît dans les connexions réseau de Windows.</summary>
    public string DeviceName { get; init; } = "OnionGuard";

    /// <summary>Nom de la carte avant le renommage (TorVpn) : ses profils réseau sont aussi retirés.</summary>
    public const string LegacyDeviceName = "TorVpn";

    /// <summary>Délai pour que la carte apparaisse après le lancement de tun2socks.</summary>
    public TimeSpan StartTimeout { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>Adresse de la carte et routes du tunnel.</summary>
    public TunnelRoutePlan RoutePlan { get; init; } = TunnelRoutePlan.Default;

    public string Tun2SocksExePath => Path.Combine(Directory, "tun2socks.exe");

    public string WintunDllPath => Path.Combine(Directory, "wintun.dll");
}
