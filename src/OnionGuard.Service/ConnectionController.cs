using OnionGuard.Core.Configuration;
using OnionGuard.Core.Ipc;
using OnionGuard.Core.Tor;
using OnionGuard.Service.Firewall;
using OnionGuard.Service.Tor;
using OnionGuard.Service.Tunnel;

namespace OnionGuard.Service;

/// <summary>
/// Connexion pilotée par l'interface. Connecter : kill switch, puis Tor, puis le tunnel.
/// Déconnecter : le tunnel et Tor s'arrêtent, puis le kill switch est retiré en dernier.
/// Les commandes passent une par une. Au démarrage du service, une connexion voulue reprend
/// (après un plantage ou un redémarrage) ; sinon les restes d'un plantage sont nettoyés.
/// </summary>
public sealed class ConnectionController(
    TorProcessManager tor,
    TorOptions torOptions,
    TunnelSession tunnel,
    KillSwitch killSwitch,
    DnsPolicy dns,
    TunnelOptions tunnelOptions,
    StateStore store,
    ConnectionStatus status,
    ILogger<ConnectionController> logger) : IHostedService
{
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ExitProbeInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ExitProbeRetry = TimeSpan.FromSeconds(3);

    private readonly SemaphoreSlim commands = new(1, 1);
    private PersistedState persisted = PersistedState.Default;
    private CancellationTokenSource? connection;
    private Task running = Task.CompletedTask;
    private TorControlClient? control;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        persisted = store.Load();
        if (persisted.ConnectionWanted)
        {
            // Les filtres persistants du kill switch ont bloqué le trafic en attendant.
            logger.LogInformation("Service OnionGuard démarré : reprise de la connexion voulue.");
            await ConnectAsync();
            return;
        }
        CleanUpLeftovers();
        logger.LogInformation("Service OnionGuard démarré, en attente de l'interface.");
    }

    /// <summary>Ce qu'un plantage pendant une connexion a pu laisser, comme « --reset-network ».</summary>
    private void CleanUpLeftovers()
    {
        try
        {
            var filters = KillSwitch.Disable();
            var rules = dns.Remove();
            var profiles = NetworkProfileCleanup.RemoveTunnelProfiles(tunnelOptions.DeviceName);
            if (filters + rules + profiles > 0)
            {
                logger.LogWarning(
                    "Restes d'une connexion précédente retirés : {Filters} filtres WFP, {Rules} règle(s) NRPT, {Profiles} profil(s) réseau.",
                    filters, rules, profiles);
            }
        }
        catch (Exception ex)
        {
            logger.LogError("Nettoyage au démarrage incomplet : {Message} Lancer « OnionGuard.Service.exe --reset-network ».", ex.Message);
        }
    }

    /// <summary>Arrêt du service : la connexion s'arrête, mais reste voulue pour la reprise.</summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await commands.WaitAsync(cancellationToken);
        try
        {
            await StopConnectionAsync();
        }
        finally
        {
            commands.Release();
        }
        logger.LogInformation("Service OnionGuard arrêté.");
    }

    public ServiceStatus Status => status.Snapshot();

    public UserSettings Settings => persisted.Settings;

    public async Task<string?> ConnectAsync()
    {
        await commands.WaitAsync();
        try
        {
            if (connection is not null)
            {
                return null;
            }
            Persist(persisted with { ConnectionWanted = true });
            status.SetState(ConnectionState.Connecting);

            OnionGuardSettings settings;
            try
            {
                settings = persisted.Settings.ToOnionGuardSettings(LoadBuiltInBridges());
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or System.Text.Json.JsonException)
            {
                logger.LogError("Réglages inutilisables : {Message}", ex.Message);
                status.SetState(ConnectionState.Error, ex.Message);
                return ex.Message;
            }

            if (settings.KillSwitchEnabled)
            {
                try
                {
                    killSwitch.Enable(settings);
                }
                catch (Exception ex) when (ex is InvalidOperationException or FileNotFoundException or PlatformNotSupportedException)
                {
                    // Sans kill switch, Tor ne démarre pas : rien ne doit sortir sans protection.
                    logger.LogError("Kill switch impossible, Tor ne démarre pas : {Message}", ex.Message);
                    status.SetState(ConnectionState.Error, $"Kill switch impossible : {ex.Message}");
                    return status.Snapshot().LastError;
                }
            }

            connection = new CancellationTokenSource();
            running = RunAsync(settings, connection.Token);
            logger.LogInformation("Connexion demandée.");
            return null;
        }
        finally
        {
            commands.Release();
        }
    }

    public async Task DisconnectAsync()
    {
        await commands.WaitAsync();
        try
        {
            Persist(persisted with { ConnectionWanted = false });
            await StopConnectionAsync();
            logger.LogInformation("Déconnecté.");
        }
        finally
        {
            commands.Release();
        }
    }

    public async Task<string?> NewIdentityAsync()
    {
        var current = control;
        if (current is null || status.State != ConnectionState.Connected)
        {
            return "Pas connecté.";
        }
        try
        {
            await current.NewIdentityAsync();
            // L'ancien pays ne vaut plus : le prochain relevé donnera celui des nouveaux circuits.
            status.ReportExitCountry(null);
            return null;
        }
        catch (Exception ex) when (ex is TorControlException or IOException or ObjectDisposedException)
        {
            return $"Nouvelle identité impossible : {ex.Message}";
        }
    }

    /// <summary>Les réglages s'appliquent à la prochaine connexion.</summary>
    public async Task<string?> SetSettingsAsync(UserSettings settings)
    {
        UserSettings normalized;
        try
        {
            normalized = settings.Normalize();
        }
        catch (ArgumentException ex)
        {
            return ex.Message;
        }

        await commands.WaitAsync();
        try
        {
            Persist(persisted with { Settings = normalized });
            logger.LogInformation(
                "Réglages : pays de sortie [{Countries}], kill switch {KillSwitch}, réseau local {Lan}.",
                string.Join(',', normalized.ExitCountries), normalized.KillSwitchEnabled, normalized.AllowLocalNetwork);
            return null;
        }
        finally
        {
            commands.Release();
        }
    }

    private async Task RunAsync(OnionGuardSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            await tor.RunAsync(
                settings,
                cancellationToken,
                tunnel.PrepareOutboundAddress,
                (client, token) =>
                {
                    control = client;
                    return Task.WhenAll(tunnel.RunAsync(settings, client, token), ProbeExitCountryAsync(client, token));
                });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur de la connexion.");
            status.ReportError(ex.Message);
        }
        finally
        {
            control = null;
        }
    }

    /// <summary>
    /// Relève le pays du relais de sortie du dernier circuit, par la base géographique de Tor :
    /// aucune requête ne part sur Internet pour cela.
    /// </summary>
    private async Task ProbeExitCountryAsync(TorControlClient client, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // Plus souvent tant que le pays est inconnu : juste après la connexion ou une nouvelle identité.
                await Task.Delay(status.Snapshot().ExitCountry is null ? ExitProbeRetry : ExitProbeInterval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            if (status.State != ConnectionState.Connected)
            {
                continue;
            }
            try
            {
                var exit = TorCircuits.FindExitFingerprint(await client.GetInfoAsync("circuit-status", cancellationToken));
                if (exit is null)
                {
                    continue;
                }
                var address = TorCircuits.FindRouterAddress(await client.GetInfoAsync($"ns/id/{exit}", cancellationToken));
                if (address is null)
                {
                    continue;
                }
                var country = (await client.GetInfoAsync($"ip-to-country/{address}", cancellationToken)).FirstOrDefault();
                status.ReportExitCountry(country is null ? null : TorCircuits.ParseCountry(country));
            }
            catch (Exception ex) when (ex is TorControlException or IOException or ObjectDisposedException)
            {
                logger.LogDebug("Pays de sortie inconnu : {Message}", ex.Message);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>Ponts obfs4 intégrés au bundle de Tor, lus seulement s'ils sont demandés.</summary>
    private IReadOnlyList<BridgeLine> LoadBuiltInBridges() =>
        persisted.Settings.Bridges == BridgeMode.BuiltIn
            ? BridgeLine.ParseBuiltIn(File.ReadAllText(torOptions.PtConfigPath))
            : [];

    /// <summary>Appelé sous le verrou des commandes.</summary>
    private async Task StopConnectionAsync()
    {
        var wasActive = connection is not null || status.State == ConnectionState.Error;
        if (connection is not null)
        {
            status.SetState(ConnectionState.Disconnecting);
            await connection.CancelAsync();
            if (await Task.WhenAny(running, Task.Delay(StopTimeout)) != running)
            {
                logger.LogError("La connexion ne s'arrête pas après {Seconds} s.", StopTimeout.TotalSeconds);
            }
            connection.Dispose();
            connection = null;
        }
        if (wasActive)
        {
            // En dernier : le tunnel et Tor sont arrêtés, rien ne fuit pendant le démontage.
            try
            {
                KillSwitch.Disable();
            }
            catch (InvalidOperationException ex)
            {
                logger.LogError("Kill switch non retiré : {Message} Lancer « OnionGuard.Service.exe --reset-network ».", ex.Message);
            }
        }
        status.SetState(ConnectionState.Disconnected);
    }

    private void Persist(PersistedState state)
    {
        persisted = state;
        try
        {
            store.Save(state);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // Sans droits administrateur, le fichier ne peut pas être protégé : il n'est pas écrit.
            logger.LogWarning("Réglages non enregistrés : {Message}", ex.Message);
        }
    }
}
