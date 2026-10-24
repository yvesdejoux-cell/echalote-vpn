using System.Threading;
using System.Windows;

namespace OnionGuard.App;

/// <summary>
/// Une seule interface par session : un second lancement fait réapparaître la fenêtre existante.
/// « --tray » démarre directement près de l'horloge (lancement à l'ouverture de session).
/// </summary>
public partial class App : Application
{
    private const string InstanceName = @"Local\OnionGuard.App";
    private const string ShowEventName = @"Local\OnionGuard.App.Show";

    private Mutex? instance;
    private EventWaitHandle? showRequested;
    private TrayIcon? tray;
    private MainWindow? window;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        instance = new Mutex(initiallyOwned: true, InstanceName, out var first);
        if (!first)
        {
            using var show = EventWaitHandle.OpenExisting(ShowEventName);
            show.Set();
            Shutdown();
            return;
        }

        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        window = new MainWindow();
        tray = new TrayIcon(window.ViewModel, ShowWindow, Quit);

        showRequested = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        ThreadPool.RegisterWaitForSingleObject(showRequested, (_, _) => Dispatcher.BeginInvoke(ShowWindow), null, Timeout.Infinite, executeOnlyOnce: false);

        window.ViewModel.Start();
        if (!e.Args.Contains("--tray"))
        {
            ShowWindow();
        }
    }

    private void ShowWindow()
    {
        if (window is null)
        {
            return;
        }
        window.Show();
        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }
        window.Activate();
    }

    private void Quit()
    {
        window?.CloseForReal();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        tray?.Dispose();
        window?.ViewModel.Stop();
        showRequested?.Dispose();
        instance?.Dispose();
        base.OnExit(e);
    }
}
