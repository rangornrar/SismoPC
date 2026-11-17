using System.Globalization;
using SismoPC.Core.Model;

namespace SismoPC.Core.Health;

/// <summary>Seuils des alertes matérielles (US6), réglables dans les paramètres.</summary>
public sealed record HardwareAlertThresholds
{
    public double CpuCriticalC { get; init; } = 95;

    public double GpuCriticalC { get; init; } = 90;

    public double DiskCriticalC { get; init; } = 70;

    public double SsdWearWarningPercent { get; init; } = 80;

    public double LowFreeSpacePercent { get; init; } = 10;
}

/// <summary>Transforme l'état matériel en alertes (US6).</summary>
public static class HardwareAlertRules
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    public static IReadOnlyList<HardwareAlert> Evaluate(HardwareState state, HardwareAlertThresholds? thresholds = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        var t = thresholds ?? new HardwareAlertThresholds();
        var alerts = new List<HardwareAlert>();

        if (state.CpuTemperatureC is { } cpu && cpu >= t.CpuCriticalC)
        {
            alerts.Add(new(AlertKind.CpuTemperature, AlertSeverity.Critical, "Processeur",
                string.Format(Fr, "Le processeur atteint {0:0} °C.", cpu)));
        }

        if (state.GpuTemperatureC is { } gpu && gpu >= t.GpuCriticalC)
        {
            alerts.Add(new(AlertKind.GpuTemperature, AlertSeverity.Critical, "Carte graphique",
                string.Format(Fr, "La carte graphique atteint {0:0} °C.", gpu)));
        }

        foreach (var disk in state.Disks)
        {
            if (disk.PredictsFailure)
            {
                alerts.Add(new(AlertKind.DiskSmartFailure, AlertSeverity.Critical, disk.Name,
                    "Le disque signale un risque de panne. Sauvegarde tes données sans attendre."));
            }

            if (disk.TemperatureC is { } temp && temp >= t.DiskCriticalC)
            {
                alerts.Add(new(AlertKind.DiskTemperature, AlertSeverity.Critical, disk.Name,
                    string.Format(Fr, "Le disque atteint {0:0} °C.", temp)));
            }

            if (disk.IsSsd && disk.WearPercent is { } wear && wear >= t.SsdWearWarningPercent)
            {
                alerts.Add(new(AlertKind.SsdWear, AlertSeverity.Warning, disk.Name,
                    string.Format(Fr, "Le SSD est usé à {0:0} %.", wear)));
            }
        }

        foreach (var volume in state.Volumes)
        {
            if (volume.FreePercent < t.LowFreeSpacePercent)
            {
                alerts.Add(new(AlertKind.LowDiskSpace, AlertSeverity.Warning, volume.Letter,
                    string.Format(Fr, "{0:0} Go libres sur {1:0} Go ({2:0} %).",
                        volume.FreeBytes / 1e9, volume.TotalBytes / 1e9, volume.FreePercent)));
            }
        }

        return alerts;
    }
}
