using System.IO.Pipes;

namespace EchaloteVpn.Core.Ipc;

/// <summary>Côté interface : une requête, une réponse, une connexion au pipe du service.</summary>
public static class IpcClient
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    /// <exception cref="TimeoutException">Le service ne répond pas (pas démarré ?).</exception>
    public static async Task<ServiceResponse> SendAsync(
        ServiceRequest request, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout ?? DefaultTimeout);

        await using var pipe = new NamedPipeClientStream(".", IpcConstants.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            await pipe.ConnectAsync(cts.Token);
            await IpcProtocol.WriteAsync(pipe, request, cts.Token);
            return await IpcProtocol.ReadAsync<ServiceResponse>(pipe, cts.Token)
                ?? throw new IOException("Le service a fermé la connexion sans répondre.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Le service EchaloteVpn ne répond pas.");
        }
    }
}
