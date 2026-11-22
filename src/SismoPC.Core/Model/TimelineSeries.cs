namespace SismoPC.Core.Model;

public readonly record struct TimedValue(DateTimeOffset At, double Value, TimeSpan Resolution);

/// <summary>Projection graphique seulement : les valeurs nulles ajoutées ne sont jamais persistées.</summary>
public static class TimelineSeries
{
    public static IReadOnlyList<TimedValue> WithZeroGaps(IEnumerable<TimedValue> source, DateTimeOffset from, DateTimeOffset to)
    {
        var points = source.OrderBy(p => p.At).Where(p => p.At <= to && p.At + p.Resolution >= from).ToList();
        var result = new List<TimedValue>();
        void Zero(DateTimeOffset at) => result.Add(new TimedValue(at, 0, TimeSpan.Zero));
        Zero(from);
        TimedValue? previous = null;
        foreach (var point in points)
        {
            var at = point.At < from ? from : point.At;
            if (previous is { } prev)
            {
                var end = prev.At + prev.Resolution;
                var tolerance = TimeSpan.FromTicks(Math.Max(TimeSpan.FromSeconds(10).Ticks, prev.Resolution.Ticks / 2));
                if (point.At - end > tolerance)
                {
                    Zero(end < from ? from : end);
                    Zero(at);
                }
            }
            else Zero(at);
            result.Add(point with { At = at });
            previous = point;
        }
        if (previous is { } last)
        {
            var end = last.At + last.Resolution;
            if (end < to) { Zero(end < from ? from : end); Zero(to); }
        }
        else Zero(to);
        return result;
    }
}
