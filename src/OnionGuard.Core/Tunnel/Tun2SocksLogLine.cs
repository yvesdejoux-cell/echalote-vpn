using System.Text.Json;

namespace OnionGuard.Core.Tunnel;

public enum Tun2SocksLogLevel
{
    Debug,
    Information,
    Warning,
    Error,
}

/// <summary>
/// Une ligne du journal de tun2socks, qui écrit tout sur sa sortie d'erreur : du JSON pour le
/// moteur, du texte pour le pilote Wintun.
/// </summary>
public readonly record struct Tun2SocksLogLine(Tun2SocksLogLevel Level, string Message, bool IsUdpRefused = false)
{
    public static Tun2SocksLogLine Parse(string line)
    {
        if (!line.StartsWith('{'))
        {
            return new(Tun2SocksLogLevel.Information, line);
        }

        string? level;
        string? message;
        try
        {
            using var json = JsonDocument.Parse(line);
            level = json.RootElement.TryGetProperty("level", out var l) ? l.GetString() : null;
            message = json.RootElement.TryGetProperty("msg", out var m) ? m.GetString() : null;
        }
        catch (JsonException)
        {
            return new(Tun2SocksLogLevel.Information, line);
        }
        if (message is null)
        {
            return new(Tun2SocksLogLevel.Information, line);
        }

        // Tor ne transporte pas l'UDP : refus attendu, bloqué à l'étape 4.
        if (message.Contains("UDP ASSOCIATE: command not supported", StringComparison.Ordinal))
        {
            return new(Tun2SocksLogLevel.Debug, message, IsUdpRefused: true);
        }
        // Une ligne par connexion, avec sa destination : pas dans le journal ordinaire.
        if (message.StartsWith("[TCP] ", StringComparison.Ordinal))
        {
            return new(Tun2SocksLogLevel.Debug, message);
        }

        return new(level switch
        {
            "debug" => Tun2SocksLogLevel.Debug,
            "warn" => Tun2SocksLogLevel.Warning,
            "error" or "fatal" or "panic" => Tun2SocksLogLevel.Error,
            _ => Tun2SocksLogLevel.Information,
        }, message);
    }
}
