using EchaloteVpn.Core.Tunnel;

namespace EchaloteVpn.Core.Tests;

public class NetworkProfileNameTests
{
    [Theory]
    [InlineData("EchaloteVpn", true)]
    [InlineData("EchaloteVpn 2", true)]
    [InlineData("EchaloteVpn 15", true)]
    [InlineData("EchaloteVpn2", false)]
    [InlineData("EchaloteVpn VIP", false)]
    [InlineData("EchaloteVpn 2 bis", false)]
    [InlineData("Mon EchaloteVpn", false)]
    [InlineData("torvpn", false)]
    [InlineData("AndroidAP_6060 12", false)]
    public void OnlyTheTunnelNameWithOptionalNumber_Matches(string name, bool expected)
    {
        Assert.Equal(expected, NetworkProfileName.IsTunnelProfile(name, "EchaloteVpn"));
    }
}
