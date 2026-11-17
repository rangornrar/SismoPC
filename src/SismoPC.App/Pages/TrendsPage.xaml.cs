using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SismoPC.Core.Health;
using SismoPC.Ui.Controls;
using SismoPC.Ui.Services;

namespace SismoPC.Ui.Pages;

/// <summary>Tendances : évolution mois par mois des indicateurs de dégradation, premier mois en référence (US8).</summary>
public sealed partial class TrendsPage : Page
{
    public TrendsPage()
    {
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        Root.Children.Clear();

        var trends = await DataAccess.ReadAsync(store => store.ReadTrends()) ?? [];
        var first = trends.SelectMany(t => t.Months).Select(m => m.Month).DefaultIfEmpty().Min();
        Root.Children.Add(UiKit.Header("Tendances", first is null
            ? "Évolution mois par mois : les premières valeurs apparaîtront après quelques jours de collecte."
            : $"Évolution mois par mois depuis {Trend.MonthLabel(first)} (premier mois de surveillance, en référence)"));

        var degraded = trends.Where(t => t.IsDegraded).ToList();
        if (degraded.Count > 0)
        {
            Root.Children.Add(UiKit.Banner(
                degraded.Count == 1 ? "1 indicateur s'est dégradé de plus de 20 %" : $"{degraded.Count} indicateurs se sont dégradés de plus de 20 %",
                string.Join(", ", degraded.Select(t => t.Name.ToLower(UiKit.Fr))),
                InfoBarSeverity.Warning));
        }

        var cards = trends.Select((t, i) => TrendCard(t, i)).ToList<UIElement>();
        cards.Add(UiKit.Card(UiKit.Stack(8,
            UiKit.SectionTitle("Capacité de la batterie"),
            UiKit.Text("Sur les portables, SismoPC suivra la capacité de la batterie par rapport à sa capacité d'origine. Mesure prévue dans une prochaine version.", secondary: true))));
        Root.Children.Add(UiKit.Columns(2, cards, 20));
    }

    private Border TrendCard(Trend trend, int index)
    {
        var status = trend.IsDegraded ? Status.Warning : trend.Change is null ? Status.Info : Status.Good;
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var pill = UiKit.Pill(trend.ChangeLabel, status, this);
        Grid.SetColumn(pill, 1);
        head.Children.Add(UiKit.Text(trend.Name, "BodyStrongTextBlockStyle"));
        head.Children.Add(pill);

        var panel = UiKit.Stack(8, head);
        if (trend.Last is not { } last || trend.First is not { } firstMonth)
        {
            panel.Children.Add(UiKit.Text("Pas encore de mesure.", secondary: true));
            return UiKit.Card(panel);
        }

        var value = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        value.Children.Add(UiKit.Text(Format(last.Value, trend.Unit), "TitleTextBlockStyle"));
        var reference = UiKit.Text($"en {Trend.MonthLabel(firstMonth.Month)} : {Format(firstMonth.Value, trend.Unit)}", secondary: true);
        reference.VerticalAlignment = VerticalAlignment.Bottom;
        value.Children.Add(reference);
        panel.Children.Add(value);

        if (trend.Months.Count > 1)
        {
            var points = trend.Months
                .Select(m => new ChartPoint(DateTimeOffset.ParseExact(m.Month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal), m.Value))
                .ToList();
            var min = points.Min(p => p.Value);
            var max = points.Max(p => p.Value);
            var pad = Math.Max(1, (max - min) * 0.3);
            var chart = new LineChart { Height = 80 };
            chart.SetData(points, points[0].At, points[^1].At, Math.Max(0, min - pad), max + pad, index);
            panel.Children.Add(chart);
        }

        return UiKit.Card(panel);
    }

    private static string Format(double value, string unit) =>
        unit == "s" || unit == "°C" || unit == "%"
            ? string.Format(UiKit.Fr, "{0:0} {1}", value, unit)
            : string.Format(UiKit.Fr, "{0:0.0} {1}", value, unit);
}
