using System.Globalization;
using System.Runtime.Versioning;
using SismoPC.Core.Model;

namespace SismoPC.Collectors;

/// <summary>Inventaire de la configuration par WMI (US1).</summary>
[SupportedOSPlatform("windows")]
public static class InventoryCollector
{
    private const string Cimv2 = @"root\CIMV2";
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    public static IReadOnlyList<HardwareComponent> Read()
    {
        var list = new List<HardwareComponent>();

        foreach (var cpu in WmiHelper.Query(Cimv2, "SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor"))
        {
            list.Add(new("Processeur", cpu.Text("Name"), new Dictionary<string, string>
            {
                ["Cœurs / threads"] = $"{cpu.Text("NumberOfCores")} / {cpu.Text("NumberOfLogicalProcessors")}",
                ["Fréquence max"] = string.Format(Fr, "{0:0.0} GHz", (cpu.Double("MaxClockSpeed") ?? 0) / 1000),
            }));
        }

        foreach (var gpu in WmiHelper.Query(Cimv2, "SELECT Name, DriverVersion, DriverDate FROM Win32_VideoController"))
        {
            list.Add(new("Carte graphique", gpu.Text("Name"), new Dictionary<string, string>
            {
                ["Pilote"] = gpu.Text("DriverVersion"),
            }));
        }

        var sticks = WmiHelper.Query(Cimv2, "SELECT Capacity, Speed, Manufacturer, PartNumber FROM Win32_PhysicalMemory");
        if (sticks.Count > 0)
        {
            var total = sticks.Sum(s => s.Long("Capacity") ?? 0);
            list.Add(new("Mémoire", string.Format(Fr, "{0:0} Go", total / 1073741824.0), new Dictionary<string, string>
            {
                ["Barrettes"] = string.Join(" + ", sticks.Select(s => string.Format(Fr, "{0:0} Go", (s.Long("Capacity") ?? 0) / 1073741824.0))),
                ["Vitesse"] = $"{sticks[0].Text("Speed")} MHz",
            }));
        }

        foreach (var disk in WmiHelper.Query(Cimv2, "SELECT Model, Size, InterfaceType, SerialNumber FROM Win32_DiskDrive"))
        {
            list.Add(new("Disque", disk.Text("Model"), new Dictionary<string, string>
            {
                ["Capacité"] = string.Format(Fr, "{0:0} Go", (disk.Long("Size") ?? 0) / 1e9),
                ["Interface"] = disk.Text("InterfaceType"),
            }));
        }

        foreach (var board in WmiHelper.Query(Cimv2, "SELECT Manufacturer, Product FROM Win32_BaseBoard"))
        {
            list.Add(new("Carte mère", $"{board.Text("Manufacturer")} {board.Text("Product")}".Trim(), new Dictionary<string, string>()));
        }

        foreach (var bios in WmiHelper.Query(Cimv2, "SELECT SMBIOSBIOSVersion, Manufacturer, ReleaseDate FROM Win32_BIOS"))
        {
            list.Add(new("BIOS", bios.Text("SMBIOSBIOSVersion"), new Dictionary<string, string>
            {
                ["Fabricant"] = bios.Text("Manufacturer"),
            }));
        }

        foreach (var os in WmiHelper.Query(Cimv2, "SELECT Caption, Version, BuildNumber FROM Win32_OperatingSystem"))
        {
            list.Add(new("Système", os.Text("Caption"), new Dictionary<string, string>
            {
                ["Version"] = os.Text("Version"),
                ["Build"] = os.Text("BuildNumber"),
            }));
        }

        return list;
    }
}
