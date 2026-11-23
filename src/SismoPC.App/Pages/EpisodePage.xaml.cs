using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SismoPC.Core.Model;
using SismoPC.Core.Health;
using SismoPC.Ui.Controls;
using SismoPC.Ui.Services;

namespace SismoPC.Ui.Pages;

/// <summary>Détail d'un épisode : explication, courbe autour de l'épisode, responsable, ce qui a changé juste avant (US4, US5).</summary>
public sealed partial class EpisodePage : Page
{
    private sealed record EpisodeData(
        SismoPC.Data.StoredEpisode Episode,
        DateTimeOffset From,
        DateTimeOffset To,
        IReadOnlyList<SismoPC.Data.SeriesPoint> Series,
        IReadOnlyList<(DateTimeOffset At, string Kind, string Label, double? Value)> Events);

    public EpisodePage()
    {
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        Root.Children.Clear();
        if (e.Parameter is not long id)
        {
            Root.Children.Add(UiKit.Text("Épisode introuvable.", secondary: true));
            return;
        }

        var data = await DataAccess.ReadAsync<EpisodeData?>(store =>
        {
            var ep = store.ReadEpisode(id);
            if (ep is null)
            {
                return null;
            }

            var from = ep.Episode.Start.AddMinutes(-15);
            var to = ep.Episode.End.AddMinutes(15);
            return new EpisodeData(ep, from, to,
                store.ReadSeries(ep.Episode.Metric, from, to),
                store.ReadSystemEvents(ep.Episode.Start.AddMinutes(-30), ep.Episode.Start));
        });

        if (data is null)
        {
            Root.Children.Add(UiKit.Text("Cet épisode n'existe plus (ignoré ou effacé).", secondary: true));
            return;
        }

        var episode = data.Episode.Episode;
        var isAnomaly = episode.Kind == EpisodeKind.Anomaly;

        var title = new StackPanel { Spacing = 8 };
        title.Children.Add(UiKit.Text(data.Episode.Title, "TitleTextBlockStyle"));
        title.Children.Add(UiKit.Pill(isAnomaly ? "Anomalie" : "Ralentissement", isAnomaly ? Status.Info : Status.Warning, this));
        Root.Children.Add(title);
        var sentence = UiKit.Text(Core.Text.Explanations.Sentence(episode), "BodyLargeTextBlockStyle");
        sentence.MaxWidth = 820;
        sentence.HorizontalAlignment = HorizontalAlignment.Left;
        Root.Children.Add(sentence);

        var chart = new LineChart { Height = 180, Unit = episode.Metric == MetricKind.DiskLatencyMs ? "ms" : episode.Metric is MetricKind.CpuTemperatureC or MetricKind.GpuTemperatureC ? "°C" : "%" };
        var points = data.Series.Select(p => new ChartPoint(p.Timestamp, p.Max)).ToList();
        var (min, max) = Range(episode.Metric, points);
        chart.SetData(points, data.From, data.To, min, max, SeriesIndex(episode.Resource), [(episode.Start, episode.End)]);

        var layout = new Grid { ColumnSpacing = 20 };
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        var chartCard = UiKit.Card(UiKit.Stack(8,
            UiKit.Text($"{UiKit.When(data.From)} → {data.To.ToLocalTime():HH:mm}", "CaptionTextBlockStyle", secondary: true),
            chart));
        var side = UiKit.Stack(16, CulpritCard(episode), BeforeCard(data.Events), AdviceCard(data.Episode.Id, episode));
        Grid.SetColumn(side, 1);
        layout.Children.Add(chartCard);
        layout.Children.Add(side);
        void Adapt()
        {
            var narrow = Root.ActualWidth < 760;
            layout.ColumnDefinitions.Clear(); layout.RowDefinitions.Clear();
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(narrow ? 1 : 3, GridUnitType.Star) });
            if (!narrow) layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            if (narrow) layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetColumn(side, narrow ? 0 : 1); Grid.SetRow(side, narrow ? 1 : 0);
            layout.RowSpacing = 16;
        }
        Root.SizeChanged += (_, _) => Adapt();
        Adapt();
        Root.Children.Add(layout);
    }

    private static Border CulpritCard(Episode episode)
    {
        var panel = UiKit.Stack(8, UiKit.Text("PROGRAMME CONTRIBUTEUR", "CaptionTextBlockStyle", secondary: true));
        if (episode.Culprit is null)
        {
            panel.Children.Add(UiKit.Text("Les relevés ne permettent pas de distinguer un programme contributeur. Cela ne prouve pas que Windows est responsable.", secondary: true));
        }
        else
        {
            panel.Children.Add(UiKit.Text(episode.Culprit, "SubtitleTextBlockStyle"));
            panel.Children.Add(UiKit.Field("Part parmi les programmes relevés", UiKit.Percent(episode.CulpritShare * 100)));
        }

        panel.Children.Add(UiKit.Field("Durée", Core.Text.Explanations.FormatDuration(episode.Duration)));
        panel.Children.Add(UiKit.Text("Une contribution observée n'est pas une preuve de causalité.", "CaptionTextBlockStyle", secondary: true));
        return UiKit.Card(panel);
    }

    private static Border BeforeCard(IReadOnlyList<(DateTimeOffset At, string Kind, string Label, double? Value)> events)
    {
        var panel = UiKit.Stack(8, UiKit.Text("JUSTE AVANT", "CaptionTextBlockStyle", secondary: true));
        if (events.Count == 0)
        {
            panel.Children.Add(UiKit.Text("Aucun changement enregistré dans les 30 minutes précédentes.", secondary: true));
        }

        foreach (var ev in events)
        {
            panel.Children.Add(UiKit.Text($"{ev.At.ToLocalTime():HH:mm} · {ev.Label}"));
        }

        return UiKit.Card(panel);
    }

    private static Border AdviceCard(long id, Episode episode)
    {
        var finding = IncidentAnalysis.Explain(episode);
        var advice = finding.Action;
        var report = new Button { Content = "Préparer un rapport" };
        report.Click += (_, _) => App.Window?.Navigate("reports", new ReportSelection(episode.Start.AddMinutes(-15), episode.End.AddMinutes(15), "Rapport d'incident"));

        var timeline = new Button { Content = "Voir sur la chronologie", Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        timeline.Click += (_, _) => App.Window?.Navigate("timeline", episode.Start);
        var ignore = new Button { Content = "Ne plus afficher cet épisode" };
        ignore.Click += async (_, _) =>
        {
            ignore.IsEnabled = false;
            if (await ServiceClient.IgnoreEpisodeAsync(id))
            {
                App.Window?.Navigate("home");
            }
            else
            {
                ignore.IsEnabled = true;
                ignore.Content = "Service indisponible, réessayez";
            }
        };

        var buttons = new StackPanel { Spacing = 8 };
        buttons.Children.Add(report);
        buttons.Children.Add(timeline);
        buttons.Children.Add(ignore);
        return UiKit.Card(UiKit.Stack(10, UiKit.Text("QUE FAIRE ?", "CaptionTextBlockStyle", secondary: true), UiKit.Text(finding.Observation), UiKit.Text(advice), buttons));
    }

    private static (double Min, double Max) Range(MetricKind metric, List<ChartPoint> points) => metric switch
    {
        MetricKind.DiskLatencyMs or MetricKind.CpuTemperatureC or MetricKind.GpuTemperatureC or MetricKind.NetworkBytesPerSecond
            => (0, points.Count > 0 ? points.Max(p => p.Value) * 1.15 : 1),
        _ => (0, 100),
    };

    private static int SeriesIndex(Resource resource) => resource switch
    {
        Resource.Cpu => 0,
        Resource.Memory => 1,
        Resource.Disk => 2,
        _ => 3,
    };
}
