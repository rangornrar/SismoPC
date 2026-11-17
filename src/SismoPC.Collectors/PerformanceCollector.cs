using System.Diagnostics;
using System.Runtime.Versioning;
using SismoPC.Core.Model;

namespace SismoPC.Collectors;

/// <summary>
/// Lit l'activité système par les compteurs de performance Windows (ADR-7) :
/// processeur, mémoire, disque, carte graphique, réseau. Chaque compteur absent est ignoré sans bloquer les autres.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class PerformanceCollector : IDisposable
{
    private readonly PerformanceCounter? _cpu;
    private readonly PerformanceCounter? _committed;
    private readonly PerformanceCounter? _diskIdle;
    private readonly PerformanceCounter? _diskLatency;
    private readonly List<PerformanceCounter> _network = [];
    private readonly List<PerformanceCounter> _gpu3d = [];
    private DateTimeOffset _lastGpuRefresh = DateTimeOffset.MinValue;

    public PerformanceCollector()
    {
        // « % Processor Utility » correspond au Gestionnaire des tâches ; repli sur « % Processor Time ».
        _cpu = TryCreate("Processor Information", "% Processor Utility", "_Total")
               ?? TryCreate("Processor", "% Processor Time", "_Total");
        _committed = TryCreate("Memory", "% Committed Bytes In Use", string.Empty);
        _diskIdle = TryCreate("PhysicalDisk", "% Idle Time", "_Total");
        _diskLatency = TryCreate("PhysicalDisk", "Avg. Disk sec/Transfer", "_Total");

        if (PerformanceCounterCategory.Exists("Network Interface"))
        {
            foreach (var instance in new PerformanceCounterCategory("Network Interface").GetInstanceNames())
            {
                if (TryCreate("Network Interface", "Bytes Total/sec", instance) is { } c)
                {
                    _network.Add(c);
                }
            }
        }

        // Premier appel de NextValue : toujours 0, il sert d'amorce.
        _ = Read(DateTimeOffset.UtcNow);
    }

    /// <summary>Lit toutes les mesures disponibles à l'instant.</summary>
    public Dictionary<MetricKind, double> Read(DateTimeOffset now)
    {
        var values = new Dictionary<MetricKind, double>();

        if (Next(_cpu) is { } cpu)
        {
            values[MetricKind.CpuPercent] = Math.Clamp(cpu, 0, 100);
        }

        if (NativeMethods.MemoryStatus() is { } mem)
        {
            values[MetricKind.MemoryUsedPercent] = mem.MemoryLoad;
        }

        if (Next(_committed) is { } committed)
        {
            values[MetricKind.MemoryCommittedPercent] = committed;
        }

        if (Next(_diskIdle) is { } idle)
        {
            values[MetricKind.DiskActivePercent] = Math.Clamp(100 - idle, 0, 100);
        }

        if (Next(_diskLatency) is { } latency)
        {
            values[MetricKind.DiskLatencyMs] = latency * 1000;
        }

        if (_network.Count > 0)
        {
            values[MetricKind.NetworkBytesPerSecond] = _network.Sum(c => Next(c) ?? 0);
        }

        if (ReadGpu(now) is { } gpu)
        {
            values[MetricKind.GpuPercent] = gpu;
        }

        return values;
    }

    /// <summary>Utilisation 3D de la carte graphique : somme des moteurs « engtype_3D » (comme le Gestionnaire des tâches).</summary>
    private double? ReadGpu(DateTimeOffset now)
    {
        // Les instances GPU changent quand des programmes démarrent : on les relit toutes les minutes.
        if (now - _lastGpuRefresh > TimeSpan.FromMinutes(1))
        {
            _lastGpuRefresh = now;
            foreach (var c in _gpu3d)
            {
                c.Dispose();
            }

            _gpu3d.Clear();
            if (PerformanceCounterCategory.Exists("GPU Engine"))
            {
                foreach (var instance in new PerformanceCounterCategory("GPU Engine").GetInstanceNames())
                {
                    if (instance.EndsWith("engtype_3D", StringComparison.OrdinalIgnoreCase)
                        && TryCreate("GPU Engine", "Utilization Percentage", instance) is { } c)
                    {
                        _ = Next(c);
                        _gpu3d.Add(c);
                    }
                }
            }

            return null;
        }

        return _gpu3d.Count == 0 ? null : Math.Clamp(_gpu3d.Sum(c => Next(c) ?? 0), 0, 100);
    }

    private static PerformanceCounter? TryCreate(string category, string counter, string instance)
    {
        try
        {
            return PerformanceCounterCategory.Exists(category)
                ? new PerformanceCounter(category, counter, instance, readOnly: true)
                : null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private static double? Next(PerformanceCounter? counter)
    {
        if (counter is null)
        {
            return null;
        }

        try
        {
            return counter.NextValue();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    public void Dispose()
    {
        _cpu?.Dispose();
        _committed?.Dispose();
        _diskIdle?.Dispose();
        _diskLatency?.Dispose();
        foreach (var c in _network.Concat(_gpu3d))
        {
            c.Dispose();
        }
    }
}
