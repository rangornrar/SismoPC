using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SismoPC.Core.Model;
using SismoPC.Data;
using SismoPC.Ui.Controls;
using SismoPC.Ui.Services;

namespace SismoPC.Ui.Pages;

/// <summary>Chronologie : courbes superposées de 1 heure à 90 jours, épisodes, programmes à un instant (US3, US9).</summary>
public sealed partial class TimelinePage : Page
{
    private static readonly (string Label, TimeSpan Span)[] Ranges =
    [
        ("1 h", TimeSpan.FromHours(1)),
        ("24 h", TimeSpan.FromHours(24)),
        ("7 j", TimeSpan.FromDays(7)),
        ("30 j", TimeSpan.FromDays(30)),
        ("90 j", TimeSpan.FromDays(90)),
    ];

    private static readonly (MetricKind Metric, string Name, string Unit, double Min, double Max)[] Rows =
    [
        (MetricKind.CpuPercent, "Processeur", "%", 0, 100),
        (MetricKind.MemoryUsedPercent, "Mémoire", "%", 0, 100),
        (MetricKind.DiskActivePercent, "Disque", "% actif", 0, 100),
        (MetricKind.CpuTemperatureC, "Température processeur", "°C", 30, 100),
    ];

    private readonly List<LineChart> _charts = [];
    private readonly TextBlock _periodLabel = UiKit.Text(string.Empty, "BodyStrongTextBlockStyle");
    private readonly StackPanel _chartsPanel = new() { Spacing = 18 };
    private readonly StackPanel _episodesPanel = new() { Spacing = 4 };
    private readonly StackPanel _instantPanel = new() { Spacing = 6 };
    private TimeSpan _span = TimeSpan.FromHours(24);
    private DateTimeOffset _to = DateTimeOffset.UtcNow;

    public TimelinePage()
    {
        InitializeComponent();
        BuildLayout();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        // Depuis le détail d'un épisode : centrer la chronologie sur son instant.
        if (e.Parameter is DateTimeOffset at)
        {
            _span = TimeSpan.FromHours(1);
            _to = at.AddMinutes(30);
        }

        await LoadAsync();
    }

