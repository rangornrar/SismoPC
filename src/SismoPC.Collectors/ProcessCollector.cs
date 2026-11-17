using System.Diagnostics;
using System.Runtime.Versioning;
using SismoPC.Core.Model;

namespace SismoPC.Collectors;

/// <summary>
/// Mesure la consommation de chaque programme entre deux collectes (processeur, mémoire, disque)
/// et renvoie les plus gourmands. Le service tournant sous le compte système, il voit aussi les services Windows.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ProcessCollector
{
    private readonly int _perDimension;
    private readonly Dictionary<int, (TimeSpan Cpu, ulong Io, string Name)> _previous = [];
    private DateTimeOffset _previousAt;

    /// <param name="perDimension">Nombre de programmes retenus par critère (processeur, mémoire, disque).</param>
    public ProcessCollector(int perDimension = 5)
    {
        _perDimension = perDimension;
    }

    public IReadOnlyList<ProcessUsage> Read(DateTimeOffset now)
    {
        var elapsed = (now - _previousAt).TotalSeconds;
        var first = _previousAt == default || elapsed <= 0;
        var cpuCount = Environment.ProcessorCount;
        var current = new Dictionary<int, (TimeSpan Cpu, ulong Io, string Name)>();
        var usages = new Dictionary<string, (double Cpu, long Ws, double Disk)>(StringComparer.OrdinalIgnoreCase);

        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                if (p.Id is 0 or 4)
                {
                    continue; // Processus inactif et noyau
                }

                try
                {
                    var name = p.ProcessName + ".exe";
                    var cpuTime = p.TotalProcessorTime;
                    var io = NativeMethods.GetProcessIoCounters(p.Handle, out var counters)
                        ? counters.ReadTransferCount + counters.WriteTransferCount
                        : 0UL;
                    current[p.Id] = (cpuTime, io, name);

                    double cpu = 0, disk = 0;
                    if (!first && _previous.TryGetValue(p.Id, out var prev) && prev.Name == name)
                    {
                        cpu = Math.Max(0, (cpuTime - prev.Cpu).TotalSeconds / (elapsed * cpuCount) * 100);
                        disk = io >= prev.Io ? (io - prev.Io) / elapsed : 0;
                    }

                    // Plusieurs processus du même programme (navigateurs) sont regroupés.
                    var sum = usages.GetValueOrDefault(name);
                    usages[name] = (sum.Cpu + cpu, sum.Ws + p.WorkingSet64, sum.Disk + disk);
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
                {
                    // Processus terminé entre-temps ou protégé : ignoré.
                }
            }
        }

        _previous.Clear();
        foreach (var kv in current)
        {
            _previous[kv.Key] = kv.Value;
        }

        _previousAt = now;

        var all = usages.Select(kv => new ProcessUsage(kv.Key, kv.Value.Cpu, kv.Value.Ws, kv.Value.Disk)).ToList();
        return all.OrderByDescending(u => u.CpuPercent).Take(_perDimension)
            .Concat(all.OrderByDescending(u => u.WorkingSetBytes).Take(_perDimension))
            .Concat(all.OrderByDescending(u => u.DiskBytesPerSecond).Take(_perDimension))
            .DistinctBy(u => u.Name)
            .ToList();
    }
}
