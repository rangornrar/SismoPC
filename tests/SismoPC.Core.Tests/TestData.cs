using SismoPC.Core.Model;

namespace SismoPC.Core.Tests;

/// <summary>Fabrique d'instantanés simulés pour les tests.</summary>
internal static class TestData
{
    public static readonly DateTimeOffset T0 = new(2026, 11, 17, 14, 0, 0, TimeSpan.Zero);

    public static Snapshot Snap(DateTimeOffset at, MetricKind metric, double value, params ProcessUsage[] processes) =>
        new(at, new Dictionary<MetricKind, double> { [metric] = value }, processes);

    /// <summary>Une suite d'instantanés toutes les 5 s, à partir de <paramref name="start"/>.</summary>
    public static IEnumerable<Snapshot> Series(DateTimeOffset start, MetricKind metric, IEnumerable<double> values, params ProcessUsage[] processes)
    {
        var i = 0;
        foreach (var v in values)
        {
            yield return Snap(start.AddSeconds(5 * i++), metric, v, processes);
        }
    }

    public static ProcessUsage Proc(string name, double cpu = 0, long ws = 0, double disk = 0) => new(name, cpu, ws, disk);
}
