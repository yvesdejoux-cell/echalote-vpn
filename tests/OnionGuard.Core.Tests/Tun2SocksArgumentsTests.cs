using OnionGuard.Core.Tunnel;

namespace OnionGuard.Core.Tests;

public class Tun2SocksArgumentsTests
{
    [Fact]
    public void Build_TargetsTorSocksOnLoopback()
    {
        var args = Tun2SocksArguments.Build("OnionGuard", 9050);

        Assert.Equal(
            ["--device", "tun://OnionGuard", "--proxy", "socks5://127.0.0.1:9050", "--loglevel", "info"],
            args);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    public void InvalidSocksPort_IsRejected(int port)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Tun2SocksArguments.Build("OnionGuard", port));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Tor Vpn")]
    [InlineData("tun://OnionGuard")]
    [InlineData("OnionGuard/x")]
    [InlineData("OnionGuard&calc")]
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
