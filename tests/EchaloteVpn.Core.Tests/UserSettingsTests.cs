using EchaloteVpn.Core.Configuration;

namespace EchaloteVpn.Core.Tests;

public class UserSettingsTests
{
    [Fact]
    public void Defaults_KillSwitchOnLocalNetworkOff()
    {
        var settings = new UserSettings();

        Assert.True(settings.KillSwitchEnabled);
        Assert.False(settings.AllowLocalNetwork);
        Assert.Empty(settings.ExitCountries);
    }

    [Fact]
    public void Normalize_UppercasesTrimsAndRemovesDuplicates()
    {
        var settings = new UserSettings { ExitCountries = ["de", " NL ", "De"] }.Normalize();

        Assert.Equal(["DE", "NL"], settings.ExitCountries);
    }

    [Theory]
    [InlineData("DEU")]
    [InlineData("1A")]
    [InlineData("")]
    public void Normalize_RejectsInvalidCode(string code)
    {
        Assert.Throws<ArgumentException>(() => new UserSettings { ExitCountries = [code] }.Normalize());
    }

    [Fact]
    public void Normalize_RejectsTooManyCountries()
    {
        var codes = Enumerable.Range(0, UserSettings.MaxExitCountries + 1).Select(i => $"{(char)('A' + i / 26)}{(char)('A' + i % 26)}").ToList();

        Assert.Throws<ArgumentException>(() => new UserSettings { ExitCountries = codes }.Normalize());
    }

    [Fact]
    public void ToEchaloteVpnSettings_CarriesUserChoicesAndDefaultPorts()
    {
        var tor = new UserSettings { ExitCountries = ["DE"], KillSwitchEnabled = false, AllowLocalNetwork = true }.ToEchaloteVpnSettings();

        Assert.Equal(["DE"], tor.ExitCountries);
        Assert.False(tor.KillSwitchEnabled);
        Assert.True(tor.AllowLocalNetwork);
        Assert.Equal(new EchaloteVpnSettings().SocksPort, tor.SocksPort);
    }

    [Fact]
    public void PersistedState_RoundTrips()
    {
        var state = new PersistedState(new UserSettings { ExitCountries = ["CH"] }, ConnectionWanted: true);

        var read = PersistedState.Parse(state.Serialize());

        Assert.True(read.ConnectionWanted);
        Assert.Equal(["CH"], read.Settings.ExitCountries);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{ abîmé")]
    [InlineData("""{"Settings":{"ExitCountries":["XXX"]},"ConnectionWanted":true}""")]
    [InlineData("null")]
    public void PersistedState_DamagedFile_GivesDefaults(string json)
    {
        Assert.Equal(PersistedState.Default, PersistedState.Parse(json));
    }
}
