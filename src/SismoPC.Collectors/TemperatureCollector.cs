using System.Runtime.Versioning;
using LibreHardwareMonitor.Hardware;

namespace SismoPC.Collectors;

/// <summary>
/// Températures du processeur et de la carte graphique par LibreHardwareMonitorLib (ADR-6).
/// Les capteurs de la carte mère et du processeur exigent le pilote PawnIO : s'il manque,
/// les valeurs restent inconnues et SismoPC continue sans elles.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class TemperatureCollector : IDisposable
{
    private static readonly string[] CpuPreferred = ["CPU Package", "Core (Tctl/Tdie)", "Core (Tctl)", "Core Max", "Core Average"];
    private static readonly string[] GpuPreferred = ["GPU Core", "GPU Hot Spot"];

    private readonly Computer? _computer;

    public TemperatureCollector()
    {
        try
        {
            var computer = new Computer { IsCpuEnabled = true, IsGpuEnabled = true };
            computer.Open();
            _computer = computer;
        }
#pragma warning disable CA1031 // Sans capteurs, SismoPC doit continuer à fonctionner.
        catch (Exception)
#pragma warning restore CA1031
        {
            _computer = null;
        }
    }

    public bool IsAvailable => _computer is not null;

    public (double? Cpu, double? Gpu) Read()
    {
        if (_computer is null)
        {
            return (null, null);
        }

        double? cpu = null;
        double? gpu = null;
        try
        {
            foreach (var hardware in _computer.Hardware)
            {
                hardware.Update();
                var temps = hardware.Sensors
                    .Where(s => s.SensorType == SensorType.Temperature && s.Value is { } v && v > 0 && v < 150)
                    .ToList();
                if (temps.Count == 0)
                {
                    continue;
                }

                if (hardware.HardwareType == HardwareType.Cpu)
                {
                    cpu = Max(cpu, Pick(temps, CpuPreferred));
                }
                else if (hardware.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel)
                {
                    gpu = Max(gpu, Pick(temps, GpuPreferred));
                }
            }
        }
#pragma warning disable CA1031 // Un capteur défaillant ne doit pas arrêter la collecte.
        catch (Exception)
#pragma warning restore CA1031
        {
            return (cpu, gpu);
        }

        return (cpu, gpu);
    }

    /// <summary>Le capteur préféré s'il existe, sinon la température la plus haute.</summary>
    private static double Pick(List<ISensor> temps, string[] preferred)
    {
        foreach (var name in preferred)
        {
            var match = temps.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
            if (match?.Value is { } value)
            {
                return value;
            }
        }

        return temps.Max(s => s.Value ?? 0);
    }

    private static double? Max(double? a, double b) => a is null ? b : Math.Max(a.Value, b);

    public void Dispose() => _computer?.Close();
}
