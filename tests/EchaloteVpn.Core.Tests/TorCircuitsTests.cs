using System.Net;
using EchaloteVpn.Core.Tor;

namespace EchaloteVpn.Core.Tests;

public class TorCircuitsTests
{
    private const string A = "1111111111111111111111111111111111111111";
    private const string B = "2222222222222222222222222222222222222222";
    private const string C = "3333333333333333333333333333333333333333";
    private const string D = "4444444444444444444444444444444444444444";

    [Fact]
    public void Exit_IsTheLastHopOfTheNewestGeneralCircuit()
    {
        string[] lines =
        [
            $"5 BUILT ${A}~a,${B}~b,${C}~c BUILD_FLAGS=NEED_CAPACITY PURPOSE=GENERAL TIME_CREATED=2026-10-24T20:00:00",
            $"9 BUILT ${A}~a,${B}~b,${D}~d BUILD_FLAGS=NEED_CAPACITY PURPOSE=GENERAL TIME_CREATED=2026-10-24T20:01:00",
            $"7 BUILT ${A}~a,${C}~c,${B}~b PURPOSE=GENERAL",
        ];

        Assert.Equal(D, TorCircuits.FindExitFingerprint(lines));
    }

    [Fact]
    public void InternalOrUnbuiltOrShortCircuits_AreIgnored()
    {
        string[] lines =
        [
            $"20 BUILT ${A}~a,${B}~b,${D}~d BUILD_FLAGS=IS_INTERNAL,NEED_CAPACITY PURPOSE=GENERAL",
            $"21 EXTENDED ${A}~a,${B}~b PURPOSE=GENERAL",
            $"22 BUILT ${A}~a,${B}~b,${D}~d PURPOSE=HS_CLIENT_INTRO",
            $"23 BUILT ${A}~a PURPOSE=GENERAL",
            $"4 BUILT ${A}~a,${B}~b,${C}~c PURPOSE=GENERAL",
        ];

        Assert.Equal(C, TorCircuits.FindExitFingerprint(lines));
    }

    [Fact]
    public void NoUsableCircuit_GivesNull()
    {
        Assert.Null(TorCircuits.FindExitFingerprint([]));
        Assert.Null(TorCircuits.FindExitFingerprint(["3 LAUNCHED  PURPOSE=GENERAL"]));
    }

    [Theory]
    // Consensus des microdescripteurs : pas de condensé.
    [InlineData("r Nickname ERERERERERERERERERERERERERE 2026-10-24 19:00:00 185.220.101.8 9001 0")]
    // Consensus complet : avec le condensé.
    [InlineData("r Nickname ERERERERERERERERERERERERERE ZGlnZXN0ZGlnZXN0ZGlnZXN0ZGk 2026-10-24 19:00:00 185.220.101.8 9001 0")]
    public void RouterAddress_IsReadFromTheRLine(string rLine)
    {
        string[] lines = [rLine, "s Exit Fast Running Stable Valid", "w Bandwidth=12000"];

        Assert.Equal(IPAddress.Parse("185.220.101.8"), TorCircuits.FindRouterAddress(lines));
    }

    [Fact]
    public void RouterAddress_WithoutRLine_IsNull()
    {
        Assert.Null(TorCircuits.FindRouterAddress(["s Exit Fast"]));
    }

    [Theory]
    [InlineData("de", "DE")]
    [InlineData("??", null)]
    [InlineData("", null)]
    public void Country_IsUppercasedOrUnknown(string value, string? expected)
    {
        Assert.Equal(expected, TorCircuits.ParseCountry(value));
    }
}
