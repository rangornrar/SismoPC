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
public readonly record struct ChartPoint(DateTimeOffset At, double Value, bool IsObserved = true);

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
    private readonly Line _cursorLine = new() { StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 3,3 } };
    private readonly ToolTip _tooltip = new();
    public string Unit { get; set; } = "";

    public LineChart()
    {
        Height = 120;
        IsTabStop = true;
        Content = _canvas;
        SizeChanged += (_, _) => Redraw();
        ActualThemeChanged += (_, _) => Redraw();
        _canvas.PointerPressed += OnPointerPressed;
        ToolTipService.SetToolTip(_canvas, _tooltip);
        _canvas.PointerMoved += (_, e) =>
        {
            var at = AtX(e.GetCurrentPoint(_canvas).Position.X);
            var point = Nearest(at);
            _tooltip.Content = point is { } p ? $"{(p.IsObserved ? p.At : at).ToLocalTime():dd/MM HH:mm:ss} · {(p.IsObserved ? $"{p.Value:0.#} {Unit}" : "Sans relevé")}" : "Aucun relevé";
            CursorMoved?.Invoke(this, at);
        };
        KeyDown += (_, e) =>
        {
            if (e.Key is not (Windows.System.VirtualKey.Left or Windows.System.VirtualKey.Right)) return;
            var step = TimeSpan.FromSeconds(Math.Max(5, (_to - _from).TotalSeconds / 100));
            var at = (_cursor ?? _from) + (e.Key == Windows.System.VirtualKey.Left ? -step : step);
            at = at < _from ? _from : at > _to ? _to : at;
            SetCursor(at); TimeSelected?.Invoke(this, at); e.Handled = true;
        };
    }

    public event EventHandler<DateTimeOffset>? TimeSelected;
    public event EventHandler<DateTimeOffset>? CursorMoved;

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
        UpdateCursor();
    }

    private double X(DateTimeOffset t, double width) =>
        42 + (t - _from).TotalSeconds / Math.Max(1, (_to - _from).TotalSeconds) * Math.Max(1, width - 50);

    private double Y(double v, double height) =>
        8 + Math.Max(1, height - 32) * (1 - (Math.Clamp(v, _min, _max) - _min) / (_max - _min));

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
        var foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
        foreach (var value in new[] { _min, (_min+_max)/2, _max })
        {
            var y = Y(value,h);
            _canvas.Children.Add(new Line { X1 = 42, X2 = w-8, Y1 = y, Y2 = y, Stroke = grid, StrokeThickness = .5 });
            var label = new TextBlock { Text = value.ToString("0.#", UiKit.Fr), FontSize = 10, Foreground = foreground };
            Canvas.SetTop(label, y-7); _canvas.Children.Add(label);
        }
        for (var i=0; i<3; i++)
        {
            var time = _from + TimeSpan.FromTicks((_to-_from).Ticks*i/2);
            var label = new TextBlock { Text = time.ToLocalTime().ToString((_to-_from).TotalDays > 1 ? "dd/MM HH:mm" : "HH:mm", UiKit.Fr), FontSize = 10, Foreground = foreground };
            Canvas.SetLeft(label, i==0 ? 42 : i==2 ? Math.Max(42,w-90) : X(time,w)-30);
            Canvas.SetTop(label,h-20); _canvas.Children.Add(label);
        }

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
                Height = Math.Max(1,h-32),
                Fill = new SolidColorBrush(ColorHelper.FromArgb(40, warning.R, warning.G, warning.B)),
            };
            Canvas.SetLeft(band, x0);
            Canvas.SetTop(band, 8);
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

            fill.Points.Add(new Point(reduced[0].X, Y(_min,h)));
            foreach (var p in reduced)
            {
                line.Points.Add(p);
                fill.Points.Add(p);
            }

            fill.Points.Add(new Point(reduced[^1].X, Y(_min,h)));
            _canvas.Children.Add(fill);
            _canvas.Children.Add(line);
        }

        _cursorLine.Stroke = (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];
        _canvas.Children.Add(_cursorLine);
        UpdateCursor();
    }

    private void UpdateCursor()
    {
        _cursorLine.Visibility = _cursor is { } at && at >= _from && at <= _to ? Visibility.Visible : Visibility.Collapsed;
        if (_cursor is not { } value) return;
        _cursorLine.X1 = _cursorLine.X2 = X(value,ActualWidth);
        _cursorLine.Y1=8; _cursorLine.Y2=Math.Max(8,ActualHeight-24);
    }

    private DateTimeOffset AtX(double x) => _from + TimeSpan.FromSeconds((_to-_from).TotalSeconds * Math.Clamp((x-42)/Math.Max(1,ActualWidth-50),0,1));

    private ChartPoint? Nearest(DateTimeOffset at)
    {
        if (_points.Count==0) return null;
        var lo=0; var hi=_points.Count-1;
        while(lo<hi) { var mid=(lo+hi)/2; if(_points[mid].At<at) lo=mid+1; else hi=mid; }
        if(lo>0 && (at-_points[lo-1].At).Duration() < (_points[lo].At-at).Duration()) lo--;
        return _points[lo];
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        Focus(FocusState.Pointer);
        var at = AtX(e.GetCurrentPoint(_canvas).Position.X);
        SetCursor(at);
        TimeSelected?.Invoke(this, at);
    }
}
