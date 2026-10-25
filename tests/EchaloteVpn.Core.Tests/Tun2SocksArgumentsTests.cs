using EchaloteVpn.Core.Tunnel;

namespace EchaloteVpn.Core.Tests;

public class Tun2SocksArgumentsTests
{
    [Fact]
    public void Build_TargetsTorSocksOnLoopback()
    {
        var args = Tun2SocksArguments.Build("EchaloteVpn", 9050);

        Assert.Equal(
            ["--device", "tun://EchaloteVpn", "--proxy", "socks5://127.0.0.1:9050", "--loglevel", "info"],
            args);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    public void InvalidSocksPort_IsRejected(int port)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Tun2SocksArguments.Build("EchaloteVpn", port));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Tor Vpn")]
    [InlineData("tun://EchaloteVpn")]
    [InlineData("EchaloteVpn/x")]
    [InlineData("EchaloteVpn&calc")]
    public void InvalidDeviceName_IsRejected(string name)
    {
        Assert.False(Tun2SocksArguments.IsValidDeviceName(name));
        Assert.Throws<ArgumentException>(() => Tun2SocksArguments.Build(name, 9050));
    }

    [Fact]
    public void TooLongDeviceName_IsRejected()
    {
        Assert.False(Tun2SocksArguments.IsValidDeviceName(new string('a', 65)));
        Assert.True(Tun2SocksArguments.IsValidDeviceName(new string('a', 64)));
    }
}
