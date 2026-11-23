using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SismoPC.Core.Ipc;
using SismoPC.Core.Model;
using SismoPC.Data;
using SismoPC.Ui.Services;

namespace SismoPC.Ui.Pages;

public sealed partial class HomePage : Page
{
    private sealed record HomeData(IReadOnlyList<HardwareAlert> Alerts, IReadOnlyList<StoredEpisode> Episodes,
        IReadOnlyList<(DateTimeOffset At, string Kind, string Label, double? Value)> Marks, int PreviousCount);
    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromSeconds(10) };
    private bool _loading;
    private bool _active;
    private HomeData? _data;
    private LiveStatus? _status;
    private double _lastWidth;

    public HomePage()
    {
        InitializeComponent();
        _refresh.Tick += async (_, _) => await RefreshAsync();
        Root.SizeChanged += (_, _) =>
        {
            if (Math.Abs(Root.ActualWidth - _lastWidth) < 2) return;
            _lastWidth = Root.ActualWidth;
            if (_active && _data is not null) Render();
        };
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _active = true;
        _refresh.Start();
        await RefreshAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _active = false;
        _refresh.Stop();
        base.OnNavigatedFrom(e);
    }

    private async Task RefreshAsync()
    {
        if (_loading || !_active) return;
        _loading = true;
        try
        {
            if (Root.Children.Count == 0) Root.Children.Add(UiKit.Text("Lecture de l'état du PC…"));
            var liveTask = ServiceClient.GetStatusAsync();
            _data = await DataAccess.ReadAsync(store =>
            {
                var now = DateTimeOffset.UtcNow;
                return new HomeData(store.ReadOpenAlerts(), store.ReadEpisodes(now.AddDays(-7), now),
                    store.ReadSystemEvents(now.AddDays(-7), now).Where(e => e.Kind == "manual").OrderByDescending(e => e.At).Take(3).ToList(),
                    store.ReadEpisodes(now.AddDays(-14), now.AddDays(-7)).Count);
            });
            _status = await liveTask;
            if (_status is not null && DateTimeOffset.UtcNow - _status.At > TimeSpan.FromSeconds(60)) _status = null;
            if (_active) Render();
        }
        finally { _loading = false; }
    }

    private void Render()
    {
        var content = UiKit.Stack(20);
        content.Children.Add(UiKit.Header("Votre PC, en clair", "L'essentiel pour comprendre la situation et retrouver ce qui s'est passé."));
        var mark = new Button { Content = "Mon PC vient de ralentir", Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        mark.Click += (_, _) => App.Window?.Navigate("incident");
        var report = new Button { Content = "Créer un bilan" };
        report.Click += (_, _) => App.Window?.Navigate("reports");
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        actions.Children.Add(mark); actions.Children.Add(report);
        content.Children.Add(actions);

        var alert = _data?.Alerts.OrderByDescending(a => a.Severity).FirstOrDefault();
        var partial = _status?.ScoreLabel.StartsWith("Surveillance partielle", StringComparison.Ordinal) == true;
        var title = _status is null ? "La collecte ne répond pas" : alert is not null ? "Un point mérite votre attention" : partial ? "La surveillance est partielle" : "Aucune alerte matérielle détectée";
        var state = _status is null || partial ? Status.Info : alert is null ? Status.Good : alert.Severity == AlertSeverity.Critical ? Status.Critical : Status.Warning;
        var summary = UiKit.Stack(10,
            UiKit.Pill(_status is null ? "À vérifier" : alert is not null ? "À examiner" : partial ? "Données incomplètes" : "Situation stable", state, this),
            UiKit.Text(title, "TitleTextBlockStyle"),
            UiKit.Text(_status is null ? "L'historique reste consultable. Vérifiez le service de collecte dans les paramètres."
                : alert?.Message ?? (partial ? "Certains capteurs ne sont pas disponibles. Un état inconnu n'est pas une confirmation de bonne santé."
                : "SismoPC veille en arrière-plan. Vous pouvez consulter les incidents récents ou signaler un ralentissement.")));
        var next = new HyperlinkButton { Content = _status is null ? "Vérifier la collecte" : alert is not null ? "Examiner ce point" : partial ? "Vérifier les capteurs" : "Voir le matériel", Padding = new Thickness(0) };
        next.Click += (_, _) => App.Window?.Navigate(_status is null || (partial && alert is null) ? "settings" : "hardware");
        summary.Children.Add(next);
        if (_status is { Learning: true } live)
        {
            var days = Math.Max(1, (int)Math.Ceiling((live.LearningEndsAt - DateTimeOffset.UtcNow).TotalDays));
            summary.Children.Add(UiKit.Text($"Analyse personnalisée en apprentissage : encore {days} jours. Les alertes matérielles et les ralentissements sont déjà suivis.", "CaptionTextBlockStyle", secondary: true));
        }
        var hero = UiKit.Card(summary);
        hero.BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Palette.Accent(this));
        content.Children.Add(hero);

        content.Children.Add(RecentCard());
        var metrics = new List<UIElement>();
        foreach (var (metric, label, unit) in new[] { (MetricKind.CpuPercent,"Processeur","%"), (MetricKind.MemoryUsedPercent,"Mémoire utilisée","%"), (MetricKind.DiskActivePercent,"Disque actif","%"), (MetricKind.CpuTemperatureC,"Température CPU","°C") })
        {
            var value = _status is not null && _status.Metrics.TryGetValue(metric.ToString(), out var v) ? $"{v:0} {unit}" : "—";
            metrics.Add(UiKit.Card(UiKit.Stack(6, UiKit.Text(label, "CaptionTextBlockStyle", secondary: true), UiKit.Text(value, "SubtitleTextBlockStyle"))));
        }
        content.Children.Add(UiKit.Stack(10, UiKit.SectionTitle("En ce moment"), UiKit.Columns(Root.ActualWidth >= 720 ? 4 : 2, metrics, 12),
            UiKit.Text(_status is null ? "Mesures indisponibles ou périmées" : $"Dernier relevé : {_status.At.ToLocalTime():HH:mm:ss} · Mise à jour discrète toutes les 10 secondes", "CaptionTextBlockStyle", secondary: true)));
        var details = new HyperlinkButton { Content = "Comprendre le score et les limites de la surveillance", Padding = new Thickness(0) };
        details.Click += (_, _) => App.Window?.Navigate("settings");
        content.Children.Add(details);
        LiveRefresh.Update(Root, content);
    }

    private Border RecentCard()
    {
        var panel = UiKit.Stack(8, UiKit.SectionTitle("À retenir cette semaine"));
        if (_data is null) panel.Children.Add(UiKit.Text("L'historique n'est pas encore disponible.", secondary: true));
        else
        {
            panel.Children.Add(UiKit.Text($"{_data.Episodes.Count} épisode{(_data.Episodes.Count > 1 ? "s" : "")} enregistré{(_data.Episodes.Count > 1 ? "s" : "")} sur 7 jours · {_data.PreviousCount} les 7 jours précédents.", "CaptionTextBlockStyle", secondary: true));
            foreach (var mark in _data.Marks)
            {
                var button = new Button { Content = $"Votre signalement · {UiKit.When(mark.At)}", Tag = mark.At, HorizontalAlignment = HorizontalAlignment.Stretch };
                button.Click += (_, _) => App.Window?.Navigate("incident", mark.At);
                panel.Children.Add(button);
            }
            foreach (var episode in _data.Episodes.Take(3)) panel.Children.Add(EpisodeRow(episode, this));
            if (_data.Episodes.Count == 0 && _data.Marks.Count == 0)
                panel.Children.Add(UiKit.Text("Aucun épisode enregistré cette semaine. Cela ne garantit pas l'absence d'incident si la collecte était interrompue.", secondary: true));
        }
        var all = new HyperlinkButton { Content = "Explorer la chronologie", Padding = new Thickness(0) };
        all.Click += (_, _) => App.Window?.Navigate("timeline");
        panel.Children.Add(all);
        return UiKit.Card(panel);
    }

    /// <summary>Ligne cliquable d'un épisode, partagée avec la chronologie.</summary>
    internal static Button EpisodeRow(StoredEpisode ep, FrameworkElement themeSource)
    {
        var grid = new Grid { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var when = UiKit.Text(UiKit.When(ep.Episode.Start), "CaptionTextBlockStyle", secondary: true);
        var what = UiKit.Stack(0, UiKit.Text(ep.Title, "BodyStrongTextBlockStyle"));
        if (ep.Episode.Culprit is { } culprit)
        {
            what.Children.Add(UiKit.Text($"Programme contributeur : {culprit}", "CaptionTextBlockStyle", secondary: true));
        }

        var pill = UiKit.Pill(ep.Episode.Kind == EpisodeKind.Anomaly ? "Anomalie" : "Ralentissement",
            ep.Episode.Kind == EpisodeKind.Anomaly ? Status.Info : Status.Warning, themeSource);
        Grid.SetColumn(what, 1);
        Grid.SetColumn(pill, 2);
        grid.Children.Add(when);
        grid.Children.Add(what);
        grid.Children.Add(pill);

        var button = new Button
        {
            Tag = ep.Id,
            Content = grid,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(4, 10, 4, 10),
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, $"{ep.Title}, {UiKit.When(ep.Episode.Start)}");
        button.Click += (_, _) => App.Window?.Navigate("episode", ep.Id);
        return button;
    }
}
