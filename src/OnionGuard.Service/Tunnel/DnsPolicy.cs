using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace OnionGuard.Service.Tunnel;

/// <summary>
/// Règle NRPT « . » : tout le DNS de Windows part vers le DNSPort de Tor, et Windows n'interroge
/// plus en parallèle les serveurs des autres cartes (la box). Ajout et retrait par l'interface WMI
/// de Add-DnsClientNrptRule, pour que le service DNS de Windows soit prévenu tout de suite.
/// </summary>
public sealed partial class DnsPolicy(ILogger<DnsPolicy> logger)
{
    private const string ClassPath = @"root\Microsoft\Windows\DNS:PS_DnsClientNrptRule";
    private const string RuleComment = "OnionGuard";

    // Règles posées par les versions nommées TorVpn : retirées elles aussi.
    private const string LegacyRuleComment = "TorVpn";

    // Les méthodes WMI ne renvoient rien à System.Management (leurs résultats sont diffusés à part) :
    // les règles se lisent là où Windows les range, sous leur nom {GUID}.
    private const string RulesKey = @"SYSTEM\CurrentControlSet\Services\Dnscache\Parameters\DnsPolicyConfig";

    public void Apply(string dnsServer)
    {
        RemoveRules();

        using (var rules = new ManagementClass(ClassPath))
        using (var parameters = rules.GetMethodParameters("Add"))
        {
            parameters["Namespace"] = new[] { "." };
            parameters["NameServers"] = new[] { dnsServer };
            parameters["Comment"] = RuleComment;
            parameters["DisplayName"] = "OnionGuard : DNS par Tor";
            using var result = rules.InvokeMethod("Add", parameters, null);
            Check(result, "ajout de la règle NRPT");
        }
        if (FindRules().Count == 0)
        {
            throw new InvalidOperationException("La règle NRPT n'a pas été créée.");
        }

        FlushCache();
        logger.LogInformation("DNS de Windows envoyé à Tor ({Server}) par une règle NRPT.", dnsServer);
    }

    /// <summary>Retire les règles OnionGuard, y compris celles laissées par un plantage. Renvoie leur nombre.</summary>
    public int Remove()
    {
        var removed = RemoveRules();
        if (removed > 0)
        {
            FlushCache();
            logger.LogInformation("Règle NRPT retirée : DNS de Windows rétabli.");
        }
        return removed;
    }

    private static int RemoveRules()
    {
        var names = FindRules();
        if (names.Count == 0)
        {
            return 0;
        }

        using var rules = new ManagementClass(ClassPath);
        foreach (var name in names)
        {
            using var parameters = rules.GetMethodParameters("Remove");
            parameters["Name"] = name;
            parameters["Force"] = true;
            using var result = rules.InvokeMethod("Remove", parameters, null);
            Check(result, $"retrait de la règle NRPT {name}");
        }
        if (FindRules().Count > 0)
        {
            throw new InvalidOperationException("La règle NRPT OnionGuard n'a pas pu être retirée.");
        }
        return names.Count;
    }

    private static List<string> FindRules()
    {
        using var key = Registry.LocalMachine.OpenSubKey(RulesKey);
        if (key is null)
        {
            return [];
        }
        return key.GetSubKeyNames()
            .Where(name =>
            {
                using var rule = key.OpenSubKey(name);
                return rule?.GetValue("Comment") is RuleComment or LegacyRuleComment;
            })
            .ToList();
    }

    private static void Check(ManagementBaseObject? result, string operation)
    {
        if (result?["ReturnValue"] is { } value && Convert.ToUInt32(value) != 0)
        {
            throw new InvalidOperationException($"Échec : {operation} (code {value}).");
        }
    }

    // Les réponses déjà en cache viennent de la box : elles ne doivent pas resservir.
    private void FlushCache()
    {
        if (!DnsFlushResolverCache())
        {
            logger.LogWarning("Le cache DNS de Windows n'a pas pu être vidé.");
        }
    }

    [LibraryImport("dnsapi.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DnsFlushResolverCache();
}
