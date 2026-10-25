namespace EchaloteVpn.Core.Tor;

/// <summary>Emplacements utilisés par tor.exe.</summary>
/// <param name="TransportPlugin">lyrebird.exe, pour les ponts obfs4 ; sans espace (Tor coupe la commande aux espaces).</param>
public sealed record TorPaths(string DataDirectory, string GeoIpFile, string GeoIpv6File, string? TransportPlugin = null);
