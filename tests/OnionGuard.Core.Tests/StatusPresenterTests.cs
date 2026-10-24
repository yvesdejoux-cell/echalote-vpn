using OnionGuard.Core.Ipc;

namespace OnionGuard.Core.Tests;

public class StatusPresenterTests
{
    [Fact]
    public void NoService_DisablesTheButton()
    {
        var view = StatusPresenter.Present(null);

        Assert.Equal(MainAction.None, view.Action);
        Assert.Contains("administrateur", view.Detail);
    }

    [Fact]
    public void Disconnected_OffersToConnect()
    {
        var view = StatusPresenter.Present(new ServiceStatus(ConnectionState.Disconnected));

        Assert.Equal("Déconnecté", view.Title);
        Assert.Equal(MainAction.Connect, view.Action);
        Assert.False(view.ShowProgress);
    }

    [Theory]
    [InlineData(45, "Démarrage de Tor : 45 %")]
    [InlineData(100, "Mise en place du tunnel…")]
    public void Connecting_ShowsProgressAndOffersToDisconnect(int percent, string detail)
    {
        var view = StatusPresenter.Present(new ServiceStatus(ConnectionState.Connecting, percent));

        Assert.Equal(detail, view.Detail);
        Assert.True(view.ShowProgress);
        Assert.Equal(percent, view.Progress);
        Assert.Equal(MainAction.Disconnect, view.Action);
    }

    [Fact]
    public void Connecting_ShowsTheLastErrorFirst()
    {
        var view = StatusPresenter.Present(new ServiceStatus(ConnectionState.Connecting, 30, "Tor : panne"));

        Assert.Equal("Tor : panne", view.Detail);
    }

    [Fact]
    public void Connected_ShowsTheExitCountryWhenKnown()
    {
        Assert.Equal("Tout le trafic passe par Tor.", StatusPresenter.Present(new ServiceStatus(ConnectionState.Connected, 100)).Detail);
        Assert.Contains("DE", StatusPresenter.Present(new ServiceStatus(ConnectionState.Connected, 100, ExitCountry: "DE")).Detail);
    }

    [Fact]
    public void Disconnecting_DisablesTheButton()
    {
        Assert.Equal(MainAction.None, StatusPresenter.Present(new ServiceStatus(ConnectionState.Disconnecting)).Action);
    }

    [Fact]
    public void Error_ShowsTheMessageAndOffersToRetry()
    {
        var view = StatusPresenter.Present(new ServiceStatus(ConnectionState.Error, LastError: "Kill switch impossible"));

        Assert.Equal("Kill switch impossible", view.Detail);
        Assert.Equal("Réessayer", view.ButtonLabel);
        Assert.Equal(MainAction.Connect, view.Action);
    }

    [Theory]
    [InlineData("DE", "Allemagne (DE)")]
    [InlineData("nl", "Pays-Bas (NL)")]
    [InlineData("ZZ", "ZZ")]
    public void CountryName_IsFrenchForKnownCountries(string code, string expected)
    {
        Assert.Equal(expected, StatusPresenter.CountryName(code));
    }
}
