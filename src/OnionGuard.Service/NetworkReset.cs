using System.Security.Principal;
using OnionGuard.Service.Firewall;
using OnionGuard.Service.Tunnel;

namespace OnionGuard.Service;

/// <summary>
/// « OnionGuard.Service.exe --reset-network » : rend le réseau normal après un plantage, en retirant
/// les filtres du kill switch, la règle NRPT et les profils réseau laissés par la carte OnionGuard.
/// La carte et ses routes disparaissent d'elles-mêmes avec tun2socks.
/// </summary>
public static class NetworkReset
{
    public const string Argument = "--reset-network";

    public static int Run()
    {
        if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
        {
            Console.Error.WriteLine("--reset-network demande les droits administrateur.");
            return 1;
        }

        var failed = false;
        try
        {
            var filters = KillSwitch.Disable();
            Console.WriteLine(filters > 0 ? $"Kill switch retiré ({filters} filtres WFP)." : "Aucun filtre WFP OnionGuard.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or PlatformNotSupportedException)
        {
            Console.Error.WriteLine($"Kill switch : {ex.Message}");
            failed = true;
        }

        try
        {
            var rules = new DnsPolicy(Microsoft.Extensions.Logging.Abstractions.NullLogger<DnsPolicy>.Instance).Remove();
            Console.WriteLine(rules > 0 ? "Règle NRPT OnionGuard retirée, cache DNS vidé." : "Aucune règle NRPT OnionGuard.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Management.ManagementException)
        {
            Console.Error.WriteLine($"Règle NRPT : {ex.Message}");
            failed = true;
        }

        try
        {
            var profiles = NetworkProfileCleanup.RemoveTunnelProfiles(new TunnelOptions().DeviceName);
            Console.WriteLine(profiles > 0 ? $"{profiles} profil(s) réseau OnionGuard retiré(s)." : "Aucun profil réseau OnionGuard.");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            Console.Error.WriteLine($"Profils réseau : {ex.Message}");
            failed = true;
        }

        Console.WriteLine(failed ? "Réseau partiellement rétabli." : "Réseau rétabli.");
        return failed ? 1 : 0;
    }
}
