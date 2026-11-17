using System.Globalization;
using SismoPC.Core.Model;

namespace SismoPC.Core.Text;

/// <summary>Rédige en français le titre et la phrase d'explication d'un épisode (US4).</summary>
public static class Explanations
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    public static string Title(Episode episode)
    {
        ArgumentNullException.ThrowIfNull(episode);
        var duration = FormatDuration(episode.Duration);

        if (episode.Kind == EpisodeKind.Anomaly)
        {
            var direction = episode.UsualValue is { } usual && episode.PeakValue < usual ? "anormalement bas" : "anormalement haut";
            return $"{MetricLabel(episode.Metric)} {direction} pendant {duration}";
        }

        return episode.Metric switch
        {
            MetricKind.CpuPercent => string.Format(Fr, "Processeur à {0:0} % pendant {1}", episode.PeakValue, duration),
            MetricKind.DiskActivePercent => $"Disque saturé pendant {duration}",
            MetricKind.DiskLatencyMs => $"Disque très lent pendant {duration}",
            MetricKind.MemoryCommittedPercent => $"Mémoire saturée pendant {duration}",
            _ => $"{MetricLabel(episode.Metric)} saturé pendant {duration}",
        };
    }

    /// <summary>Phrase d'explication, par exemple : « De 14:05 à 14:11, le disque était occupé à plus de 90 %, surtout par TiWorker.exe (92 % de l'activité). »</summary>
    public static string Sentence(Episode episode, TimeZoneInfo? zone = null)
    {
        ArgumentNullException.ThrowIfNull(episode);
        var tz = zone ?? TimeZoneInfo.Local;
        var start = TimeZoneInfo.ConvertTime(episode.Start, tz).ToString("HH:mm", Fr);
        var end = TimeZoneInfo.ConvertTime(episode.End, tz).ToString("HH:mm", Fr);

        var what = episode.Kind == EpisodeKind.Anomaly
            ? string.Format(Fr, "{0} a atteint {1}, alors qu'il est d'habitude autour de {2} à cette heure-ci",
                MetricSubject(episode.Metric), FormatValue(episode.Metric, episode.PeakValue),
                FormatValue(episode.Metric, episode.UsualValue ?? 0))
            : episode.Metric switch
            {
                MetricKind.CpuPercent => string.Format(Fr, "le processeur est monté jusqu'à {0:0} %", episode.PeakValue),
                MetricKind.DiskActivePercent => "le disque était occupé à plus de 90 %",
                MetricKind.DiskLatencyMs => string.Format(Fr, "le disque mettait jusqu'à {0:0} ms à répondre", episode.PeakValue),
                MetricKind.MemoryCommittedPercent => string.Format(Fr, "la mémoire était engagée à {0:0} %", episode.PeakValue),
                _ => $"{MetricSubject(episode.Metric)} était saturé",
            };

        var who = episode.Culprit is null
            ? string.Empty
            : string.Format(Fr, ", surtout par {0} ({1:0} % de l'activité)", episode.Culprit, episode.CulpritShare * 100);

        return $"De {start} à {end}, {what}{who}.";
    }

    public static string FormatDuration(TimeSpan d) => d.TotalMinutes switch
    {
        < 1 => $"{Math.Max(1, (int)Math.Round(d.TotalSeconds))} s",
        < 60 => $"{(int)Math.Round(d.TotalMinutes)} min",
        _ => $"{(int)d.TotalHours} h {d.Minutes:00}",
    };

    private static string MetricLabel(MetricKind m) => m switch
    {
        MetricKind.CpuPercent => "Processeur",
        MetricKind.MemoryUsedPercent or MetricKind.MemoryCommittedPercent => "Mémoire",
        MetricKind.DiskActivePercent or MetricKind.DiskLatencyMs => "Disque",
        MetricKind.GpuPercent => "Carte graphique",
        MetricKind.NetworkBytesPerSecond => "Réseau",
        MetricKind.CpuTemperatureC => "Température du processeur",
        MetricKind.GpuTemperatureC => "Température de la carte graphique",
        _ => m.ToString(),
    };

    private static string MetricSubject(MetricKind m) => m switch
    {
        MetricKind.CpuPercent => "le processeur",
        MetricKind.MemoryUsedPercent or MetricKind.MemoryCommittedPercent => "la mémoire",
        MetricKind.DiskActivePercent or MetricKind.DiskLatencyMs => "le disque",
        MetricKind.GpuPercent => "la carte graphique",
        MetricKind.NetworkBytesPerSecond => "le réseau",
        MetricKind.CpuTemperatureC => "la température du processeur",
        MetricKind.GpuTemperatureC => "la température de la carte graphique",
        _ => m.ToString(),
    };

    private static string FormatValue(MetricKind m, double v) => m switch
    {
        MetricKind.CpuTemperatureC or MetricKind.GpuTemperatureC => string.Format(Fr, "{0:0} °C", v),
        MetricKind.DiskLatencyMs => string.Format(Fr, "{0:0} ms", v),
        MetricKind.NetworkBytesPerSecond => string.Format(Fr, "{0:0.0} Mo/s", v / 1e6),
        _ => string.Format(Fr, "{0:0} %", v),
    };
}
