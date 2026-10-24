namespace OnionGuard.Service.Tor;

/// <summary>Emplacements de tor.exe et de ses données.</summary>
public sealed class TorOptions
{
    /// <summary>Contenu du Tor Expert Bundle, copié à côté du service à la compilation.</summary>
    public string BundleDirectory { get; init; } = Path.Combine(AppContext.BaseDirectory, "tor-bundle");

    public string DataDirectory { get; init; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "OnionGuard", "tor");

    public TimeSpan BootstrapTimeout { get; init; } = TimeSpan.FromMinutes(3);

    public string TorExePath => Path.Combine(BundleDirectory, "tor", "tor.exe");

    public string GeoIpFile => Path.Combine(BundleDirectory, "data", "geoip");

    public string GeoIpv6File => Path.Combine(BundleDirectory, "data", "geoip6");

    /// <summary>Transport des ponts obfs4.</summary>
    public string LyrebirdPath => Path.Combine(BundleDirectory, "tor", "pluggable_transports", "lyrebird.exe");

    /// <summary>Liste des transports et des ponts intégrés au bundle.</summary>
    public string PtConfigPath => Path.Combine(BundleDirectory, "tor", "pluggable_transports", "pt_config.json");
}
