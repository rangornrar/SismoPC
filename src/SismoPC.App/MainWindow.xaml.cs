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
        if (Environment.GetEnvironmentVariable("SISMOPC_DATA_DIRECTORY") is not null) Title += " · environnement d’essai";
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1280, 860));
        var icon = Path.Combine(AppContext.BaseDirectory, "SismoPC.ico");
        if (File.Exists(icon))
        {
            AppWindow.SetIcon(icon);
        }

        ContentFrame.Navigated += (_, e) =>
        {
            Nav.IsBackEnabled = ContentFrame.CanGoBack;
            var tag = e.SourcePageType == typeof(HomePage) ? "home" : e.SourcePageType == typeof(TimelinePage) ? "timeline"
                : e.SourcePageType == typeof(HardwarePage) ? "hardware" : e.SourcePageType == typeof(TrendsPage) ? "trends"
                : e.SourcePageType == typeof(SettingsPage) ? "settings" : e.SourcePageType == typeof(ReportsPage) ? "reports" : null;
            Nav.SelectedItem = Nav.MenuItems.Concat(Nav.FooterMenuItems).OfType<NavigationViewItem>().FirstOrDefault(item => Equals(item.Tag, tag));
        };
        Nav.SelectedItem = Nav.MenuItems[0];
    }

    /// <summary>Ouvre une page : home, timeline, hardware, trends, settings ou episode (avec son identifiant).</summary>
    public void Navigate(string tag, object? parameter = null)
    {
        var page = tag switch
        {
            "incident" => typeof(IncidentPage),
            "reports" => typeof(ReportsPage),
            "timeline" => typeof(TimelinePage),
            "hardware" => typeof(HardwarePage),
            "trends" => typeof(TrendsPage),
            "settings" => typeof(SettingsPage),
            "episode" => typeof(EpisodePage),
            _ => typeof(HomePage),
        };
        if (ContentFrame.CurrentSourcePageType != page || parameter is not null)
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
        if (parts.Length == 1 && parts[0] is "reports" or "incident" or "home") { Navigate(parts[0]); return; }
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
