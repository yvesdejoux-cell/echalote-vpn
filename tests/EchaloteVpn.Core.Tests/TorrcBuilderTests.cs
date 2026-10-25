using System.Net;
using EchaloteVpn.Core.Configuration;
using EchaloteVpn.Core.Tor;

namespace EchaloteVpn.Core.Tests;

public class TorrcBuilderTests
{
    private static readonly TorPaths Paths = new(
        @"C:\ProgramData\EchaloteVpn\tor",
        @"C:\Program Files\EchaloteVpn\tor-bundle\data\geoip",
        @"C:\Program Files\EchaloteVpn\tor-bundle\data\geoip6");

    private static string[] BuildLines(EchaloteVpnSettings settings, int? pid = null, IPAddress? bind = null) =>
        TorrcBuilder.Build(settings, Paths, pid, bind).Split('\n', StringSplitOptions.RemoveEmptyEntries);

    [Fact]
    public void DefaultSettings_ListenOnLoopbackOnly()
    {
        var lines = BuildLines(new EchaloteVpnSettings());

        Assert.Contains("SocksPort 127.0.0.1:9050", lines);
        Assert.Contains("ControlPort 127.0.0.1:9051", lines);
        Assert.Contains("DNSPort 127.66.0.1:53", lines);
        Assert.Contains("CookieAuthentication 1", lines);
        Assert.DoesNotContain(lines, l => l.StartsWith("ExitNodes", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.StartsWith("__OwningControllerProcess", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.StartsWith("OutboundBindAddress", StringComparison.Ordinal));
    }

    [Fact]
    public void WindowsPaths_AreQuotedAndEscaped()
    {
        var lines = BuildLines(new EchaloteVpnSettings());

        Assert.Contains("""DataDirectory "C:\\ProgramData\\EchaloteVpn\\tor" """.TrimEnd(), lines);
        Assert.Contains("""GeoIPFile "C:\\Program Files\\EchaloteVpn\\tor-bundle\\data\\geoip" """.TrimEnd(), lines);
    }

    [Fact]
    public void ExitCountries_AreNormalizedAndStrict()
    {
        var lines = BuildLines(new EchaloteVpnSettings { ExitCountries = ["FR", "de"] });

        Assert.Contains("ExitNodes {fr},{de}", lines);
        Assert.Contains("StrictNodes 1", lines);
    }

    [Theory]
    [InlineData("FRA")]
    [InlineData("f1")]
    [InlineData("")]
    public void InvalidCountry_IsRejected(string code)
    {
        Assert.Throws<ArgumentException>(() => BuildLines(new EchaloteVpnSettings { ExitCountries = [code] }));
    }

    [Fact]
    public void OwningProcess_IsWritten()
    {
        var lines = BuildLines(new EchaloteVpnSettings(), pid: 1234);

        Assert.Contains("__OwningControllerProcess 1234", lines);
    }

    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("192.168.1.16")]
    [InlineData("::1")]
    [InlineData("localhost")]
    public void NonLoopbackDnsAddress_IsRejected(string address)
    {
        Assert.Throws<ArgumentException>(() => BuildLines(new EchaloteVpnSettings { DnsAddress = address }));
    }

    [Fact]
    public void InvalidPorts_AreRejected()
    {
        Assert.Throws<ArgumentException>(() => BuildLines(new EchaloteVpnSettings { DnsPort = 9050 }));
    }

    [Fact]
    public void OutboundBindAddress_IsWritten()
    {
        var lines = BuildLines(new EchaloteVpnSettings(), bind: IPAddress.Parse("192.168.1.16"));

        Assert.Contains("OutboundBindAddress 192.168.1.16", lines);
    }

    [Fact]
    public void Ipv6OutboundBindAddress_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => BuildLines(new EchaloteVpnSettings(), bind: IPAddress.IPv6Loopback));
    }

    [Fact]
    public void Bridges_AddTransportPluginAndBridgeLines()
    {
        BridgeLine.TryParse("obfs4 37.218.245.14:38224 D9A82D2F9C2F65A18407B1D2B764F130847F8B5D cert=bjRaMrr1BRiAW8IE9U5z27fQaYgOhX1UCmOpg2pFpoMvo6ZgQMzLsaTzzQNTlm7hNcb+Sg iat-mode=0", out var bridge);
        var paths = Paths with { TransportPlugin = @"C:\EchaloteVpn\lyrebird.exe" };

        var lines = TorrcBuilder.Build(new EchaloteVpnSettings { Bridges = [bridge!] }, paths).Split('\n');

        Assert.Contains("UseBridges 1", lines);
        Assert.Contains(@"ClientTransportPlugin obfs4 exec C:\EchaloteVpn\lyrebird.exe", lines);
        Assert.Contains($"Bridge {bridge}", lines);
    }

    [Fact]
    public void NoBridges_NoBridgeLines()
    {
        Assert.DoesNotContain(BuildLines(new EchaloteVpnSettings()), l => l.StartsWith("UseBridges", StringComparison.Ordinal) || l.StartsWith("Bridge", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(@"C:\Program Files\EchaloteVpn\lyrebird.exe")]
    public void Bridges_NeedAPluginPathWithoutSpaces(string? plugin)
    {
        BridgeLine.TryParse("obfs4 37.218.245.14:38224 D9A82D2F9C2F65A18407B1D2B764F130847F8B5D cert=bjRaMrr1BRiAW8IE9U5z27fQaYgOhX1UCmOpg2pFpoMvo6ZgQMzLsaTzzQNTlm7hNcb+Sg iat-mode=0", out var bridge);

        Assert.Throws<ArgumentException>(() => TorrcBuilder.Build(new EchaloteVpnSettings { Bridges = [bridge!] }, Paths with { TransportPlugin = plugin }));
    }
}
