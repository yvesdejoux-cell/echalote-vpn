using System.Diagnostics;
using System.Net.NetworkInformation;
using OnionGuard.Core.Tunnel;

namespace OnionGuard.Service.Tunnel;

/// <summary>
/// Lance tun2socks, qui crée la carte Wintun et renvoie son trafic vers le SOCKS de Tor.
/// Pas de redémarrage ici : il dépend de Tor et des routes, géré par l'orchestration.
/// </summary>
public sealed class Tun2SocksProcess(TunnelOptions options, ILogger<Tun2SocksProcess> logger) : IAsyncDisposable
{
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);

    private Process? process;
    private KillOnCloseJob? job;
    private int udpRefusedReported;

    /// <summary>Se termine si tun2socks s'arrête : les routes doivent alors être retirées.</summary>
    public Task Exited { get; private set; } = Task.CompletedTask;

    /// <summary>Lance tun2socks et attend que la carte Wintun soit active.</summary>
    public async Task StartAsync(int socksPort, CancellationToken cancellationToken)
    {
        if (process is not null)
        {
            throw new InvalidOperationException("tun2socks est déjà lancé.");
        }
        foreach (var file in new[] { options.Tun2SocksExePath, options.WintunDllPath })
        {
            if (!File.Exists(file))
            {
                throw new FileNotFoundException(
                    $"{Path.GetFileName(file)} introuvable : lancer scripts/fetch-deps.ps1 avant la compilation.", file);
            }
        }

        var startInfo = new ProcessStartInfo(options.Tun2SocksExePath)
        {
            WorkingDirectory = options.Directory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in Tun2SocksArguments.Build(options.DeviceName, socksPort))
        {
            startInfo.ArgumentList.Add(argument);
        }

        process = new Process { StartInfo = startInfo };
        // tun2socks écrit son journal ordinaire sur la sortie d'erreur : le niveau vient de chaque ligne.
        process.OutputDataReceived += (_, e) => Log(e.Data);
        process.ErrorDataReceived += (_, e) => Log(e.Data);

        job = new KillOnCloseJob();
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        Exited = process.WaitForExitAsync(CancellationToken.None);
        logger.LogInformation("tun2socks démarré (PID {Pid}).", process.Id);

        try
        {
            job.Assign(process);
            await WaitForDeviceAsync(cancellationToken);
            logger.LogInformation("Carte {Device} active.", options.DeviceName);
        }
        catch
        {
            await StopAsync();
            throw;
        }
    }

    private async Task WaitForDeviceAsync(CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + options.StartTimeout;
        while (!IsDeviceUp())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Exited.IsCompleted)
            {
                throw new InvalidOperationException(
                    $"tun2socks s'est arrêté au démarrage (code {process!.ExitCode}). " +
                    "Créer une carte Wintun demande les droits administrateur.");
            }
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"La carte {options.DeviceName} n'est pas apparue.");
            }
            await Task.Delay(250, cancellationToken);
        }
    }

    private void Log(string? data)
    {
        if (data is null)
        {
            return;
        }

        var line = Tun2SocksLogLine.Parse(data);
        if (line.IsUdpRefused && Interlocked.Exchange(ref udpRefusedReported, 1) == 0)
        {
            logger.LogInformation("L'UDP ne passe pas par Tor : paquets ignorés (blocage prévu à l'étape 4).");
        }
        var level = line.Level switch
        {
            Tun2SocksLogLevel.Debug => LogLevel.Debug,
            Tun2SocksLogLevel.Warning => LogLevel.Warning,
            Tun2SocksLogLevel.Error => LogLevel.Error,
            _ => LogLevel.Information,
        };
        logger.Log(level, "[tun2socks] {Line}", line.Message);
    }

    private bool IsDeviceUp() =>
        NetworkInterface.GetAllNetworkInterfaces().Any(n =>
            string.Equals(n.Name, options.DeviceName, StringComparison.OrdinalIgnoreCase)
            && n.OperationalStatus == OperationalStatus.Up);

    /// <summary>
    /// Arrête tun2socks. Il n'a pas d'arrêt propre sous Windows ; Wintun supprime la carte
    /// quand le processus meurt.
    /// </summary>
    public async Task StopAsync()
    {
        if (process is null)
        {
            return;
        }

        if (!Exited.IsCompleted)
        {
            process.Kill(entireProcessTree: true);
            try
            {
                await Exited.WaitAsync(StopTimeout);
            }
            catch (TimeoutException)
            {
                logger.LogWarning("tun2socks ne s'arrête pas après {Timeout} s.", StopTimeout.TotalSeconds);
            }
        }
        logger.LogInformation("tun2socks arrêté.");
        process.Dispose();
        process = null;
        job?.Dispose();
        job = null;
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
