using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SismoPC.Core.Model;
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
            Root.Children.Add(Ui.Text("Épisode introuvable.", secondary: true));
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
            Root.Children.Add(Ui.Text("Cet épisode n'existe plus (ignoré ou effacé).", secondary: true));
            return;
        }

        var episode = data.Episode.Episode;
        var isAnomaly = episode.Kind == EpisodeKind.Anomaly;

        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        title.Children.Add(Ui.Text(data.Episode.Title, "TitleTextBlockStyle"));
        title.Children.Add(Ui.Pill(isAnomaly ? "Anomalie" : "Ralentissement", isAnomaly ? Status.Info : Status.Warning, this));
        Root.Children.Add(title);
        var sentence = Ui.Text(data.Episode.Sentence, "BodyLargeTextBlockStyle");
        sentence.MaxWidth = 820;
        sentence.HorizontalAlignment = HorizontalAlignment.Left;
        Root.Children.Add(sentence);

        var chart = new LineChart { Height = 180 };
        var points = data.Series.Select(p => new ChartPoint(p.Timestamp, p.Max)).ToList();
        var (min, max) = Range(episode.Metric, points);
        chart.SetData(points, data.From, data.To, min, max, SeriesIndex(episode.Resource), [(episode.Start, episode.End)]);

        var layout = new Grid { ColumnSpacing = 20 };
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        var chartCard = Ui.Card(Ui.Stack(8,
            Ui.Text($"{Ui.When(data.From)} → {data.To.ToLocalTime():HH:mm}", "CaptionTextBlockStyle", secondary: true),
            chart));
        var side = Ui.Stack(16, CulpritCard(episode), BeforeCard(data.Events), AdviceCard(data.Episode.Id, episode));
        Grid.SetColumn(side, 1);
        layout.Children.Add(chartCard);
        layout.Children.Add(side);
        Root.Children.Add(layout);
    }

    private static Border CulpritCard(Episode episode)
    {
        var panel = Ui.Stack(8, Ui.Text("RESPONSABLE PROBABLE", "CaptionTextBlockStyle", secondary: true));
        if (episode.Culprit is null)
        {
            panel.Children.Add(Ui.Text("Aucun programme ne se distingue : la charge venait de Windows ou de plusieurs programmes à la fois.", secondary: true));
        }
        else
        {
            panel.Children.Add(Ui.Text(episode.Culprit, "SubtitleTextBlockStyle"));
            panel.Children.Add(Ui.Field("Part de l'activité", Ui.Percent(episode.CulpritShare * 100)));
        }

        panel.Children.Add(Ui.Field("Durée", Core.Text.Explanations.FormatDuration(episode.Duration)));
        return Ui.Card(panel);
    }

    private static Border BeforeCard(IReadOnlyList<(DateTimeOffset At, string Kind, string Label, double? Value)> events)
    {
        var panel = Ui.Stack(8, Ui.Text("JUSTE AVANT", "CaptionTextBlockStyle", secondary: true));
        if (events.Count == 0)
        {
            panel.Children.Add(Ui.Text("Aucun changement enregistré dans les 30 minutes précédentes.", secondary: true));
        }

        foreach (var ev in events)
        {
            panel.Children.Add(Ui.Text($"{ev.At.ToLocalTime():HH:mm} · {ev.Label}"));
        }

        return Ui.Card(panel);
    }

    private Border AdviceCard(long id, Episode episode)
    {
        var advice = episode.Resource switch
        {
            Resource.Disk => "Fréquent pendant une mise à jour ou une analyse antivirus. Si cela revient souvent hors de ces moments, surveille l'état du disque dans Matériel.",
            Resource.Cpu => "Si le même programme revient souvent, vérifie ses réglages ou s'il se lance tout seul au démarrage.",
            Resource.Memory => "Fermer les programmes inutiles libère de la mémoire. Si cela revient souvent, plus de mémoire peut aider.",
            _ => "Si cet épisode se répète, observe la chronologie autour pour repérer ce qu'ils ont en commun.",
        };

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
                ignore.Content = "Service indisponible, réessaie";
            }
        };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(timeline);
        buttons.Children.Add(ignore);
        return Ui.Card(Ui.Stack(10, Ui.Text("À RETENIR", "CaptionTextBlockStyle", secondary: true), Ui.Text(advice), buttons));
    }

    private static (double Min, double Max) Range(MetricKind metric, IReadOnlyList<ChartPoint> points) => metric switch
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
