using OnionGuard.Core.Configuration;
using OnionGuard.Core.Tor;

namespace OnionGuard.Core.Tests;

public class BridgeLineTests
{
    private const string Valid =
        "obfs4 37.218.245.14:38224 D9A82D2F9C2F65A18407B1D2B764F130847F8B5D cert=bjRaMrr1BRiAW8IE9U5z27fQaYgOhX1UCmOpg2pFpoMvo6ZgQMzLsaTzzQNTlm7hNcb+Sg iat-mode=0";

    [Theory]
    [InlineData(Valid)]
    [InlineData("Bridge " + Valid)]
    [InlineData("  " + Valid + "  ")]
    public void ValidLine_IsParsedAndCanonical(string text)
    {
        Assert.True(BridgeLine.TryParse(text, out var bridge));
        Assert.Equal("37.218.245.14", bridge!.Endpoint.Address.ToString());
        Assert.Equal(38224, bridge.Endpoint.Port);
        Assert.Equal(Valid, bridge.ToString());
    }

    [Fact]
    public void Fingerprint_IsUppercased()
    {
        Assert.True(BridgeLine.TryParse(Valid.Replace("D9A82D2F9C2F65A18407B1D2B764F130847F8B5D", "d9a82d2f9c2f65a18407b1d2b764f130847f8b5d"), out var bridge));
        Assert.Equal("D9A82D2F9C2F65A18407B1D2B764F130847F8B5D", bridge!.Fingerprint);
    }

    [Theory]
    [InlineData("")]
    [InlineData("snowflake 192.0.2.3:80 2B280B23E1107BB62ABFC40DDCC8824814F80A72 url=https://example.org/")]
    [InlineData("obfs4 37.218.245.14 D9A82D2F9C2F65A18407B1D2B764F130847F8B5D cert=bjRaMrr1BRiAW8IE9U5z27fQaYgOhX1UCmOpg2pFpoMvo6ZgQMzLsaTzzQNTlm7hNcb+Sg iat-mode=0")]
    [InlineData("obfs4 37.218.245.14:70000 D9A82D2F9C2F65A18407B1D2B764F130847F8B5D cert=bjRaMrr1BRiAW8IE9U5z27fQaYgOhX1UCmOpg2pFpoMvo6ZgQMzLsaTzzQNTlm7hNcb+Sg iat-mode=0")]
    [InlineData("obfs4 999.218.245.14:443 D9A82D2F9C2F65A18407B1D2B764F130847F8B5D cert=bjRaMrr1BRiAW8IE9U5z27fQaYgOhX1UCmOpg2pFpoMvo6ZgQMzLsaTzzQNTlm7hNcb+Sg iat-mode=0")]
    [InlineData("obfs4 37.218.245.14:443 D9A82D2F cert=bjRaMrr1BRiAW8IE9U5z27fQaYgOhX1UCmOpg2pFpoMvo6ZgQMzLsaTzzQNTlm7hNcb+Sg iat-mode=0")]
    [InlineData("obfs4 37.218.245.14:443 D9A82D2F9C2F65A18407B1D2B764F130847F8B5D cert=bjRa iat-mode=0")]
    [InlineData("obfs4 37.218.245.14:443 D9A82D2F9C2F65A18407B1D2B764F130847F8B5D cert=bjRaMrr1BRiAW8IE9U5z27fQaYgOhX1UCmOpg2pFpoMvo6ZgQMzLsaTzzQNTlm7hNcb+Sg iat-mode=3")]
    // Tentatives d'injection dans le torrc
    [InlineData(Valid + "\nSocksPort 0.0.0.0:9050")]
    [InlineData(Valid + " ExitNodes {us}")]
    [InlineData("obfs4 37.218.245.14:443 D9A82D2F9C2F65A18407B1D2B764F130847F8B5D cert=a\"b\nControlPort 9999 iat-mode=0")]
    public void InvalidLine_IsRejected(string text)
    {
        Assert.False(BridgeLine.TryParse(text, out var bridge));
        Assert.Null(bridge);
    }

    [Fact]
    public void BuiltInBridges_AreReadFromPtConfig()
    {
        var json = """
            {
              "bridges": {
                "obfs4": [ "VALID", "obfs4 pas une ligne" ],
                "snowflake": [ "snowflake 192.0.2.3:80 2B280B23E1107BB62ABFC40DDCC8824814F80A72" ]
              }
            }
            """.Replace("VALID", Valid);

        var bridges = BridgeLine.ParseBuiltIn(json);

        Assert.Equal(Valid, Assert.Single(bridges).ToString());
        Assert.Empty(BridgeLine.ParseBuiltIn("{}"));
    }

    [Fact]
    public void Settings_CustomBridgesAreNormalizedAndRequired()
    {
        var settings = new UserSettings { Bridges = BridgeMode.Custom, CustomBridges = ["Bridge " + Valid, Valid, ""] }.Normalize();

        Assert.Equal([Valid], settings.CustomBridges);
        Assert.Throws<ArgumentException>(() => new UserSettings { Bridges = BridgeMode.Custom }.Normalize());
        Assert.Throws<ArgumentException>(() => new UserSettings { CustomBridges = ["obfs4 n'importe quoi"] }.Normalize());
    }

    [Fact]
    public void Settings_BridgesAreResolvedForTor()
    {
        BridgeLine.TryParse(Valid, out var bridge);

        Assert.Empty(new UserSettings().ToOnionGuardSettings([bridge!]).Bridges);
        Assert.Equal([bridge!], new UserSettings { Bridges = BridgeMode.BuiltIn }.ToOnionGuardSettings([bridge!]).Bridges);
        Assert.Equal([bridge!], new UserSettings { Bridges = BridgeMode.Custom, CustomBridges = [Valid] }.ToOnionGuardSettings().Bridges);
        Assert.Throws<InvalidOperationException>(() => new UserSettings { Bridges = BridgeMode.BuiltIn }.ToOnionGuardSettings([]));
    }
}
