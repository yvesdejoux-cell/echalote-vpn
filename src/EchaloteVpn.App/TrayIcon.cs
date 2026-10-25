using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using EchaloteVpn.Core.Ipc;
using Forms = System.Windows.Forms;

namespace EchaloteVpn.App;

/// <summary>
/// Icône près de l'horloge : sa couleur suit l'état, son menu ouvre la fenêtre, connecte ou
/// déconnecte, et quitte l'interface (le service, lui, continue).
/// </summary>
public sealed partial class TrayIcon : IDisposable
{
    private readonly MainViewModel viewModel;
    private readonly Forms.NotifyIcon icon;
    private readonly Forms.ToolStripMenuItem actionItem;
    private readonly Dictionary<Color, Icon> icons = [];
    private readonly Bitmap logo = LoadLogo();

    public TrayIcon(MainViewModel viewModel, Action open, Action quit)
    {
        this.viewModel = viewModel;
        actionItem = new Forms.ToolStripMenuItem("Connecter", null, async (_, _) => await viewModel.ActAsync());
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(new Forms.ToolStripMenuItem("Ouvrir Échalote VPN", null, (_, _) => open()) { Font = new Font(Forms.Control.DefaultFont, FontStyle.Bold) });
        menu.Items.Add(actionItem);
        if (Donation.IsAvailable)
        {
            menu.Items.Add(new Forms.ToolStripMenuItem("Soutenir le projet (Ko-fi)…", null, (_, _) => Donation.Open()));
        }
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(new Forms.ToolStripMenuItem("Quitter l'interface", null, (_, _) => quit()));

        icon = new Forms.NotifyIcon { ContextMenuStrip = menu, Visible = true };
        icon.DoubleClick += (_, _) => open();
        viewModel.PropertyChanged += OnChanged;
        Update();
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e) => Update();

    private void Update()
    {
        var color = viewModel.State switch
        {
            ConnectionState.Connected => Color.FromArgb(46, 160, 67),
            ConnectionState.Connecting or ConnectionState.Disconnecting => Color.FromArgb(230, 145, 30),
            ConnectionState.Error => Color.FromArgb(207, 34, 46),
            _ => Color.FromArgb(140, 140, 140),
        };
        icon.Icon = IconFor(color);
        // Une info-bulle de NotifyIcon ne dépasse pas 63 caractères.
        var text = $"Échalote VPN : {viewModel.Title}";
        icon.Text = text.Length <= 63 ? text : text[..63];
        actionItem.Text = viewModel.ButtonLabel;
        actionItem.Enabled = viewModel.CanAct;
    }

    private static Bitmap LoadLogo()
    {
        using var stream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/logo-64.png")).Stream;
        return new Bitmap(stream);
    }

    /// <summary>Le bouclier avec une pastille de couleur, dessiné une fois par couleur.</summary>
    private Icon IconFor(Color color)
    {
        if (icons.TryGetValue(color, out var cached))
        {
            return cached;
        }
        using var bitmap = new Bitmap(32, 32);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(logo, 0, 0, 30, 30);
            using var fill = new SolidBrush(color);
            using var border = new Pen(Color.FromArgb(30, 31, 35), 2);
            graphics.FillEllipse(fill, 18, 18, 13, 13);
            graphics.DrawEllipse(border, 18, 18, 13, 13);
        }
        var handle = bitmap.GetHicon();
        try
        {
            // Copie gérée : le handle natif peut être libéré tout de suite.
            var result = (Icon)Icon.FromHandle(handle).Clone();
            icons[color] = result;
            return result;
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    public void Dispose()
    {
        viewModel.PropertyChanged -= OnChanged;
        icon.Visible = false;
        icon.Dispose();
        foreach (var cached in icons.Values)
        {
            cached.Dispose();
        }
        logo.Dispose();
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(nint handle);
}
