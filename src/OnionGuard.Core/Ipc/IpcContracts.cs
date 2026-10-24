using OnionGuard.Core.Configuration;

namespace OnionGuard.Core.Ipc;

/// <summary>
/// Contrats d'échange entre l'interface (utilisateur) et le service (SYSTEM), par named pipe.
/// </summary>
public static class IpcConstants
{
    public const string PipeName = "OnionGuard.Service";
}

public enum ConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Disconnecting,
    Error,
}

public enum ServiceCommand
{
    GetStatus,
    Connect,
    Disconnect,
    NewIdentity,
    GetSettings,
    SetSettings,
}

/// <summary><see cref="Settings"/> n'est utilisé que par <see cref="ServiceCommand.SetSettings"/>.</summary>
public sealed record ServiceRequest(ServiceCommand Command, UserSettings? Settings = null);

/// <summary>
/// État du service : connexion, avancement du bootstrap de Tor (0-100), dernière erreur,
/// et pays du dernier relais de sortie quand il est connu.
/// </summary>
public sealed record ServiceStatus(
    ConnectionState State,
    int BootstrapPercent = 0,
    string? LastError = null,
    string? ExitCountry = null);

/// <summary><see cref="Error"/> explique le refus d'une commande ; l'état est toujours joint.</summary>
public sealed record ServiceResponse(bool Ok, ServiceStatus Status, UserSettings? Settings = null, string? Error = null);
