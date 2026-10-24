using System.Net;
using OnionGuard.Core.Tunnel;

namespace OnionGuard.Core.Tests;

public class TunnelRoutePlanTests
{
    private static readonly TunnelRoutePlan Plan = TunnelRoutePlan.Default;

    [Fact]
    public void Default_LocalAddressIsInA30()
    {
        Assert.Equal("10.66.0.1/30", Plan.LocalAddress.ToString());
        Assert.Equal(IPAddress.Parse("10.66.0.0"), Plan.LocalAddress.Network);
        Assert.Equal(IPAddress.Parse("10.66.0.3"), Plan.LocalAddress.Broadcast);
    }

    [Fact]
    public void Routes_AreTheTwoHalves_NotTheDefaultRoute()
    {
        Assert.Equal(["0.0.0.0/1", "128.0.0.0/1"], Plan.Routes.Select(r => r.ToString()));
        Assert.DoesNotContain(Plan.Routes, r => r.Length == 0);
        Assert.All(Plan.Routes, r => Assert.Equal(r.Network, r.Address));
    }

    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("8.8.8.8")]
    [InlineData("127.255.255.255")]
    [InlineData("128.0.0.0")]
    [InlineData("185.220.101.1")]
    [InlineData("255.255.255.255")]
    public void Routes_CoverEachIpv4AddressExactlyOnce(string address)
    {
        Assert.Single(Plan.Routes, r => r.Contains(IPAddress.Parse(address)));
    }

    [Fact]
    public void Routes_AreLessSpecificThanLoopback()
    {
        // Windows garde 127.0.0.0/8 sur la boucle locale : le préfixe le plus long gagne.
        Assert.All(Plan.Routes, r => Assert.True(r.Length < 8));
    }

    [Theory]
    [InlineData("10.66.0.0/30")]
    [InlineData("10.66.0.3/30")]
    [InlineData("10.66.0.1/31")]
    [InlineData("10.66.0.1/32")]
    public void InvalidLocalAddress_IsRejected(string prefix)
    {
        Assert.Throws<ArgumentException>(() => new TunnelRoutePlan(Ipv4Prefix.Parse(prefix)));
    }

    [Theory]
    [InlineData("10.66.0.2", true)]
    [InlineData("10.66.0.4", false)]
    [InlineData("::1", false)]
    public void Prefix_Contains(string address, bool expected)
    {
        Assert.Equal(expected, Ipv4Prefix.Parse("10.66.0.1/30").Contains(IPAddress.Parse(address)));
    }

    [Theory]
    [InlineData("10.66.0.1")]
    [InlineData("10.66.0.1/33")]
    [InlineData("10.66.0.1/-1")]
    [InlineData("::1/64")]
    [InlineData("pas une adresse/8")]
    public void InvalidPrefix_IsRejected(string text)
    {
        Assert.Throws<FormatException>(() => Ipv4Prefix.Parse(text));
    }
}
