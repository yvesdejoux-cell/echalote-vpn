using System.Net;
using System.Net.Sockets;
using System.Text;

namespace EchaloteVpn.Core.Tor;

/// <summary>
/// Client du port de contrôle de Tor (127.0.0.1 uniquement).
/// Les commandes sont envoyées une par une ; aucun événement asynchrone n'est demandé.
/// </summary>
public sealed class TorControlClient : IDisposable
{
    private const string BootstrapKey = "status/bootstrap-phase=";

    private readonly TcpClient _tcp;
    private readonly StreamReader _reader;
    private readonly StreamWriter _writer;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private TorControlClient(TcpClient tcp)
    {
        _tcp = tcp;
        var stream = tcp.GetStream();
        _reader = new StreamReader(stream, Encoding.ASCII);
        _writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n" };
    }

    public static async Task<TorControlClient> ConnectAsync(int port, CancellationToken cancellationToken = default)
    {
        var tcp = new TcpClient();
        try
        {
            await tcp.ConnectAsync(IPAddress.Loopback, port, cancellationToken);
            return new TorControlClient(tcp);
        }
        catch
        {
            tcp.Dispose();
            throw;
        }
    }

    public async Task AuthenticateAsync(byte[] cookie, CancellationToken cancellationToken = default) =>
        EnsureSuccess(await SendCommandAsync($"AUTHENTICATE {Convert.ToHexString(cookie)}", cancellationToken), "AUTHENTICATE");

    /// <summary>Tor s'arrête dès que cette connexion se ferme.</summary>
    public async Task TakeOwnershipAsync(CancellationToken cancellationToken = default) =>
        EnsureSuccess(await SendCommandAsync("TAKEOWNERSHIP", cancellationToken), "TAKEOWNERSHIP");

    public async Task<BootstrapStatus> GetBootstrapStatusAsync(CancellationToken cancellationToken = default)
    {
        var reply = await SendCommandAsync("GETINFO status/bootstrap-phase", cancellationToken);
        EnsureSuccess(reply, "GETINFO");

        var line = reply.Lines.FirstOrDefault(l => l.StartsWith(BootstrapKey, StringComparison.Ordinal))
            ?? throw new TorControlException("Réponse GETINFO sans status/bootstrap-phase.");
        return BootstrapStatus.Parse(line[BootstrapKey.Length..]);
    }

    /// <summary>
    /// Valeur d'une clé GETINFO, sur une ligne (« 250-clé=valeur ») ou en bloc (« 250+clé= » puis
    /// des lignes jusqu'au « . »). Renvoie les lignes de la valeur.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetInfoAsync(string key, CancellationToken cancellationToken = default)
    {
        if (key.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
        {
            throw new ArgumentException("Clé GETINFO invalide.", nameof(key));
        }
        var reply = await SendCommandAsync($"GETINFO {key}", cancellationToken);
        EnsureSuccess(reply, "GETINFO");

        var prefix = key + "=";
        var start = reply.Lines.ToList().FindIndex(l => l.StartsWith(prefix, StringComparison.Ordinal));
        if (start < 0)
        {
            throw new TorControlException($"Réponse GETINFO sans {key}.");
        }
        var values = new List<string>();
        var first = reply.Lines[start][prefix.Length..];
        if (first.Length > 0)
        {
            values.Add(first);
        }
        // Lignes du bloc éventuel, avant le « OK » final.
        values.AddRange(reply.Lines.Skip(start + 1).Take(reply.Lines.Count - start - 2));
        return values;
    }

    /// <summary>Nouveaux circuits, donc nouvelle IP de sortie.</summary>
    public async Task NewIdentityAsync(CancellationToken cancellationToken = default) =>
        EnsureSuccess(await SendCommandAsync("SIGNAL NEWNYM", cancellationToken), "SIGNAL NEWNYM");

    /// <summary>Change la carte de sortie de Tor sans le redémarrer (changement de réseau).</summary>
    public async Task SetOutboundBindAddressAsync(IPAddress address, CancellationToken cancellationToken = default)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork)
        {
            throw new ArgumentException("OutboundBindAddress : adresse IPv4 attendue.", nameof(address));
        }
        EnsureSuccess(await SendCommandAsync($"SETCONF OutboundBindAddress={address}", cancellationToken), "SETCONF");
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken = default) =>
        EnsureSuccess(await SendCommandAsync("SIGNAL SHUTDOWN", cancellationToken), "SIGNAL SHUTDOWN");

    public async Task<TorReply> SendCommandAsync(string command, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            await _writer.WriteLineAsync(command.AsMemory(), cancellationToken);
            await _writer.FlushAsync(cancellationToken);
            return await TorReplyReader.ReadAsync(_reader, cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose()
    {
        _tcp.Dispose();
        _lock.Dispose();
    }

    private static void EnsureSuccess(TorReply reply, string command)
    {
        if (!reply.IsSuccess)
        {
            throw new TorControlException($"{command} a échoué : {reply.StatusCode} {string.Join(' ', reply.Lines)}");
        }
    }
}
