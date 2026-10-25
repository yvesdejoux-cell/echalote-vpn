using Microsoft.Win32;
using EchaloteVpn.Core.Tunnel;

namespace EchaloteVpn.Service.Tunnel;

/// <summary>
/// tun2socks donne un nouveau GUID à la carte à chaque lancement, et Windows enregistre alors un
/// nouveau profil réseau (« EchaloteVpn 2 », « EchaloteVpn 3 »…). On retire ces profils une fois la carte
/// disparue, avec les signatures qui y renvoient.
/// </summary>
public static class NetworkProfileCleanup
{
    private const string NetworkListKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\NetworkList";

    /// <summary>Renvoie le nombre de profils retirés.</summary>
    public static int RemoveTunnelProfiles(string deviceName)
    {
        var removed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var profiles = Registry.LocalMachine.OpenSubKey($@"{NetworkListKey}\Profiles", writable: true))
        {
            if (profiles is null)
            {
                return 0;
            }
            foreach (var id in profiles.GetSubKeyNames())
            {
                using var profile = profiles.OpenSubKey(id);
                if (profile?.GetValue("ProfileName") is string name
                    && (NetworkProfileName.IsTunnelProfile(name, deviceName)
                        || TunnelOptions.LegacyDeviceNames.Any(legacy => NetworkProfileName.IsTunnelProfile(name, legacy))))
                {
                    removed.Add(id);
                }
            }
            foreach (var id in removed)
            {
                profiles.DeleteSubKeyTree(id, throwOnMissingSubKey: false);
            }
        }

        foreach (var kind in new[] { "Unmanaged", "Managed" })
        {
            using var signatures = Registry.LocalMachine.OpenSubKey($@"{NetworkListKey}\Signatures\{kind}", writable: true);
            if (signatures is null)
            {
                continue;
            }
            foreach (var signature in signatures.GetSubKeyNames())
            {
                using var entry = signatures.OpenSubKey(signature);
                if (entry?.GetValue("ProfileGuid") is string profileId && removed.Contains(profileId))
                {
                    signatures.DeleteSubKeyTree(signature, throwOnMissingSubKey: false);
                }
            }
        }
        return removed.Count;
    }
}
