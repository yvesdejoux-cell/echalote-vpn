using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using OnionGuard.Core.Ipc;

namespace OnionGuard.Service.Ipc;

/// <summary>
/// Named pipe par lequel l'interface pilote le service : une requête et une réponse par connexion.
/// </summary>
public sealed class PipeServer(ConnectionController controller, ILogger<PipeServer> logger) : BackgroundService
{
    private static readonly TimeSpan ClientTimeout = TimeSpan.FromSeconds(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(@"Interface attendue sur \\.\pipe\{Pipe}.", IpcConstants.PipeName);
        var first = true;
        while (!stoppingToken.IsCancellationRequested)
        {
            NamedPipeServerStream pipe;
            try
            {
                pipe = CreatePipe(first);
                first = false;
            }
            catch (UnauthorizedAccessException) when (first)
            {
                // FirstPipeInstance : un autre programme a déjà pris le nom du pipe.
                logger.LogCritical("Le pipe {Pipe} existe déjà : un autre service OnionGuard tourne-t-il ?", IpcConstants.PipeName);
                throw;
            }

            try
            {
                await pipe.WaitForConnectionAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                await pipe.DisposeAsync();
                break;
            }

            _ = HandleClientAsync(pipe, stoppingToken);
        }
    }

    private static NamedPipeServerStream CreatePipe(bool first)
    {
        // Le service tourne sous SYSTEM, l'interface sous le compte de l'utilisateur connecté.
        // Le compte du service garde le contrôle total, nécessaire pour créer les instances
        // suivantes du pipe (SYSTEM une fois installé, le développeur lancé en console).
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(
            WindowsIdentity.GetCurrent().User!, PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        // Les utilisateurs en session sur ce PC pilotent OnionGuard, comme un VPN ordinaire.
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.InteractiveSid, null), PipeAccessRights.ReadWrite, AccessControlType.Allow));
        // Un named pipe est joignable depuis un autre PC (SMB) : interdit à tout accès par le réseau.
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));

        var options = PipeOptions.Asynchronous | (first ? PipeOptions.FirstPipeInstance : PipeOptions.None);
        return NamedPipeServerStreamAcl.Create(
            IpcConstants.PipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte, options, 0, 0, security);
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken stoppingToken)
    {
        await using var _ = pipe;
        try
        {
            ServiceRequest? request;
            using (var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken))
            {
                readTimeout.CancelAfter(ClientTimeout);
                request = await IpcProtocol.ReadAsync<ServiceRequest>(pipe, readTimeout.Token);
            }
            if (request is null)
            {
                return;
            }

            // Une déconnexion peut durer plus que le délai de lecture : il ne compte pas ici.
            var response = await HandleAsync(request);
            using var writeTimeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            writeTimeout.CancelAfter(ClientTimeout);
            await IpcProtocol.WriteAsync(pipe, response, writeTimeout.Token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
        {
            logger.LogWarning("Client IPC en erreur : {Message}", ex.Message);
        }
    }

    /// <summary>L'interface reçoit toujours une réponse, même si la commande échoue de façon imprévue.</summary>
    private async Task<ServiceResponse> HandleAsync(ServiceRequest request)
    {
        try
        {
            return await RunCommandAsync(request);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Commande {Command} en échec.", request.Command);
            return new ServiceResponse(false, controller.Status, controller.Settings, ex.Message);
        }
    }

    private async Task<ServiceResponse> RunCommandAsync(ServiceRequest request)
    {
        string? error = null;
        switch (request.Command)
        {
            case ServiceCommand.GetStatus:
            case ServiceCommand.GetSettings:
                break;
            case ServiceCommand.Connect:
                error = await controller.ConnectAsync();
                break;
            case ServiceCommand.Disconnect:
                await controller.DisconnectAsync();
                break;
            case ServiceCommand.NewIdentity:
                error = await controller.NewIdentityAsync();
                break;
            case ServiceCommand.SetSettings:
                error = request.Settings is null ? "Réglages manquants." : await controller.SetSettingsAsync(request.Settings);
                break;
            default:
                error = $"Commande inconnue : {request.Command}";
                break;
        }
        return new ServiceResponse(error is null, controller.Status, controller.Settings, error);
    }
}
