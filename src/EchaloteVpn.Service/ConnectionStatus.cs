using EchaloteVpn.Core.Ipc;

namespace EchaloteVpn.Service;

/// <summary>
/// État de la connexion, partagé entre le contrôleur, Tor et le tunnel, et lu par l'interface.
/// </summary>
public sealed class ConnectionStatus
{
    private readonly Lock gate = new();
    private ConnectionState state = ConnectionState.Disconnected;
    private int bootstrapPercent;
    private string? lastError;
    private string? exitCountry;

    public ServiceStatus Snapshot()
    {
        lock (gate)
        {
            return new ServiceStatus(state, bootstrapPercent, lastError, state == ConnectionState.Connected ? exitCountry : null);
        }
    }

    public ConnectionState State
    {
        get
        {
            lock (gate)
            {
                return state;
            }
        }
    }

    public void SetState(ConnectionState newState, string? error = null)
    {
        lock (gate)
        {
            state = newState;
            exitCountry = null;
            if (newState is ConnectionState.Connecting or ConnectionState.Disconnected)
            {
                bootstrapPercent = 0;
            }
            if (error is not null || newState is ConnectionState.Connecting or ConnectionState.Disconnected)
            {
                lastError = error;
            }
        }
    }

    public void ReportBootstrap(int percent)
    {
        lock (gate)
        {
            bootstrapPercent = percent;
        }
    }

    /// <summary>Le tunnel monte ou tombe (Tor relancé, réseau changé) : seulement pendant une connexion.</summary>
    public void ReportTunnel(bool active)
    {
        lock (gate)
        {
            if (state is ConnectionState.Connecting or ConnectionState.Connected)
            {
                state = active ? ConnectionState.Connected : ConnectionState.Connecting;
                if (!active)
                {
                    exitCountry = null;
                }
                if (active)
                {
                    lastError = null;
                }
            }
        }
    }

    /// <summary>Pays du dernier relais de sortie (null = inconnu, par exemple juste après une nouvelle identité).</summary>
    public void ReportExitCountry(string? country)
    {
        lock (gate)
        {
            exitCountry = country;
        }
    }

    /// <summary>Une erreur pendant la connexion, sans changer d'état : Tor ou le tunnel réessaient.</summary>
    public void ReportError(string message)
    {
        lock (gate)
        {
            lastError = message;
        }
    }
}
