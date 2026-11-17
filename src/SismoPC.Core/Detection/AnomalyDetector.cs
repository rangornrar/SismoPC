using SismoPC.Core.Model;

namespace SismoPC.Core.Detection;

/// <summary>
/// Détecte les anomalies (US5) : une mesure reste hors de sa plage habituelle au moins
/// <see cref="MinDuration"/>. Pendant l'apprentissage (14 jours par défaut), il apprend sans alerter.
/// </summary>
public sealed class AnomalyDetector
{
    private static readonly MetricKind[] Watched =
    [
        MetricKind.CpuPercent,
        MetricKind.MemoryUsedPercent,
        MetricKind.DiskActivePercent,
        MetricKind.GpuPercent,
        MetricKind.CpuTemperatureC,
        MetricKind.GpuTemperatureC,
    ];

    private readonly Baseline _baseline;
    private readonly Dictionary<MetricKind, OpenAnomaly> _open = [];

    public AnomalyDetector(Baseline baseline, DateTimeOffset learningStartedAt)
    {
        _baseline = baseline;
        LearningStartedAt = learningStartedAt;
    }

    public DateTimeOffset LearningStartedAt { get; }

    public TimeSpan LearningPeriod { get; init; } = TimeSpan.FromDays(14);

    public TimeSpan MinDuration { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>Fuseau utilisé pour rattacher une mesure à son créneau horaire.</summary>
    public TimeZoneInfo TimeZone { get; init; } = TimeZoneInfo.Local;

    public bool IsLearning(DateTimeOffset now) => now < LearningStartedAt + LearningPeriod;

    /// <summary>Anomalies en cours depuis au moins <see cref="MinDuration"/> (pour le score de santé).</summary>
    public int OpenCount(DateTimeOffset now) => _open.Values.Count(a => now - a.Start >= MinDuration);

    /// <summary>Traite un instantané ; renvoie les anomalies qui viennent de se terminer.</summary>
    public IReadOnlyList<Episode> Process(Snapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var closed = new List<Episode>();
        var local = TimeZoneInfo.ConvertTime(snapshot.Timestamp, TimeZone);
        var learning = IsLearning(snapshot.Timestamp);

        foreach (var metric in Watched)
        {
            var value = snapshot[metric];
            if (value is null)
            {
                continue;
            }

            var range = learning ? null : _baseline.UsualRange(metric, local);
            var outside = range is { } r && (value.Value > r.High || value.Value < r.Low);

            if (outside)
            {
                if (!_open.TryGetValue(metric, out var open))
                {
                    open = new OpenAnomaly(snapshot.Timestamp, range!.Value.Mean);
                    _open[metric] = open;
                }

                open.Last = snapshot.Timestamp;
                open.Peak = Math.Abs(value.Value - open.Usual) > Math.Abs(open.Peak - open.Usual) ? value.Value : open.Peak;
                open.Accumulate(metric, snapshot.TopProcesses);
            }
            else
            {
                if (_open.Remove(metric, out var ended) && ended.Last - ended.Start >= MinDuration)
                {
                    closed.Add(ended.ToEpisode(metric));
                }

                // On n'apprend que des valeurs normales, pour ne pas habituer la normale aux anomalies.
                _baseline.Learn(metric, local, value.Value);
            }
        }

        return closed;
    }

    private static Resource ResourceOf(MetricKind metric) => metric switch
    {
        MetricKind.CpuPercent or MetricKind.CpuTemperatureC => Resource.Cpu,
        MetricKind.MemoryUsedPercent or MetricKind.MemoryCommittedPercent => Resource.Memory,
        MetricKind.DiskActivePercent or MetricKind.DiskLatencyMs => Resource.Disk,
        MetricKind.GpuPercent or MetricKind.GpuTemperatureC => Resource.Gpu,
        _ => Resource.Network,
    };

    private sealed class OpenAnomaly(DateTimeOffset start, double usual)
    {
        private readonly Dictionary<string, double> _weights = new(StringComparer.OrdinalIgnoreCase);

        public DateTimeOffset Start { get; } = start;

        public DateTimeOffset Last { get; set; } = start;

        public double Usual { get; } = usual;

        public double Peak { get; set; } = usual;

        public void Accumulate(MetricKind metric, IReadOnlyList<ProcessUsage> processes)
        {
            foreach (var p in processes)
            {
                var weight = ResourceOf(metric) switch
                {
                    Resource.Cpu or Resource.Gpu => p.CpuPercent,
                    Resource.Memory => p.WorkingSetBytes,
                    Resource.Disk => p.DiskBytesPerSecond,
                    _ => 0,
                };
                if (weight > 0)
                {
                    _weights[p.Name] = _weights.GetValueOrDefault(p.Name) + weight;
                }
            }
        }

        public Episode ToEpisode(MetricKind metric)
        {
            string? culprit = null;
            double share = 0;
            var total = _weights.Values.Sum();
            if (total > 0)
            {
                var top = _weights.MaxBy(kv => kv.Value);
                culprit = top.Key;
                share = top.Value / total;
            }

            return new Episode(EpisodeKind.Anomaly, ResourceOf(metric), metric, Start, Last, Peak, Usual, culprit, share);
        }
    }
}
