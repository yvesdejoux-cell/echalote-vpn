using System.Text;
using EchaloteVpn.Core.Configuration;
using EchaloteVpn.Core.Ipc;

namespace EchaloteVpn.Core.Tests;

public class IpcProtocolTests
{
    [Fact]
    public async Task Request_RoundTrips()
    {
        using var stream = new MemoryStream();
        var request = new ServiceRequest(
            ServiceCommand.SetSettings,
            new UserSettings { ExitCountries = ["DE", "NL"], AllowLocalNetwork = true });

        await IpcProtocol.WriteAsync(stream, request);
        stream.Position = 0;
        var read = await IpcProtocol.ReadAsync<ServiceRequest>(stream);

        Assert.Equal(ServiceCommand.SetSettings, read!.Command);
        Assert.Equal(["DE", "NL"], read.Settings!.ExitCountries);
        Assert.True(read.Settings.AllowLocalNetwork);
        Assert.True(read.Settings.KillSwitchEnabled);
    }

    [Fact]
    public async Task Response_UsesEnumNamesAndRoundTrips()
    {
        using var stream = new MemoryStream();
        var response = new ServiceResponse(false, new ServiceStatus(ConnectionState.Connecting, 45, "panne"), Error: "refusé");

        await IpcProtocol.WriteAsync(stream, response);
        var text = Encoding.UTF8.GetString(stream.ToArray());
        stream.Position = 0;
        var read = await IpcProtocol.ReadAsync<ServiceResponse>(stream);

        Assert.Contains("\"Connecting\"", text);
        Assert.EndsWith("\n", text);
        Assert.Equal(response.Status, read!.Status);
        Assert.Equal("refusé", read.Error);
    }

    [Fact]
    public async Task ClosedStream_ReturnsNull()
    {
        using var stream = new MemoryStream();

        Assert.Null(await IpcProtocol.ReadAsync<ServiceRequest>(stream));
    }

    [Fact]
    public async Task TruncatedMessage_Throws()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("""{"Command":"Connect"}"""));

        await Assert.ThrowsAsync<EndOfStreamException>(() => IpcProtocol.ReadAsync<ServiceRequest>(stream));
    }

    [Fact]
    public async Task TooLongMessage_Throws()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(new string('a', IpcProtocol.MaxMessageLength + 1)));

        await Assert.ThrowsAsync<InvalidDataException>(() => IpcProtocol.ReadAsync<ServiceRequest>(stream));
    }
}
