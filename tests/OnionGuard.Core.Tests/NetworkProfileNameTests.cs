using OnionGuard.Core.Tunnel;

namespace OnionGuard.Core.Tests;

public class NetworkProfileNameTests
{
    [Theory]
    [InlineData("OnionGuard", true)]
    [InlineData("OnionGuard 2", true)]
    [InlineData("OnionGuard 15", true)]
    [InlineData("OnionGuard2", false)]
    [InlineData("OnionGuard VIP", false)]
    [InlineData("OnionGuard 2 bis", false)]
    [InlineData("Mon OnionGuard", false)]
    [InlineData("torvpn", false)]
    [InlineData("AndroidAP_6060 12", false)]
    public void OnlyTheTunnelNameWithOptionalNumber_Matches(string name, bool expected)
    {
        Assert.Equal(expected, NetworkProfileName.IsTunnelProfile(name, "OnionGuard"));
    }
}
