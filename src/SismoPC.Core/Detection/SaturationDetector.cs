using SismoPC.Core.Model;

namespace SismoPC.Core.Detection;

/// <summary>Règle de saturation : une mesure au-dessus d'un seuil pendant une durée minimale.</summary>
public sealed record SaturationRule(Resource Resource, MetricKind Metric, double Threshold, TimeSpan MinDuration)
{
    /// <summary>Règles de départ des spécifications : 90 % pendant 30 s, latence disque 50 ms.</summary>
    public static IReadOnlyList<SaturationRule> Defaults(double percentThreshold = 90, int minSeconds = 30)
    {
        var min = TimeSpan.FromSeconds(minSeconds);
        return
        [
            new(Resource.Cpu, MetricKind.CpuPercent, percentThreshold, min),
            new(Resource.Disk, MetricKind.DiskActivePercent, percentThreshold, min),
            new(Resource.Disk, MetricKind.DiskLatencyMs, 50, min),
            new(Resource.Memory, MetricKind.MemoryCommittedPercent, percentThreshold, min),
        ];
    }
}

/// <summary>
/// Détecte les épisodes de ralentissement (US4) : une ressource reste au-dessus de son seuil
/// au moins <see cref="SaturationRule.MinDuration"/>. L'épisode est émis quand la saturation cesse,
/// avec le programme qui consommait le plus la ressource pendant l'épisode.
/// </summary>
public sealed class SaturationDetector
{
    private readonly IReadOnlyList<SaturationRule> _rules;
    private readonly Dictionary<SaturationRule, State> _states = [];

    public SaturationDetector(IEnumerable<SaturationRule> rules)
    {
        _rules = rules.ToList();
    }

    /// <summary>Traite un instantané ; renvoie les épisodes qui viennent de se terminer.</summary>
    public IReadOnlyList<Episode> Process(Snapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var closed = new List<Episode>();

        foreach (var rule in _rules)
        {
            if (_states.TryGetValue(rule, out var previous) &&
                (snapshot.Timestamp - previous.LastAbove > TimeSpan.FromSeconds(15) || snapshot.Timestamp < previous.LastAbove || snapshot[rule.Metric] is null))
            {
                if (previous.ToEpisode(rule) is { } interrupted) closed.Add(interrupted);
                _states.Remove(rule);
            }
            var value = snapshot[rule.Metric];
            if (value is null)
            {
                continue;
            }

            _states.TryGetValue(rule, out var state);

            if (value.Value >= rule.Threshold)
            {
                state ??= new State(snapshot.Timestamp);
                state.LastAbove = snapshot.Timestamp;
                state.Peak = Math.Max(state.Peak, value.Value);
                state.Accumulate(rule.Resource, snapshot.TopProcesses);
                _states[rule] = state;
            }
            else if (state is not null)
            {
                _states.Remove(rule);
                var episode = state.ToEpisode(rule);
                if (episode is not null)
                {
                    closed.Add(episode);
                }
            }
        }

        return closed;
    }

    /// <summary>Clôt les épisodes en cours (arrêt du service).</summary>
    public IReadOnlyList<Episode> CurrentEpisodes() => _states.Select(kv => kv.Value.ToEpisode(kv.Key)).OfType<Episode>().ToList();

    public IReadOnlyList<Episode> Flush()
    {
        var closed = _states
            .Select(kv => kv.Value.ToEpisode(kv.Key))
            .OfType<Episode>()
            .ToList();
        _states.Clear();
        return closed;
    }

    private sealed class State(DateTimeOffset start)
    {
        private readonly Dictionary<string, double> _weights = new(StringComparer.OrdinalIgnoreCase);

        public DateTimeOffset Start { get; } = start;

        public DateTimeOffset LastAbove { get; set; } = start;

        public double Peak { get; set; }

        public void Accumulate(Resource resource, IReadOnlyList<ProcessUsage> processes)
        {
            foreach (var p in processes)
            {
                var weight = resource switch
                {
                    Resource.Cpu => p.CpuPercent,
                    Resource.Disk => p.DiskBytesPerSecond,
                    Resource.Memory => p.WorkingSetBytes,
                    _ => 0,
                };
                if (weight > 0)
                {
                    _weights[p.Name] = _weights.GetValueOrDefault(p.Name) + weight;
                }
            }
        }

        public Episode? ToEpisode(SaturationRule rule)
        {
            if (LastAbove - Start < rule.MinDuration)
            {
                return null;
            }

            string? culprit = null;
            double share = 0;
            var total = _weights.Values.Sum();
            if (total > 0)
            {
                var top = _weights.MaxBy(kv => kv.Value);
                culprit = top.Key;
                share = top.Value / total;
            }

            return new Episode(EpisodeKind.Slowdown, rule.Resource, rule.Metric, Start, LastAbove, Peak, null, culprit, share);
        }
    }
}
