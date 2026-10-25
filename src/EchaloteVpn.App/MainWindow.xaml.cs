using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace EchaloteVpn.App;

public partial class MainWindow : Window
{
    // Barre de titre sombre (Windows 10 2004 et suivants), assortie au thème.
    private const int DwmwaUseImmersiveDarkMode = 20;

    private bool closing;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = ViewModel;
        DonationLink.Visibility = Donation.IsAvailable ? Visibility.Visible : Visibility.Collapsed;
        SourceInitialized += (_, _) =>
        {
            var dark = 1;
            DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));
        };
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);

    public MainViewModel ViewModel { get; } = new();

    /// <summary>Fermer la fenêtre la range près de l'horloge ; seul « Quitter » la ferme vraiment.</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!closing)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnClosing(e);
    }

    public void CloseForReal()
    {
        closing = true;
        Close();
    }

    private async void MainButton_Click(object sender, RoutedEventArgs e) => await ViewModel.ActAsync();

    private void Donation_Click(object sender, RoutedEventArgs e) => Donation.Open();

    private async void NewIdentity_Click(object sender, RoutedEventArgs e) => await ViewModel.NewIdentityAsync();

    private async void ApplySettings_Click(object sender, RoutedEventArgs e)
    {
        if (await ViewModel.ApplySettingsAsync()
            && MessageBox.Show(
                this,
                "Les nouveaux réglages s'appliquent à la prochaine connexion. Se reconnecter maintenant ?",
                "EchaloteVpn",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            await ViewModel.ReconnectAsync();
        }
    }
}
