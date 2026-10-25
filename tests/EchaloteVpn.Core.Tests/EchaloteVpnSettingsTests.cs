using EchaloteVpn.Core.Configuration;
using EchaloteVpn.Core.Ipc;

namespace EchaloteVpn.Core.Tests;

public class EchaloteVpnSettingsTests
{
    [Fact]
    public void DefaultSettings_HaveValidDistinctPorts()
    {
        var settings = new EchaloteVpnSettings();

        Assert.True(settings.HasValidPorts());
        Assert.True(settings.HasValidDnsAddress());
        Assert.Equal(53, settings.DnsPort);
        Assert.True(settings.KillSwitchEnabled);
        Assert.False(settings.AllowLocalNetwork);
        Assert.Empty(settings.ExitCountries);
    }

    [Fact]
    public void DuplicatePorts_AreRejected()
    {
        var settings = new EchaloteVpnSettings { DnsPort = 9050 };

        Assert.False(settings.HasValidPorts());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(70000)]
    public void OutOfRangePort_IsRejected(int port)
    {
        var settings = new EchaloteVpnSettings { SocksPort = port };

        Assert.False(settings.HasValidPorts());
    }

    [Fact]
    public void PipeName_IsDefined()
    {
        Assert.False(string.IsNullOrWhiteSpace(IpcConstants.PipeName));
    }
}
