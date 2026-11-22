using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using SismoPC.Ui.Services;
using Windows.Foundation;

namespace SismoPC.Ui.Controls;

/// <summary>Point d'une courbe.</summary>
public readonly record struct ChartPoint(DateTimeOffset At, double Value);

/// <summary>
/// Courbe temporelle légère (une série), avec bandes surlignées (épisodes) et curseur.
/// Les points sont réduits à la largeur disponible : 90 jours de mesures s'affichent sans ralentir.
/// Un clic sur la courbe signale l'instant choisi (<see cref="TimeSelected"/>).
/// </summary>
public sealed partial class LineChart : UserControl
{
    private readonly Canvas _canvas = new() { Background = new SolidColorBrush(Colors.Transparent) };
    private IReadOnlyList<ChartPoint> _points = [];
    private IReadOnlyList<(DateTimeOffset Start, DateTimeOffset End)> _bands = [];
    private DateTimeOffset _from;
    private DateTimeOffset _to;
    private double _min;
    private double _max = 100;
    private int _seriesIndex;
    private DateTimeOffset? _cursor;

    public LineChart()
    {
        Height = 90;
        Content = _canvas;
        SizeChanged += (_, _) => Redraw();
        ActualThemeChanged += (_, _) => Redraw();
        _canvas.PointerPressed += OnPointerPressed;
    }

    public event EventHandler<DateTimeOffset>? TimeSelected;

    public void SetData(
        IReadOnlyList<ChartPoint> points,
        DateTimeOffset from,
        DateTimeOffset to,
        double min,
        double max,
        int seriesIndex,
        IReadOnlyList<(DateTimeOffset Start, DateTimeOffset End)>? bands = null)
    {
        _points = points;
        _from = from;
        _to = to;
        _min = min;
        _max = max <= min ? min + 1 : max;
        _seriesIndex = seriesIndex;
        _bands = bands ?? [];
        Redraw();
    }

    public void SetCursor(DateTimeOffset? at)
    {
        _cursor = at;
        Redraw();
    }

    private double X(DateTimeOffset t, double width) =>
        (t - _from).TotalSeconds / Math.Max(1, (_to - _from).TotalSeconds) * width;

    private double Y(double v, double height) =>
        height - (Math.Clamp(v, _min, _max) - _min) / (_max - _min) * height;

    private void Redraw()
    {
        _canvas.Children.Clear();
        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 0 || h <= 0)
        {
            return;
        }

        var grid = (Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"];
        _canvas.Children.Add(new Line { X1 = 0, X2 = w, Y1 = h - 0.5, Y2 = h - 0.5, Stroke = grid, StrokeThickness = 1 });
        _canvas.Children.Add(new Line { X1 = 0, X2 = w, Y1 = 0.5, Y2 = 0.5, Stroke = grid, StrokeThickness = 1 });

        var warning = Palette.StatusColor(Status.Warning, this);
        foreach (var (start, end) in _bands)
        {
            var x0 = Math.Max(0, X(start, w));
            var x1 = Math.Min(w, X(end, w));
            if (x1 <= 0 || x0 >= w)
            {
                continue;
            }

            var band = new Rectangle
            {
                Width = Math.Max(3, x1 - x0),
                Height = h,
                Fill = new SolidColorBrush(ColorHelper.FromArgb(40, warning.R, warning.G, warning.B)),
            };
            Canvas.SetLeft(band, x0);
            _canvas.Children.Add(band);
        }

        if (_points.Count > 1)
        {
            var color = Palette.Series(_seriesIndex, this);
            var line = new Polyline { Stroke = new SolidColorBrush(color), StrokeThickness = 1.5, StrokeLineJoin = PenLineJoin.Round };
            var fill = new Polygon { Fill = new SolidColorBrush(ColorHelper.FromArgb(28, color.R, color.G, color.B)) };

            // Réduction : extrêmes et transitions par pixel, pour conserver les pics et les retours à zéro.
            var buckets = Math.Max(2, (int)w);
            var reduced = _points.Select((point, index) => (point, index))
                .GroupBy(p => (int)Math.Clamp(X(p.point.At, w), 0, buckets - 1))
                .OrderBy(g => g.Key)
                .SelectMany(g => new[] { g.First(), g.MinBy(p => p.point.Value), g.MaxBy(p => p.point.Value), g.Last() }
                    .DistinctBy(p => p.index).OrderBy(p => p.index)
                    .Select(p => new Point(g.Key, Y(p.point.Value, h))))
                .ToList();

            fill.Points.Add(new Point(reduced[0].X, h));
            foreach (var p in reduced)
            {
                line.Points.Add(p);
                fill.Points.Add(p);
            }

            fill.Points.Add(new Point(reduced[^1].X, h));
            _canvas.Children.Add(fill);
            _canvas.Children.Add(line);
        }

        if (_cursor is { } cursor)
        {
            var x = X(cursor, w);
            _canvas.Children.Add(new Line
            {
                X1 = x, X2 = x, Y1 = 0, Y2 = h,
                Stroke = (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"],
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 3, 3 },
            });
        }
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var x = e.GetCurrentPoint(_canvas).Position.X;
        var w = Math.Max(1, ActualWidth);
        var at = _from + TimeSpan.FromSeconds((_to - _from).TotalSeconds * Math.Clamp(x / w, 0, 1));
        TimeSelected?.Invoke(this, at);
    }
}
