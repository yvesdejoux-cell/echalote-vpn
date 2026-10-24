using System.Net;
using System.Net.NetworkInformation;
using OnionGuard.Core.Configuration;
using OnionGuard.Core.Tor;

namespace OnionGuard.Service.Tunnel;

/// <summary>
/// Fait vivre le tunnel pendant que Tor est connecté : tun2socks, puis les routes ; suit les
/// changements de réseau pour que tor.exe sorte toujours par la carte physique.
/// </summary>
public sealed class TunnelSession(
    TunnelOptions options,
    Tun2SocksProcess tun2socks,
    RouteManager routes,
    DnsPolicy dns,
    ConnectionStatus connection,
    ILogger<TunnelSession> logger)
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan NetworkSettleDelay = TimeSpan.FromSeconds(2);

    private PhysicalInterface? physical;
    private TaskCompletionSource networkChanged = NewSignal();

    /// <summary>
    /// Avant chaque lancement de tor.exe, routes du tunnel absentes : adresse de la carte physique.
    /// </summary>
    public IPAddress PrepareOutboundAddress()
    {
        physical = PhysicalInterface.Find();
        logger.LogInformation("Carte physique : index {Index}, adresse {Address}.", physical.InterfaceIndex, physical.SourceAddress);
        return physical.SourceAddress;
    }

    /// <summary>Jusqu'à l'annulation (arrêt du service ou de Tor), en relançant le tunnel après chaque échec.</summary>
    public async Task RunAsync(OnionGuardSettings settings, TorControlClient control, CancellationToken cancellationToken)
    {
        NetworkChange.NetworkAddressChanged += OnNetworkChanged;
        NetworkChange.NetworkAvailabilityChanged += OnNetworkChanged;
        try
        {
            while (true)
            {
                try
                {
                    await RunOnceAsync(settings, control, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                {
                    logger.LogError(ex, "Tunnel indisponible, nouvel essai dans {Delay} s.", RetryDelay.TotalSeconds);
                    connection.ReportError($"Tunnel : {ex.Message}");
                }
                await Task.Delay(RetryDelay, cancellationToken);
            }
        }
        finally
        {
            NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
            NetworkChange.NetworkAvailabilityChanged -= OnNetworkChanged;
        }
    }

    private async Task RunOnceAsync(OnionGuardSettings settings, TorControlClient control, CancellationToken cancellationToken)
    {
        await tun2socks.StartAsync(settings.SocksPort, cancellationToken);
        try
        {
            NetbiosSetting.Disable(options.DeviceName, logger);

            // Le réseau a pu changer depuis le lancement de Tor.
            await RefreshPhysicalAsync(control, cancellationToken);
            var bridges = settings.Bridges.Select(bridge => bridge.Endpoint.Address).Distinct().ToList();
            routes.Apply(physical!, bridges);
            dns.Apply(settings.DnsAddress);
            logger.LogInformation("Tunnel actif : le trafic IPv4 et le DNS passent par Tor.");
            connection.ReportTunnel(true);

            while (true)
            {
                var signal = Volatile.Read(ref networkChanged).Task;
                await Task.WhenAny(tun2socks.Exited, signal.WaitAsync(cancellationToken));
                cancellationToken.ThrowIfCancellationRequested();
                if (tun2socks.Exited.IsCompleted)
                {
                    throw new InvalidOperationException("tun2socks s'est arrêté.");
                }

                // Les événements arrivent en rafale ; nos propres routes en déclenchent aussi.
                await Task.Delay(NetworkSettleDelay, cancellationToken);
                Volatile.Write(ref networkChanged, NewSignal());
                if (physical?.IsStillCurrent() == true)
                {
                    continue;
                }

                // Le temps de retrouver la carte physique, le trafic sort sans Tor (kill switch à l'étape 4).
                logger.LogWarning("Réseau changé : routes du tunnel retirées le temps de retrouver la carte physique.");
                connection.ReportTunnel(false);
                routes.Remove();
                await RefreshPhysicalAsync(control, cancellationToken);
                routes.Apply(physical!, bridges);
                logger.LogInformation("Tunnel de nouveau actif.");
                connection.ReportTunnel(true);
            }
        }
        finally
        {
            connection.ReportTunnel(false);
            try
            {
                dns.Remove();
            }
            catch (Exception ex)
            {
                // Le nettoyage continue, et l'erreur d'origine du tunnel n'est pas masquée.
                logger.LogError(ex, "Règle NRPT non retirée : le DNS de Windows reste envoyé à Tor.");
            }
            routes.Remove();
            await tun2socks.StopAsync();
            RemoveNetworkProfiles();
        }
    }

    /// <summary>Routes du tunnel absentes : retrouve la carte physique et l'indique à Tor si elle a changé.</summary>
    private async Task RefreshPhysicalAsync(TorControlClient control, CancellationToken cancellationToken)
    {
        if (routes.IsApplied)
        {
            throw new InvalidOperationException("Recherche de la carte physique avec les routes du tunnel en place.");
        }

        var previous = physical;
        physical = PhysicalInterface.Find();
        if (!physical.SourceAddress.Equals(previous?.SourceAddress))
        {
            await control.SetOutboundBindAddressAsync(physical.SourceAddress, cancellationToken);
            logger.LogInformation("Tor sort désormais par {Address} (index {Index}).", physical.SourceAddress, physical.InterfaceIndex);
        }
    }

    // La carte vient de disparaître avec tun2socks : son profil réseau ne resservira jamais.
    private void RemoveNetworkProfiles()
    {
        try
        {
            var removed = NetworkProfileCleanup.RemoveTunnelProfiles(options.DeviceName);
            if (removed > 0)
            {
                logger.LogDebug("{Count} profil(s) réseau {Device} retiré(s).", removed, options.DeviceName);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            logger.LogWarning("Profils réseau {Device} non retirés : {Message}", options.DeviceName, ex.Message);
        }
    }

    private void OnNetworkChanged(object? sender, EventArgs e) => Volatile.Read(ref networkChanged).TrySetResult();

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
