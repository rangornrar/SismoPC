using SismoPC.Core.Model;

namespace SismoPC.Core.Detection;

/// <summary>Statistiques cumulées (algorithme de Welford) : moyenne et écart-type sans garder les valeurs.</summary>
public sealed class RunningStats
{
    public long Count { get; private set; }

    public double Mean { get; private set; }

    /// <summary>Somme des carrés des écarts à la moyenne.</summary>
    public double M2 { get; private set; }

    public RunningStats()
    {
    }

    public RunningStats(long count, double mean, double m2)
    {
        Count = count;
        Mean = mean;
        M2 = m2;
    }

    public double StandardDeviation => Count > 1 ? Math.Sqrt(M2 / (Count - 1)) : 0;

    public void Add(double value)
    {
        Count++;
        var delta = value - Mean;
        Mean += delta / Count;
        M2 += delta * (value - Mean);
    }
}

/// <summary>Créneau de la normale : heure de la journée et type de jour (semaine ou week-end).</summary>
public readonly record struct BaselineSlot(MetricKind Metric, int Hour, bool Weekend)
{
    public static BaselineSlot For(MetricKind metric, DateTimeOffset localTime) =>
        new(metric, localTime.Hour, localTime.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday);
}

/// <summary>
/// Normale du PC (US5) : pour chaque mesure, la plage habituelle selon l'heure et le type de jour.
/// La plage vaut moyenne ± <see cref="Sigmas"/> écarts-types, jamais plus étroite que <see cref="MinHalfWidth"/>.
/// </summary>
public sealed class Baseline
{
    private readonly Dictionary<BaselineSlot, RunningStats> _stats = [];

    /// <summary>Nombre d'écarts-types de part et d'autre de la moyenne.</summary>
    public double Sigmas { get; init; } = 3;

    /// <summary>Demi-largeur minimale de la plage, en unité de la mesure (évite les alertes sur un PC très régulier).</summary>
    public double MinHalfWidth { get; init; } = 5;

    /// <summary>Nombre minimal de mesures dans un créneau pour qu'il serve de référence.</summary>
    public int MinSamples { get; init; } = 120;

    public IReadOnlyDictionary<BaselineSlot, RunningStats> Slots => _stats;

    public void Learn(MetricKind metric, DateTimeOffset localTime, double value)
    {
        var slot = BaselineSlot.For(metric, localTime);
        if (!_stats.TryGetValue(slot, out var stats))
        {
            stats = new RunningStats();
            _stats[slot] = stats;
        }

        stats.Add(value);
    }

    public void Load(BaselineSlot slot, RunningStats stats) => _stats[slot] = stats;

    /// <summary>Plage habituelle, ou null si le créneau n'a pas assez de mesures.</summary>
    public (double Low, double High, double Mean)? UsualRange(MetricKind metric, DateTimeOffset localTime)
    {
        if (!_stats.TryGetValue(BaselineSlot.For(metric, localTime), out var stats) || stats.Count < MinSamples)
        {
            return null;
        }

        var half = Math.Max(Sigmas * stats.StandardDeviation, MinHalfWidth);
        return (stats.Mean - half, stats.Mean + half, stats.Mean);
    }
}
