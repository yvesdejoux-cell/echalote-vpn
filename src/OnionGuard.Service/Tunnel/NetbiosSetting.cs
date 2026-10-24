using System.Management;
using System.Net.NetworkInformation;

namespace OnionGuard.Service.Tunnel;

/// <summary>
/// Désactive NetBIOS sur TCP/IP pour la carte du tunnel, comme la case des propriétés de la carte.
/// Sinon Windows y envoie des diffusions UDP 137, que Tor ne peut pas transporter.
/// </summary>
public static class NetbiosSetting
{
    private const uint NetbiosDisabled = 2;

    public static void Disable(string deviceName, ILogger logger)
    {
        try
        {
            var adapter = NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(n => string.Equals(n.Name, deviceName, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"carte {deviceName} introuvable");
            if (!Guid.TryParse(adapter.Id, out var guid))
            {
                throw new InvalidOperationException($"identifiant de carte inattendu : {adapter.Id}");
            }

            using var searcher = new ManagementObjectSearcher(
                $"SELECT * FROM Win32_NetworkAdapterConfiguration WHERE SettingID = '{guid:B}'");
            using var results = searcher.Get();
            var configuration = results.Cast<ManagementObject>().FirstOrDefault()
                ?? throw new InvalidOperationException($"configuration WMI de {deviceName} introuvable");
            using (configuration)
            {
                var status = Convert.ToUInt32(configuration.InvokeMethod("SetTcpipNetbios", [NetbiosDisabled]));
                if (status is not (0 or 1))
                {
                    throw new InvalidOperationException($"SetTcpipNetbios a renvoyé {status}");
                }
            }
            logger.LogInformation("NetBIOS désactivé sur la carte {Device}.", deviceName);
        }
        catch (Exception ex) when (ex is ManagementException or InvalidOperationException or UnauthorizedAccessException)
        {
            // Sans conséquence sur le tunnel : seulement des diffusions UDP refusées.
            logger.LogWarning("NetBIOS non désactivé sur la carte {Device} : {Message}", deviceName, ex.Message);
        }
    }
}