    private void BuildLayout()
    {
        Root.Children.Add(UiKit.Header("Chronologie", "Remonte le temps : chaque courbe, chaque épisode, chaque changement sur le PC"));

        var bar = new SelectorBar();
        foreach (var (label, span) in Ranges)
        {
            var item = new SelectorBarItem { Text = label, Tag = span };
            bar.Items.Add(item);
            if (span == _span)
            {
                bar.SelectedItem = item;
            }
        }

        bar.SelectionChanged += async (s, _) =>
        {
            if (s.SelectedItem?.Tag is TimeSpan span)
            {
                _span = span;
                _to = DateTimeOffset.UtcNow;
                await LoadAsync();
            }
        };

        var previous = new Button { Content = new FontIcon { Glyph = "", FontSize = 14 } };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(previous, "Période précédente");
        previous.Click += async (_, _) => { _to -= _span; await LoadAsync(); };
        var next = new Button { Content = new FontIcon { Glyph = "", FontSize = 14 } };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(next, "Période suivante");
        next.Click += async (_, _) => { _to = Min(_to + _span, DateTimeOffset.UtcNow); await LoadAsync(); };

        var nav = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        nav.Children.Add(previous);
        nav.Children.Add(_periodLabel);
        nav.Children.Add(next);
        _periodLabel.VerticalAlignment = VerticalAlignment.Center;

        var toolbar = new Grid();
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(nav, 1);
        toolbar.Children.Add(bar);
        toolbar.Children.Add(nav);
        Root.Children.Add(toolbar);

        foreach (var row in Rows)
        {
            var chart = new LineChart();
            chart.TimeSelected += async (_, at) => await ShowInstantAsync(at);
            _charts.Add(chart);

            var grid = new Grid { ColumnSpacing = 16 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var label = UiKit.Stack(0, UiKit.Text(row.Name, "BodyStrongTextBlockStyle"), UiKit.Text(row.Unit, "CaptionTextBlockStyle", secondary: true));
            Grid.SetColumn(chart, 1);
            grid.Children.Add(label);
            grid.Children.Add(chart);
            _chartsPanel.Children.Add(grid);
        }

        _chartsPanel.Children.Add(UiKit.Text("Clique sur une courbe pour voir les programmes actifs à cet instant.", "CaptionTextBlockStyle", secondary: true));
        Root.Children.Add(UiKit.Card(_chartsPanel));
        Root.Children.Add(UiKit.Card(_instantPanel));
        Root.Children.Add(UiKit.Card(UiKit.Stack(8, UiKit.SectionTitle("Épisodes et événements de la période"), _episodesPanel)));
        _instantPanel.Children.Add(UiKit.SectionTitle("Programmes à un instant"));
        _instantPanel.Children.Add(UiKit.Text("Aucun instant choisi.", secondary: true));
    }

    private async Task LoadAsync()
    {
        var from = _to - _span;
        _periodLabel.Text = _span <= TimeSpan.FromHours(24)
            ? $"{from.ToLocalTime().ToString("ddd d MMM HH:mm", UiKit.Fr)} → {_to.ToLocalTime():HH:mm}"
            : $"{from.ToLocalTime().ToString("d MMM", UiKit.Fr)} → {_to.ToLocalTime().ToString("d MMM", UiKit.Fr)}";

        var data = await DataAccess.ReadAsync(store => (
            Series: Rows.Select(r => store.ReadSeries(r.Metric, from, _to)).ToList(),
            Episodes: store.ReadEpisodes(from, _to),
            Events: store.ReadSystemEvents(from, _to)));

        if (data.Series is null)
        {
            _episodesPanel.Children.Clear();
            _episodesPanel.Children.Add(UiKit.Text("Pas encore de données : le service SismoPC n'a rien enregistré sur cette période.", secondary: true));
            return;
        }

        var bands = data.Episodes.Select(ep => (ep.Episode.Start, ep.Episode.End)).ToList();
        for (var i = 0; i < Rows.Length; i++)
        {
            var points = data.Series[i].Select(p => new ChartPoint(p.Timestamp, p.Max)).ToList();
            var max = Rows[i].Metric == MetricKind.CpuTemperatureC && points.Count > 0
                ? Math.Max(Rows[i].Max, points.Max(p => p.Value) + 5)
                : Rows[i].Max;
            _charts[i].SetData(points, from, _to, Rows[i].Min, max, i, bands);
        }

        _episodesPanel.Children.Clear();
        foreach (var ep in data.Episodes)
        {
            _episodesPanel.Children.Add(HomePage.EpisodeRow(ep, this));
        }

        foreach (var ev in data.Events)
        {
            var label = ev.Kind == "boot" && ev.Value is { } s ? $"{ev.Label} ({s:0} s)" : ev.Label;
            _episodesPanel.Children.Add(UiKit.Text($"{UiKit.When(ev.At)} · {label}", "CaptionTextBlockStyle", secondary: true));
        }

        if (_episodesPanel.Children.Count == 0)
        {
            _episodesPanel.Children.Add(UiKit.Text("Aucun épisode sur cette période.", secondary: true));
        }
    }

    private async Task ShowInstantAsync(DateTimeOffset at)
    {
        foreach (var chart in _charts)
        {
            chart.SetCursor(at);
        }

        var processes = await DataAccess.ReadAsync(store => store.ReadProcessesAt(at)) ?? [];
        _instantPanel.Children.Clear();
        _instantPanel.Children.Add(UiKit.SectionTitle($"À {at.ToLocalTime():HH:mm}"));
        if (processes.Count == 0)
        {
            _instantPanel.Children.Add(UiKit.Text("Le détail par programme n'est conservé que 7 jours.", secondary: true));
            return;
        }

        _instantPanel.Children.Add(UiKit.Text("Programmes les plus gourmands à cet instant", "CaptionTextBlockStyle", secondary: true));
        foreach (var p in processes.Take(8))
        {
            _instantPanel.Children.Add(UiKit.Field(p.Name,
                $"Processeur {UiKit.Percent(p.CpuPercent)} · Mémoire {p.WorkingSetBytes / 1e9:0.0} Go · Disque {p.DiskBytesPerSecond / 1e6:0.0} Mo/s"));
        }
    }

    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;
}
