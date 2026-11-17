namespace SismoPC.Core.Model;

/// <summary>Ressource du PC susceptible de saturer.</summary>
public enum Resource
{
    Cpu = 0,
    Memory = 1,
    Disk = 2,
    Gpu = 3,
    Network = 4,
}

/// <summary>Mesure système collectée à intervalle régulier.</summary>
public enum MetricKind
{
    CpuPercent = 0,
    MemoryUsedPercent = 1,
    MemoryCommittedPercent = 2,
    DiskActivePercent = 3,
    DiskLatencyMs = 4,
    GpuPercent = 5,
    NetworkBytesPerSecond = 6,
    CpuTemperatureC = 7,
    GpuTemperatureC = 8,
}

/// <summary>Consommation d'un programme sur un intervalle de collecte.</summary>
/// <param name="Name">Nom de l'exécutable, par exemple « chrome.exe ».</param>
/// <param name="CpuPercent">Part du processeur total, de 0 à 100.</param>
/// <param name="WorkingSetBytes">Mémoire physique utilisée.</param>
/// <param name="DiskBytesPerSecond">Débit de lecture et d'écriture.</param>
public sealed record ProcessUsage(string Name, double CpuPercent, long WorkingSetBytes, double DiskBytesPerSecond);

/// <summary>État du PC à un instant : toutes les mesures et les programmes les plus gourmands.</summary>
public sealed record Snapshot(
    DateTimeOffset Timestamp,
    IReadOnlyDictionary<MetricKind, double> Metrics,
    IReadOnlyList<ProcessUsage> TopProcesses)
{
    public double? this[MetricKind kind] => Metrics.TryGetValue(kind, out var v) ? v : null;
}
