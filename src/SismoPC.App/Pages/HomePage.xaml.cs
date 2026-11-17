using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SismoPC.Core.Ipc;
using SismoPC.Core.Model;
using SismoPC.Data;
using SismoPC.Ui.Services;

namespace SismoPC.Ui.Pages;

/// <summary>Accueil : score de santé, composants, alertes en cours, derniers épisodes (US4, US5, US6).</summary>
public sealed partial class HomePage : Page
{
    private sealed record HomeData(
        IReadOnlyList<HardwareAlert> Alerts,
        IReadOnlyList<StoredEpisode> Episodes,
        IReadOnlyList<DiskHealth> Disks,
        double? LastBootSeconds,
        string? System);

    public HomePage()
    {
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        Root.Children.Clear();
        Root.Children.Add(Ui.Header("Accueil", "Chargement…"));
        Root.Children.Add(new ProgressRing { IsActive = true, HorizontalAlignment = HorizontalAlignment.Left });

        var statusTask = ServiceClient.GetStatusAsync();
        var data = await DataAccess.ReadAsync(store =>
        {
            var now = DateTimeOffset.UtcNow;
            var boot = store.ReadSystemEvents(now.AddDays(-60), now).LastOrDefault(ev => ev.Kind == "boot");
            return new HomeData(
                store.ReadOpenAlerts(),
                store.ReadEpisodes(now.AddDays(-7), now).Take(5).ToList(),
                store.ReadLatestDiskHealth(),
                boot.Value,
                store.ReadComponents().FirstOrDefault(c => c.Type == "Système")?.Name);
        });
        var status = await statusTask;

        Render(status, data);
    }

