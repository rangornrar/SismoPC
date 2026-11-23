using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SismoPC.Core.Health;
using SismoPC.Core.Model;
using SismoPC.Ui.Controls;
using SismoPC.Ui.Services;

namespace SismoPC.Ui.Pages;

public sealed partial class IncidentPage : Page
{
    private readonly StackPanel _root = new() { Spacing = 20, Padding = new Thickness(32,28,32,40), MaxWidth = 1100 };
    public IncidentPage()
    {
        InitializeComponent();
        Content = new ScrollViewer { Content = _root };
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _root.Children.Add(UiKit.Header("Comprendre ce ralentissement", "Examen des dix minutes précédant votre signalement."));
        _root.Children.Add(UiKit.Text("Lecture des observations…", secondary: true));
        var at = e.Parameter is DateTimeOffset saved ? saved : await ServiceClient.MarkIncidentAsync();
        _root.Children.RemoveAt(_root.Children.Count - 1);
        if (at is null)
        {
            _root.Children.Add(UiKit.Banner("Le signalement n'a pas pu être enregistré", "Vérifiez que le service SismoPC fonctionne et qu'il est à jour. Vous pouvez toujours consulter la chronologie.", InfoBarSeverity.Warning));
            var timeline = new Button { Content = "Ouvrir la chronologie" };
            timeline.Click += (_, _) => App.Window?.Navigate("timeline");
            _root.Children.Add(timeline);
            return;
        }
        var from = at.Value.AddMinutes(-10);
        _root.Children.Add(UiKit.Text($"Signalement enregistré · {at.Value.ToLocalTime():dd/MM/yyyy HH:mm:ss}", "BodyStrongTextBlockStyle"));
        var data = await DataAccess.ReadAsync(store => (Observations: store.ReadObservations(from, at.Value),
            Cpu: store.ReadSeries(MetricKind.CpuPercent, from, at.Value), Processes: store.ReadProcessesAt(at.Value)));
        var assessment = IncidentAnalysis.Assess(data.Observations ?? []);
        _root.Children.Add(UiKit.Card(UiKit.Stack(10, UiKit.Text(assessment.Summary, "SubtitleTextBlockStyle"), UiKit.Text(assessment.Limit, secondary: true))));
        foreach (var finding in assessment.Findings)
            _root.Children.Add(UiKit.Card(UiKit.Stack(8, UiKit.SectionTitle(finding.Title), UiKit.Text(finding.Observation), UiKit.Text("À essayer : " + finding.Action))));
        var chart = new LineChart { Height = 170, Unit = "%" };
        var points = TimelineSeries.WithZeroGaps((data.Cpu ?? []).Select(p => new TimedValue(p.Timestamp, p.Max, p.Resolution)), from, at.Value);
        chart.SetData(points.Select(p => new ChartPoint(p.At, p.Value, p.Resolution > TimeSpan.Zero)).ToList(), from, at.Value, 0, 100, 0);
        _root.Children.Add(UiKit.Card(UiKit.Stack(8, UiKit.SectionTitle("Activité du processeur avant le signalement"), chart,
            UiKit.Text("Les périodes sans relevé sont représentées à zéro ; il ne s'agit pas de mesures réelles.", "CaptionTextBlockStyle", secondary: true))));
        var programs = UiKit.Stack(8, UiKit.SectionTitle("Programmes relevés autour du signalement"));
        if (data.Processes is not { Count: > 0 }) programs.Children.Add(UiKit.Text("Aucun relevé de programme suffisamment proche n'est disponible.", secondary: true));
        else foreach (var process in data.Processes.Take(5)) programs.Children.Add(UiKit.Field(process.Name, $"CPU {process.CpuPercent:0.#} % · mémoire {process.WorkingSetBytes / 1e9:0.0} Go"));
        programs.Children.Add(UiKit.Text("Cette liste ne prouve pas que l'un de ces programmes est responsable du problème.", "CaptionTextBlockStyle", secondary: true));
        _root.Children.Add(UiKit.Card(programs));
        var report = new Button { Content = "Préparer un rapport de cet incident", Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        report.Click += (_, _) => App.Window?.Navigate("reports", new ReportSelection(from, at.Value, "Rapport d'un ralentissement signalé"));
        _root.Children.Add(report);
    }
}
