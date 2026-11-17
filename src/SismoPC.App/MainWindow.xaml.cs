using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SismoPC.Ui.Pages;

namespace SismoPC.Ui;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1280, 860));
        var icon = Path.Combine(AppContext.BaseDirectory, "SismoPC.ico");
        if (File.Exists(icon))
        {
            AppWindow.SetIcon(icon);
        }

        ContentFrame.Navigated += (_, _) => Nav.IsBackEnabled = ContentFrame.CanGoBack;
        Nav.SelectedItem = Nav.MenuItems[0];
    }

    /// <summary>Ouvre une page : home, timeline, hardware, trends, settings ou episode (avec son identifiant).</summary>
    public void Navigate(string tag, object? parameter = null)
    {
        var page = tag switch
        {
            "timeline" => typeof(TimelinePage),
            "hardware" => typeof(HardwarePage),
            "trends" => typeof(TrendsPage),
            "settings" => typeof(SettingsPage),
            "episode" => typeof(EpisodePage),
            _ => typeof(HomePage),
        };
        ContentFrame.Navigate(page, parameter);
    }

    /// <summary>Gère « sismopc://episode/42 » et « sismopc://home » passés en ligne de commande.</summary>
    public void NavigateFromArguments(IEnumerable<string> args)
    {
        var uri = args.FirstOrDefault(a => a.StartsWith("sismopc://", StringComparison.OrdinalIgnoreCase));
        if (uri is null)
        {
            return;
        }

        var parts = uri["sismopc://".Length..].Trim('/').Split('/');
        if (parts.Length == 2 && parts[0] == "episode"
            && long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var id))
        {
            Navigate("episode", id);
        }
    }

    private void OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer?.Tag is string tag)
        {
            Navigate(tag);
        }
    }

    private void OnBackRequested(NavigationView sender, NavigationViewBackRequestedEventArgs args)
    {
        if (ContentFrame.CanGoBack)
        {
            ContentFrame.GoBack();
        }
    }
}
