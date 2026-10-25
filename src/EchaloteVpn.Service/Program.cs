using EchaloteVpn.Service;
using EchaloteVpn.Service.Firewall;
using EchaloteVpn.Service.Ipc;
using EchaloteVpn.Service.Tor;
using EchaloteVpn.Service.Tunnel;

if (args.Contains(NetworkReset.Argument))
{
    return NetworkReset.Run();
}

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options => options.ServiceName = "EchaloteVpn");
// Sous SYSTEM, sans console : le journal va dans l'observateur d'événements (Application, source
// EchaloteVpn). Les lignes de Tor et de tun2socks n'y vont qu'à partir des avertissements.
builder.Services.Configure<Microsoft.Extensions.Logging.EventLog.EventLogSettings>(settings => settings.SourceName = "EchaloteVpn");
builder.Services.AddSingleton(new TorOptions());
builder.Services.AddSingleton<TorProcessManager>();
builder.Services.AddSingleton(new TunnelOptions());
builder.Services.AddSingleton<Tun2SocksProcess>();
builder.Services.AddSingleton<RouteManager>();
builder.Services.AddSingleton<DnsPolicy>();
builder.Services.AddSingleton<TunnelSession>();
builder.Services.AddSingleton<KillSwitch>();
builder.Services.AddSingleton<ConnectionStatus>();
builder.Services.AddSingleton<StateStore>();
builder.Services.AddSingleton<ConnectionController>();
builder.Services.AddHostedService(services => services.GetRequiredService<ConnectionController>());
builder.Services.AddHostedService<PipeServer>();

var host = builder.Build();
host.Run();
return 0;