    private void Render(LiveStatus? status, HomeData? data)
    {
        Root.Children.Clear();
        Root.Children.Add(Ui.Header("Accueil", data?.System ?? "SismoPC"));

        if (status is null && data is null)
        {
            Root.Children.Add(Ui.Banner(
                "Le service SismoPC ne répond pas",
                "La collecte ne tourne pas : réinstalle SismoPC ou démarre le service « SismoPC » dans les services Windows.",
                InfoBarSeverity.Error));
            return;
        }

        if (status is { Learning: true })
        {
            var days = Math.Max(1, (int)Math.Ceiling((status.LearningEndsAt - DateTimeOffset.UtcNow).TotalDays));
            Root.Children.Add(Ui.Banner(
                "Apprentissage de la normale",
                $"SismoPC apprend le comportement habituel de ce PC : encore {days} jour{(days > 1 ? "s" : "")} avant les premières alertes d'anomalie.",
                InfoBarSeverity.Informational));
        }

        var row = new Grid { ColumnSpacing = 20 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        var score = ScoreCard(status);
        var components = ComponentsCard(status, data);
        Grid.SetColumn(components, 1);
        row.Children.Add(score);
        row.Children.Add(components);
        Root.Children.Add(row);

        foreach (var alert in data?.Alerts ?? [])
        {
            Root.Children.Add(Ui.Banner(
                alert.Subject,
                alert.Message,
                alert.Severity == AlertSeverity.Critical ? InfoBarSeverity.Error : InfoBarSeverity.Warning));
        }

        Root.Children.Add(EpisodesCard(data?.Episodes ?? []));
    }

    private Border ScoreCard(LiveStatus? status)
    {
        var ring = new Grid { Width = 140, Height = 140 };
        ring.Children.Add(new ProgressRing
        {
            IsIndeterminate = false,
            Value = status?.Score ?? 0,
            Maximum = 100,
            Width = 140,
            Height = 140,
            Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Palette.Accent(this)),
        });
        var number = Ui.Stack(0,
            Ui.Text(status is null ? "—" : status.Score.ToString(Ui.Fr), "TitleLargeTextBlockStyle"),
            Ui.Text("sur 100", "CaptionTextBlockStyle", secondary: true));
        number.HorizontalAlignment = HorizontalAlignment.Center;
        number.VerticalAlignment = VerticalAlignment.Center;
        foreach (var child in number.Children.OfType<TextBlock>())
        {
            child.HorizontalAlignment = HorizontalAlignment.Center;
        }

        ring.Children.Add(number);

        var details = Ui.Stack(6, Ui.Text("SCORE DE SANTÉ", "CaptionTextBlockStyle", secondary: true));
        details.Children.Add(Ui.Text(status?.ScoreLabel ?? "Service indisponible", "SubtitleTextBlockStyle"));
        foreach (var penalty in status?.Penalties ?? [])
        {
            details.Children.Add(Ui.Text($"−{penalty.Points} : {penalty.Reason}", "CaptionTextBlockStyle", secondary: true));
        }

        var link = new HyperlinkButton { Content = "Comprendre le calcul", Padding = new Thickness(0) };
        link.Click += (_, _) => App.Window?.Navigate("settings");
        details.Children.Add(link);

        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 20 };
        panel.Children.Add(ring);
        panel.Children.Add(details);
        return Ui.Card(panel);
    }

    private Border ComponentsCard(LiveStatus? status, HomeData? data)
    {
        double? Metric(MetricKind kind) =>
            status is not null && status.Metrics.TryGetValue(kind.ToString(), out var v) ? v : null;

        var tiles = new List<UIElement>
        {
            Tile("Processeur",
                Describe(Metric(MetricKind.CpuTemperatureC), Metric(MetricKind.CpuPercent), "d'utilisation"),
                Metric(MetricKind.CpuTemperatureC) >= 95 ? Status.Critical : Status.Good),
            Tile("Carte graphique",
                Describe(Metric(MetricKind.GpuTemperatureC), Metric(MetricKind.GpuPercent), "d'utilisation"),
                Metric(MetricKind.GpuTemperatureC) >= 90 ? Status.Critical : Status.Good),
            Tile("Mémoire",
                Metric(MetricKind.MemoryUsedPercent) is { } mem ? $"{Ui.Percent(mem)} utilisée" : "—",
                Status.Good),
        };

        foreach (var disk in data?.Disks ?? [])
        {
            var parts = new List<string>();
            if (disk.WearPercent is { } wear)
            {
                parts.Add($"Usure {Ui.Percent(wear)}");
            }

            if (disk.TemperatureC is { } t)
            {
                parts.Add(Ui.Celsius(t));
            }

            var state = disk.PredictsFailure ? Status.Critical : disk.WearPercent >= 80 ? Status.Warning : Status.Good;
            tiles.Add(Tile(disk.Name, parts.Count > 0 ? string.Join(" · ", parts) : "Santé SMART", state));
        }

        foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady))
        {
            var free = 100.0 * drive.AvailableFreeSpace / drive.TotalSize;
            tiles.Add(Tile($"Lecteur {drive.Name.TrimEnd('\\')}", $"{Ui.Percent(free)} d'espace libre", free < 10 ? Status.Warning : Status.Good));
        }

        if (data?.LastBootSeconds is { } boot)
        {
            tiles.Add(Tile("Démarrage", $"{boot:0} s au dernier démarrage", Status.Good));
        }

        var panel = Ui.Stack(14, Ui.SectionTitle("Composants"), Ui.Columns(3, tiles, 10));
        return Ui.Card(panel);
    }

    private static string Describe(double? temperature, double? usage, string usageLabel)
    {
        var parts = new List<string>();
        if (temperature is { } t)
        {
            parts.Add(Ui.Celsius(t));
        }

        if (usage is { } u)
        {
            parts.Add($"{Ui.Percent(u)} {usageLabel}");
        }

        return parts.Count > 0 ? string.Join(" · ", parts) : "—";
    }

    private Border Tile(string name, string detail, Status status)
    {
        var label = status switch
        {
            Status.Critical => "Critique",
            Status.Warning => "Attention",
            _ => "OK",
        };
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = Ui.Text(name, "BodyStrongTextBlockStyle");
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        title.TextWrapping = TextWrapping.NoWrap;
        var pill = Ui.Pill(label, status, this);
        Grid.SetColumn(pill, 1);
        head.Children.Add(title);
        head.Children.Add(pill);

        return new Border
        {
            BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(14),
            Child = Ui.Stack(6, head, Ui.Text(detail, "CaptionTextBlockStyle", secondary: true)),
        };
    }

    private Border EpisodesCard(IReadOnlyList<StoredEpisode> episodes)
    {
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var all = new HyperlinkButton { Content = "Tout voir sur la chronologie" };
        all.Click += (_, _) => App.Window?.Navigate("timeline");
        Grid.SetColumn(all, 1);
        header.Children.Add(Ui.SectionTitle("Derniers épisodes"));
        header.Children.Add(all);

        var panel = Ui.Stack(4, header);
        if (episodes.Count == 0)
        {
            panel.Children.Add(Ui.Text("Aucun ralentissement ni anomalie ces 7 derniers jours.", secondary: true));
        }

        foreach (var ep in episodes)
        {
            panel.Children.Add(EpisodeRow(ep, this));
        }

        return Ui.Card(panel);
    }

    /// <summary>Ligne cliquable d'un épisode, partagée avec la chronologie.</summary>
    internal static Button EpisodeRow(StoredEpisode ep, FrameworkElement themeSource)
    {
        var grid = new Grid { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var when = Ui.Text(Ui.When(ep.Episode.Start), "CaptionTextBlockStyle", secondary: true);
        var what = Ui.Stack(0, Ui.Text(ep.Title, "BodyStrongTextBlockStyle"));
        if (ep.Episode.Culprit is { } culprit)
        {
            what.Children.Add(Ui.Text($"Responsable probable : {culprit}", "CaptionTextBlockStyle", secondary: true));
        }

        var pill = Ui.Pill(ep.Episode.Kind == EpisodeKind.Anomaly ? "Anomalie" : "Ralentissement",
            ep.Episode.Kind == EpisodeKind.Anomaly ? Status.Info : Status.Warning, themeSource);
        Grid.SetColumn(what, 1);
        Grid.SetColumn(pill, 2);
        grid.Children.Add(when);
        grid.Children.Add(what);
        grid.Children.Add(pill);

        var button = new Button
        {
            Content = grid,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(4, 10, 4, 10),
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, $"{ep.Title}, {Ui.When(ep.Episode.Start)}");
        button.Click += (_, _) => App.Window?.Navigate("episode", ep.Id);
        return button;
    }
}
