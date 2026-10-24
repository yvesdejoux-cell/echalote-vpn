using OnionGuard.Core.Tunnel;

namespace OnionGuard.Core.Tests;

public class Tun2SocksLogLineTests
{
    private static string Json(string level, string msg) =>
        $$"""{"level":"{{level}}","ts":1792867754.41,"caller":"engine/engine.go:250","msg":"{{msg}}"}""";

    [Theory]
    [InlineData("debug", Tun2SocksLogLevel.Debug)]
    [InlineData("info", Tun2SocksLogLevel.Information)]
    [InlineData("warn", Tun2SocksLogLevel.Warning)]
    [InlineData("error", Tun2SocksLogLevel.Error)]
    [InlineData("fatal", Tun2SocksLogLevel.Error)]
    public void JsonLine_KeepsItsLevel(string level, Tun2SocksLogLevel expected)
    {
        var line = Tun2SocksLogLine.Parse(Json(level, "[STACK] tun://OnionGuard <-> socks5://127.0.0.1:9050"));

        Assert.Equal(expected, line.Level);
        Assert.Equal("[STACK] tun://OnionGuard <-> socks5://127.0.0.1:9050", line.Message);
        Assert.False(line.IsUdpRefused);
    }

    [Fact]
    public void WintunTextLine_IsInformation()
    {
        var line = Tun2SocksLogLine.Parse("2026/10/24 20:49:11 Creating adapter");

        Assert.Equal(Tun2SocksLogLevel.Information, line.Level);
        Assert.Equal("2026/10/24 20:49:11 Creating adapter", line.Message);
    }

    [Fact]
    public void RefusedUdp_IsDebugAndFlagged()
    {
        var line = Tun2SocksLogLine.Parse(
            Json("warn", "[UDP] dial 10.66.0.3:137: client handshake: UDP ASSOCIATE: command not supported"));

        Assert.Equal(Tun2SocksLogLevel.Debug, line.Level);
        Assert.True(line.IsUdpRefused);
    }

    [Fact]
    public void TcpConnection_IsDebug()
    {
        var line = Tun2SocksLogLine.Parse(Json("info", "[TCP] 10.66.0.1:64666 <-> 20.190.159.4:443"));

        Assert.Equal(Tun2SocksLogLevel.Debug, line.Level);
        Assert.False(line.IsUdpRefused);
    }

    [Theory]
    [InlineData("{pas du json")]
    [InlineData("""{"level":"warn"}""")]
    public void MalformedJson_IsInformationWithRawText(string text)
    {
        var line = Tun2SocksLogLine.Parse(text);

        Assert.Equal(Tun2SocksLogLevel.Information, line.Level);
        Assert.Equal(text, line.Message);
    }
}
