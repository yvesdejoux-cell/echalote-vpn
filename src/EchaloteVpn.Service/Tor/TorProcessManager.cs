using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using EchaloteVpn.Core.Configuration;
using EchaloteVpn.Core.Tor;

namespace EchaloteVpn.Service.Tor;

/// <summary>
/// Lance tor.exe, attend la fin du bootstrap et le relance s'il s'arrête.
/// </summary>
public sealed class TorProcessManager(TorOptions options, ConnectionStatus connection, ILogger<TorProcessManager> logger)
{
    private static readonly TimeSpan[] RestartDelays =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(15),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromSeconds(60),
    ];

    private static readonly TimeSpan ControlPortTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Fait tourner Tor jusqu'à l'arrêt demandé, en le relançant après chaque plantage.</summary>
    /// <param name="outboundBindAddress">
    /// Appelé avant chaque lancement de tor.exe : adresse de la carte physique pour OutboundBindAddress.
    /// </param>
    /// <param name="whileConnected">
    /// Appelé une fois le bootstrap à 100 %, et annulé dès que tor.exe s'arrête : le tunnel ne vit pas sans Tor.
    /// </param>
    public async Task RunAsync(
        EchaloteVpnSettings settings,
        CancellationToken stoppingToken,
        Func<IPAddress?>? outboundBindAddress = null,
        Func<TorControlClient, CancellationToken, Task>? whileConnected = null)
    {
        var failures = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (await RunOnceAsync(settings, outboundBindAddress?.Invoke(), whileConnected, stoppingToken))
                {
                    failures = 0;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erreur du moteur Tor.");
                connection.ReportError($"Tor : {ex.Message}");
            }

            var delay = RestartDelays[Math.Min(failures++, RestartDelays.Length - 1)];
            logger.LogWarning("Redémarrage de tor.exe dans {Delay} s.", delay.TotalSeconds);
            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>Une vie de tor.exe. Renvoie vrai si le bootstrap a atteint 100 %.</summary>
    private async Task<bool> RunOnceAsync(
        EchaloteVpnSettings settings,
        IPAddress? outboundBindAddress,
        Func<TorControlClient, CancellationToken, Task>? whileConnected,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(options.TorExePath))
        {
            throw new FileNotFoundException(
                "tor.exe introuvable : lancer scripts/fetch-deps.ps1 avant la compilation.", options.TorExePath);
        }

        Directory.CreateDirectory(options.DataDirectory);
        var torrcPath = Path.Combine(options.DataDirectory, "torrc");
        var paths = new TorPaths(
            options.DataDirectory,
            options.GeoIpFile,
            options.GeoIpv6File,
            File.Exists(options.LyrebirdPath) ? ShortPath.Of(options.LyrebirdPath) : null);
        await File.WriteAllTextAsync(torrcPath, TorrcBuilder.Build(settings, paths, Environment.ProcessId, outboundBindAddress), cancellationToken);

        // Un ancien cookie ferait échouer l'authentification : on attend celui que Tor va écrire.
        var cookiePath = Path.Combine(options.DataDirectory, "control_auth_cookie");
        File.Delete(cookiePath);

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(options.TorExePath)
            {
                ArgumentList = { "-f", torrcPath },
                WorkingDirectory = Path.GetDirectoryName(options.TorExePath)!,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
        };
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                logger.LogInformation("[tor] {Line}", e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                logger.LogWarning("[tor] {Line}", e.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        logger.LogInformation("tor.exe démarré (PID {Pid}).", process.Id);

        var exited = process.WaitForExitAsync(CancellationToken.None);
        TorControlClient? control = null;
        try
        {
            control = await ConnectControlAsync(settings.ControlPort, cookiePath, exited, cancellationToken);
            await control.TakeOwnershipAsync(cancellationToken);

            var bootstrapped = await WaitForBootstrapAsync(control, exited, cancellationToken);
            if (bootstrapped)
            {
                logger.LogInformation("Tor est connecté. SOCKS sur 127.0.0.1:{Port}.", settings.SocksPort);
                if (whileConnected is not null)
                {
                    await RunWhileConnectedAsync(whileConnected, control, exited, cancellationToken);
                }
            }

            await exited.WaitAsync(cancellationToken);
            logger.LogWarning("tor.exe s'est arrêté (code {Code}).", process.ExitCode);
            return bootstrapped;
        }
        finally
        {
            await StopAsync(process, control, exited);
            control?.Dispose();
        }
    }

    /// <summary>Fait tourner <paramref name="whileConnected"/> jusqu'à l'arrêt de tor.exe ou du service.</summary>
    private async Task RunWhileConnectedAsync(
        Func<TorControlClient, CancellationToken, Task> whileConnected,
        TorControlClient control,
        Task exited,
        CancellationToken cancellationToken)
    {
        using var torAlive = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var running = whileConnected(control, torAlive.Token);
        await Task.WhenAny(exited, running);
        await torAlive.CancelAsync();
        try
        {
            await running;
        }
        catch (OperationCanceledException) when (torAlive.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur pendant que Tor était connecté.");
        }
    }

    private static async Task<TorControlClient> ConnectControlAsync(
        int port, string cookiePath, Task exited, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + ControlPortTimeout;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (exited.IsCompleted)
            {
                throw new InvalidOperationException("tor.exe s'est arrêté pendant le démarrage.");
            }

            if (File.Exists(cookiePath))
            {
                TorControlClient? client = null;
                try
                {
                    client = await TorControlClient.ConnectAsync(port, cancellationToken);
                    await client.AuthenticateAsync(await File.ReadAllBytesAsync(cookiePath, cancellationToken), cancellationToken);
                    return client;
                }
                catch (Exception ex) when (ex is SocketException or IOException or TorControlException)
                {
                    client?.Dispose();
                }
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Le port de contrôle de Tor ne répond pas.");
            }
            await Task.Delay(250, cancellationToken);
        }
    }

    private async Task<bool> WaitForBootstrapAsync(TorControlClient control, Task exited, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.BootstrapTimeout);

        var lastProgress = -1;
        try
        {
            while (!exited.IsCompleted)
            {
                var status = await control.GetBootstrapStatusAsync(timeout.Token);
                if (status.Progress != lastProgress)
                {
                    lastProgress = status.Progress;
                    logger.LogInformation("Bootstrap Tor : {Progress} % ({Summary})", status.Progress, status.Summary);
                    connection.ReportBootstrap(status.Progress);
                }
                if (status.IsDone)
                {
                    return true;
                }
                await Task.Delay(500, timeout.Token);
            }
            return false;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Bootstrap de Tor incomplet après {options.BootstrapTimeout.TotalMinutes} min.");
        }
    }

    private async Task StopAsync(Process process, TorControlClient? control, Task exited)
    {
        if (exited.IsCompleted)
        {
            return;
        }

        try
        {
            if (control is not null)
            {
                using var timeout = new CancellationTokenSource(StopTimeout);
                await control.ShutdownAsync(timeout.Token);
                await exited.WaitAsync(timeout.Token);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or TorControlException or IOException)
        {
            logger.LogDebug(ex, "Arrêt propre de tor.exe impossible.");
        }

        if (!process.HasExited)
        {
            logger.LogWarning("tor.exe ne s'arrête pas, arrêt forcé.");
            process.Kill(entireProcessTree: true);
        }
        logger.LogInformation("tor.exe arrêté.");
    }
}
