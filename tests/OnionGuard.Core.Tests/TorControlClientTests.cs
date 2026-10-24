using System.Net;
using System.Net.Sockets;
using System.Text;
using OnionGuard.Core.Tor;

namespace OnionGuard.Core.Tests;

public class TorControlClientTests
{
    /// <summary>Faux port de contrôle : enregistre chaque commande et renvoie la réponse prévue.</summary>
    private static (int Port, Task<List<string>> Commands) StartFakeTor(params string[][] replies)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var commands = Task.Run(async () =>
        {
            var received = new List<string>();
            try
            {
                using var socket = await listener.AcceptTcpClientAsync();
                using var stream = socket.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII);
                using var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };
                foreach (var reply in replies)
                {
                    received.Add(await reader.ReadLineAsync() ?? string.Empty);
                    foreach (var line in reply)
                    {
                        await writer.WriteLineAsync(line);
                    }
                }
            }
            finally
            {
                listener.Stop();
            }
            return received;
        });

        return (port, commands);
    }

    [Fact]
    public async Task Authenticate_ThenReadBootstrap()
    {
        var (port, commands) = StartFakeTor(
            ["250 OK"],
            ["250-status/bootstrap-phase=NOTICE BOOTSTRAP PROGRESS=100 TAG=done SUMMARY=\"Done\"", "250 OK"]);

        BootstrapStatus status;
        using (var client = await TorControlClient.ConnectAsync(port))
        {
            await client.AuthenticateAsync([0x01, 0xAB]);
            status = await client.GetBootstrapStatusAsync();
        }

        Assert.True(status.IsDone);
        Assert.Equal(new[] { "AUTHENTICATE 01AB", "GETINFO status/bootstrap-phase" }, await commands);
    }

    [Fact]
    public async Task NewIdentity_SendsNewnym()
    {
        var (port, commands) = StartFakeTor(["250 OK"]);

        using (var client = await TorControlClient.ConnectAsync(port))
        {
            await client.NewIdentityAsync();
        }

        Assert.Equal(new[] { "SIGNAL NEWNYM" }, await commands);
    }

    [Fact]
    public async Task FailedAuthentication_Throws()
    {
        var (port, commands) = StartFakeTor(["515 Authentication failed"]);

        using (var client = await TorControlClient.ConnectAsync(port))
        {
            await Assert.ThrowsAsync<TorControlException>(() => client.AuthenticateAsync([0x00]));
        }

        await commands;
    }

    [Fact]
    public async Task SetOutboundBindAddress_SendsSetconf()
    {
        var (port, commands) = StartFakeTor(["250 OK"]);

        using (var client = await TorControlClient.ConnectAsync(port))
        {
            await client.SetOutboundBindAddressAsync(IPAddress.Parse("192.168.1.16"));
        }

        Assert.Equal(new[] { "SETCONF OutboundBindAddress=192.168.1.16" }, await commands);
    }

    [Fact]
    public async Task SetOutboundBindAddress_RejectsIpv6()
    {
        var (port, commands) = StartFakeTor();

        using (var client = await TorControlClient.ConnectAsync(port))
        {
            await Assert.ThrowsAsync<ArgumentException>(() => client.SetOutboundBindAddressAsync(IPAddress.IPv6Loopback));
        }

        Assert.Empty(await commands);
    }

    [Fact]
    public async Task GetInfo_ReadsSingleLineAndBlockValues()
    {
        var (port, commands) = StartFakeTor(
            ["250-ip-to-country/185.220.101.8=de", "250 OK"],
            ["250+circuit-status=", "1 BUILT $A~a,$B~b,$C~c PURPOSE=GENERAL", "2 BUILT $A~a PURPOSE=GENERAL", ".", "250 OK"]);

        IReadOnlyList<string> country, circuits;
        using (var client = await TorControlClient.ConnectAsync(port))
        {
            country = await client.GetInfoAsync("ip-to-country/185.220.101.8");
            circuits = await client.GetInfoAsync("circuit-status");
        }

        Assert.Equal(["de"], country);
        Assert.Equal(["1 BUILT $A~a,$B~b,$C~c PURPOSE=GENERAL", "2 BUILT $A~a PURPOSE=GENERAL"], circuits);
        Assert.Equal(new[] { "GETINFO ip-to-country/185.220.101.8", "GETINFO circuit-status" }, await commands);
    }
}
