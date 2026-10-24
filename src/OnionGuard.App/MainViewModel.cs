using System.ComponentModel;
using System.IO;
using System.Windows.Threading;
using OnionGuard.Core.Configuration;
using OnionGuard.Core.Ipc;

namespace OnionGuard.App;

/// <summary>Un choix de la liste des modes de ponts.</summary>
public sealed record BridgeOption(BridgeMode Mode, string Label)
{
    public override string ToString() => Label;
}

/// <summary>Un choix de la liste des pays de sortie ; Code null = automatique.</summary>
public sealed record CountryOption(string? Code, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// État affiché par la fenêtre. Interroge le service toutes les secondes ; le bouton principal
/// envoie Connect ou Disconnect selon l'état ; les réglages s'appliquent à la prochaine connexion.
/// </summary>
public sealed class MainViewModel : INotifyPropertyChanged
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    // Une déconnexion attend l'arrêt du tunnel et de Tor.
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(90);
    // Service absent : la fenêtre le dit vite, sans attendre les 5 s par défaut.
    private static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(1.5);

    private readonly DispatcherTimer timer;
    private StatusView view = StatusPresenter.Present(null);
    private ConnectionState? state;
    private bool polling;
    private bool splash = true;
    private bool busy;
    private string? message;
    private bool settingsLoaded;
    private CountryOption selectedCountry;
    private bool killSwitchEnabled = true;
    private bool allowLocalNetwork;
    private BridgeOption selectedBridges;
    private string customBridgesText = string.Empty;
    private bool settingsDirty;

    public MainViewModel()
    {
        Countries =
        [
            new CountryOption(null, "Automatique (au choix de Tor)"),
            .. ExitCountries.Codes
                .Select(code => new CountryOption(code, StatusPresenter.CountryName(code)))
                .OrderBy(option => option.Label, StringComparer.CurrentCulture),
        ];
        selectedCountry = Countries[0];
        BridgeOptions =
        [
            new BridgeOption(BridgeMode.None, "Aucun pont (connexion directe à Tor)"),
            new BridgeOption(BridgeMode.BuiltIn, "Ponts obfs4 intégrés"),
            new BridgeOption(BridgeMode.Custom, "Ponts obfs4 personnalisés"),
        ];
        selectedBridges = BridgeOptions[0];
        timer = new DispatcherTimer { Interval = PollInterval };
        timer.Tick += async (_, _) => await RefreshAsync();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Title => view.Title;

    public string Detail => message ?? view.Detail;

    public string ButtonLabel => view.ButtonLabel;

    public bool CanAct => !busy && view.Action != MainAction.None;

    /// <summary>État du service (null s'il ne répond pas), pour l'icône près de l'horloge.</summary>
    public ConnectionState? State => state;

    public bool ShowProgress => view.ShowProgress;

    public int Progress => view.Progress;

    public bool CanNewIdentity => !busy && state == ConnectionState.Connected;

    public bool IsConnected => state is ConnectionState.Connected or ConnectionState.Connecting;

    public IReadOnlyList<CountryOption> Countries { get; }

    public bool CanEditSettings => settingsLoaded && !busy;

    public bool CanApplySettings => CanEditSettings && settingsDirty;

    public CountryOption SelectedCountry
    {
        get => selectedCountry;
        set => EditSetting(ref selectedCountry, value);
    }

    public bool KillSwitchEnabled
    {
        get => killSwitchEnabled;
        set => EditSetting(ref killSwitchEnabled, value);
    }

    public bool AllowLocalNetwork
    {
        get => allowLocalNetwork;
        set => EditSetting(ref allowLocalNetwork, value);
    }

    public bool KillSwitchWarning => !killSwitchEnabled;

    public IReadOnlyList<BridgeOption> BridgeOptions { get; }

    public BridgeOption SelectedBridges
    {
        get => selectedBridges;
        set => EditSetting(ref selectedBridges, value);
    }

    public bool ShowCustomBridges => selectedBridges.Mode == BridgeMode.Custom;

    /// <summary>Une ligne obfs4 par ligne, collées depuis bridges.torproject.org.</summary>
    public string CustomBridgesText
    {
        get => customBridgesText;
        set => EditSetting(ref customBridgesText, value);
    }

    /// <summary>Écran d'accueil (grand logo) jusqu'à la première réponse du service, au moins 1,2 s.</summary>
    public bool ShowSplash => splash;

    public bool ShowMain => !splash;

    public void Start()
    {
        timer.Start();
        _ = StartAsync();
    }

    private async Task StartAsync()
    {
        await Task.WhenAll(RefreshAsync(), Task.Delay(TimeSpan.FromSeconds(1.2)));
        splash = false;
        NotifyAll();
    }

    public void Stop() => timer.Stop();

    public Task ActAsync() => view.Action switch
    {
        MainAction.Connect => SendAsync(new ServiceRequest(ServiceCommand.Connect)),
        MainAction.Disconnect => SendAsync(new ServiceRequest(ServiceCommand.Disconnect)),
        _ => Task.CompletedTask,
    };

    public async Task NewIdentityAsync()
    {
        if (await SendAsync(new ServiceRequest(ServiceCommand.NewIdentity)))
        {
            message = "Nouvelle identité demandée : les prochaines connexions changent de sortie.";
            NotifyAll();
        }
    }

    /// <summary>Renvoie vrai s'il faut proposer de se reconnecter pour appliquer les réglages.</summary>
    public async Task<bool> ApplySettingsAsync()
    {
        var settings = new UserSettings
        {
            ExitCountries = selectedCountry.Code is null ? [] : [selectedCountry.Code],
            KillSwitchEnabled = killSwitchEnabled,
            AllowLocalNetwork = allowLocalNetwork,
            Bridges = selectedBridges.Mode,
            CustomBridges = customBridgesText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        };
        if (!await SendAsync(new ServiceRequest(ServiceCommand.SetSettings, settings)))
        {
            return false;
        }
        settingsDirty = false;
        message = IsConnected ? "Réglages enregistrés : ils s'appliqueront à la prochaine connexion." : "Réglages enregistrés.";
        NotifyAll();
        return IsConnected;
    }

    public async Task ReconnectAsync()
    {
        if (await SendAsync(new ServiceRequest(ServiceCommand.Disconnect)))
        {
            await SendAsync(new ServiceRequest(ServiceCommand.Connect));
        }
    }

    /// <summary>Envoie une commande ; renvoie vrai si le service l'a acceptée.</summary>
    private async Task<bool> SendAsync(ServiceRequest request)
    {
        if (busy)
        {
            return false;
        }
        busy = true;
        message = null;
        NotifyAll();
        try
        {
            var response = await IpcClient.SendAsync(request, CommandTimeout);
            message = response.Error;
            Show(response.Status, response.Settings);
            return response.Ok;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
        {
            message = ex.Message;
            return false;
        }
        finally
        {
            busy = false;
            NotifyAll();
        }
    }

    private async Task RefreshAsync()
    {
        if (polling)
        {
            return;
        }
        polling = true;
        try
        {
            var response = await IpcClient.SendAsync(new ServiceRequest(ServiceCommand.GetStatus), PollTimeout);
            Show(response.Status, response.Settings);
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
        {
            Show(null, null);
        }
        finally
        {
            polling = false;
        }
    }

    private void Show(ServiceStatus? status, UserSettings? settings)
    {
        if (status is null)
        {
            // Service absent : les réglages seront relus à son retour.
            settingsLoaded = false;
        }
        else if (settings is not null && !settingsLoaded)
        {
            LoadSettings(settings);
        }

        var next = StatusPresenter.Present(status);
        if (next != view || status?.State != state)
        {
            // Un message reste affiché jusqu'au prochain changement d'affichage (état, pays…).
            message = null;
            view = next;
            state = status?.State;
            NotifyAll();
        }
    }

    private void LoadSettings(UserSettings settings)
    {
        var code = settings.ExitCountries.FirstOrDefault();
        selectedCountry = Countries.FirstOrDefault(c => c.Code == code) ?? Countries[0];
        killSwitchEnabled = settings.KillSwitchEnabled;
        allowLocalNetwork = settings.AllowLocalNetwork;
        selectedBridges = BridgeOptions.FirstOrDefault(b => b.Mode == settings.Bridges) ?? BridgeOptions[0];
        customBridgesText = string.Join(Environment.NewLine, settings.CustomBridges);
        settingsLoaded = true;
        settingsDirty = false;
        NotifyAll();
    }

    private void EditSetting<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }
        field = value;
        settingsDirty = true;
        NotifyAll();
    }

    // Une chaîne vide signale à WPF que toutes les propriétés ont pu changer.
    private void NotifyAll() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
}
