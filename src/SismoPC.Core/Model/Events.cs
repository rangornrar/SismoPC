namespace SismoPC.Core.Model;

public enum EpisodeKind
{
    /// <summary>Une ressource est restée saturée au-delà d'un seuil fixe.</summary>
    Slowdown = 0,

    /// <summary>Une mesure est sortie durablement de sa plage habituelle.</summary>
    Anomaly = 1,
}

/// <summary>Épisode détecté : ralentissement ou anomalie, avec son responsable probable.</summary>
public sealed record Episode(
    EpisodeKind Kind,
    Resource Resource,
    MetricKind Metric,
    DateTimeOffset Start,
    DateTimeOffset End,
    double PeakValue,
    double? UsualValue,
    string? Culprit,
    double CulpritShare)
{
    public TimeSpan Duration => End - Start;
}

public enum AlertSeverity
{
    Warning = 0,
    Critical = 1,
}

public enum AlertKind
{
    CpuTemperature = 0,
    GpuTemperature = 1,
    DiskTemperature = 2,
    DiskSmartFailure = 3,
    SsdWear = 4,
    LowDiskSpace = 5,
}

/// <summary>Alerte matérielle en cours.</summary>
/// <param name="Subject">Composant concerné, par exemple « D: » ou « Processeur ».</param>
public sealed record HardwareAlert(AlertKind Kind, AlertSeverity Severity, string Subject, string Message);
