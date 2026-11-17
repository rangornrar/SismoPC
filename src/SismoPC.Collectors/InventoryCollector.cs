using System.Globalization;
using System.Management;
using System.Runtime.Versioning;
using Microsoft.Win32;
using SismoPC.Core.Model;

namespace SismoPC.Collectors;

/// <summary>
/// Inventaire détaillé de la configuration (US1) : WMI, registre et micrologiciel.
/// Chaque famille est lue séparément : une information indisponible n'empêche jamais les autres.
/// </summary>
[SupportedOSPlatform("windows")]
public static class InventoryCollector
{
    private const string Cimv2 = @"root\CIMV2";
    private const string DisplayClassKey = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    public static IReadOnlyList<HardwareComponent> Read()
    {
        var list = new List<HardwareComponent>();
        list.AddRange(Processors());
        list.AddRange(GraphicsCards());
        list.AddRange(MemorySticks());
        list.AddRange(Disks());
        list.AddRange(Motherboard());
        list.AddRange(Bios());
        list.AddRange(Monitors());
        list.AddRange(NetworkAdapters());
        list.AddRange(WindowsInfo());
        return list;
    }

    private static IEnumerable<HardwareComponent> Processors()
    {
        foreach (var cpu in WmiHelper.Query(Cimv2,
            "SELECT Name, Manufacturer, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed, L2CacheSize, L3CacheSize, SocketDesignation, VirtualizationFirmwareEnabled FROM Win32_Processor"))
        {
            var d = new Details();
            d.Add("Fabricant", Vendor(cpu.Text("Manufacturer")));
            d.Add("Cœurs / threads", $"{cpu.Text("NumberOfCores")} / {cpu.Text("NumberOfLogicalProcessors")}");
            d.Add("Fréquence de base", cpu.Double("MaxClockSpeed") is { } mhz ? string.Format(Fr, "{0:0.0#} GHz", mhz / 1000) : null);
            d.Add("Cache L2", cpu.Long("L2CacheSize") is { } l2 and > 0 ? HardwareNames.Bytes(l2 * 1024.0) : null);
            d.Add("Cache L3", cpu.Long("L3CacheSize") is { } l3 and > 0 ? HardwareNames.Bytes(l3 * 1024.0) : null);
            d.Add("Support", cpu.Text("SocketDesignation"));
            d.Add("Virtualisation", cpu.Text("VirtualizationFirmwareEnabled") switch
            {
                "True" => "Activée dans le BIOS",
                "False" => "Désactivée dans le BIOS",
                _ => null,
            });
            yield return new("Processeur", cpu.Text("Name"), d.Values);
        }
    }

    private static IEnumerable<HardwareComponent> GraphicsCards()
    {
        var vram = VideoMemoryFromRegistry();
        foreach (var gpu in WmiHelper.Query(Cimv2,
            "SELECT Name, AdapterCompatibility, DriverVersion, DriverDate, AdapterRAM, VideoProcessor, CurrentHorizontalResolution, CurrentVerticalResolution, CurrentRefreshRate FROM Win32_VideoController"))
        {
            var name = gpu.Text("Name");
            var d = new Details();
            d.Add("Marque", Vendor(gpu.Text("AdapterCompatibility")));

            // AdapterRAM (WMI) plafonne à 4 Go : la valeur exacte est lue dans le registre du pilote.
            var memory = vram.TryGetValue(name, out var bytes) ? bytes : gpu.Long("AdapterRAM");
            d.Add("Mémoire vidéo", memory is > 0 ? HardwareNames.Bytes(memory.Value) : null);
            d.Add("Pilote", gpu.Text("DriverVersion"));
            d.Add("Date du pilote", CimDate(gpu.Text("DriverDate")));
            if (gpu.Long("CurrentHorizontalResolution") is { } w and > 0 && gpu.Long("CurrentVerticalResolution") is { } h and > 0)
            {
                var hz = gpu.Long("CurrentRefreshRate") is { } r and > 1 ? $" à {r} Hz" : string.Empty;
                d.Add("Affichage", $"{w} × {h}{hz}");
            }

            yield return new("Carte graphique", name, d.Values);
        }
    }

    /// <summary>Mémoire vidéo exacte par nom de carte : HardwareInformation.qwMemorySize (ou MemorySize) du pilote.</summary>
    private static Dictionary<string, long> VideoMemoryFromRegistry()
    {
        var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var classKey = Registry.LocalMachine.OpenSubKey(DisplayClassKey);
            foreach (var sub in classKey?.GetSubKeyNames() ?? [])
            {
                if (!sub.All(char.IsDigit))
                {
                    continue;
                }

                using var key = classKey!.OpenSubKey(sub);
                if (key?.GetValue("DriverDesc") is not string desc)
                {
                    continue;
                }

                var size = ToLong(key.GetValue("HardwareInformation.qwMemorySize"))
                           ?? ToLong(key.GetValue("HardwareInformation.MemorySize"));
                if (size is > 0)
                {
                    result[desc] = Math.Max(result.GetValueOrDefault(desc), size.Value);
                }
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            // Registre inaccessible : on garde la valeur WMI.
        }

        return result;
    }

    private static IEnumerable<HardwareComponent> MemorySticks()
    {
        var sticks = WmiHelper.Query(Cimv2,
            "SELECT Capacity, Speed, ConfiguredClockSpeed, Manufacturer, PartNumber, DeviceLocator, SMBIOSMemoryType, FormFactor FROM Win32_PhysicalMemory");
        if (sticks.Count == 0)
        {
            yield break;
        }

        var total = sticks.Sum(s => s.Long("Capacity") ?? 0);
        var type = sticks.Select(s => HardwareNames.MemoryType(s.Long("SMBIOSMemoryType"))).FirstOrDefault(t => t is not null);
        var d = new Details();
        d.Add("Type", type);
        d.Add("Format", sticks.Select(s => HardwareNames.MemoryFormFactor(s.Long("FormFactor"))).FirstOrDefault(f => f is not null));
        d.Add("Vitesse", sticks[0].Long("ConfiguredClockSpeed") is { } configured and > 0
            ? $"{configured} MT/s (max {sticks[0].Text("Speed")} MT/s)"
            : $"{sticks[0].Text("Speed")} MT/s");

        var slots = WmiHelper.Query(Cimv2, "SELECT MemoryDevices FROM Win32_PhysicalMemoryArray").Sum(a => a.Long("MemoryDevices") ?? 0);
        d.Add("Emplacements", slots > 0 ? $"{sticks.Count} occupé{(sticks.Count > 1 ? "s" : "")} sur {slots}" : $"{sticks.Count} barrette{(sticks.Count > 1 ? "s" : "")}");

        var i = 1;
        foreach (var s in sticks)
        {
            var maker = Vendor(s.Text("Manufacturer"));
            var part = s.Text("PartNumber");
            var label = string.Join(" · ", new[]
            {
                HardwareNames.Bytes(s.Long("Capacity") ?? 0),
                string.IsNullOrWhiteSpace(maker) || maker.StartsWith("Unknown", StringComparison.OrdinalIgnoreCase) ? null : maker,
                string.IsNullOrWhiteSpace(part) ? null : part,
            }.Where(x => x is not null));
            var slot = s.Text("DeviceLocator");
            d.Add(string.IsNullOrWhiteSpace(slot) ? $"Barrette {i}" : slot, label);
            i++;
        }

        var name = type is null ? HardwareNames.Bytes(total) : $"{HardwareNames.Bytes(total)} {type}";
        yield return new("Mémoire", name, d.Values);
    }

    private static IEnumerable<HardwareComponent> Disks()
    {
        const string storage = @"root\Microsoft\Windows\Storage";
        var physical = WmiHelper.Query(storage, "SELECT FriendlyName, BusType, MediaType, Size, FirmwareVersion, SpindleSpeed FROM MSFT_PhysicalDisk");
        if (physical.Count > 0)
        {
            foreach (var disk in physical)
            {
                var d = new Details();
                d.Add("Capacité", disk.Long("Size") is { } size and > 0 ? HardwareNames.DiskSize(size) : null);
                d.Add("Type", HardwareNames.DiskMedia(disk.Long("MediaType")));
                d.Add("Interface", HardwareNames.DiskBus(disk.Long("BusType")));
                d.Add("Vitesse de rotation", disk.Long("SpindleSpeed") is { } rpm and > 0 and < 100000 ? $"{rpm} tr/min" : null);
                d.Add("Micrologiciel", disk.Text("FirmwareVersion"));
                yield return new("Disque", disk.Text("FriendlyName"), d.Values);
            }

            yield break;
        }

        // Repli sans l'API de stockage (droits insuffisants) : Win32_DiskDrive.
        foreach (var disk in WmiHelper.Query(Cimv2, "SELECT Model, Size, InterfaceType FROM Win32_DiskDrive"))
        {
            var d = new Details();
            d.Add("Capacité", disk.Long("Size") is { } size and > 0 ? HardwareNames.DiskSize(size) : null);
            d.Add("Interface", disk.Text("InterfaceType"));
            yield return new("Disque", disk.Text("Model"), d.Values);
        }
    }

    private static IEnumerable<HardwareComponent> Motherboard()
    {
        var system = WmiHelper.Query(Cimv2, "SELECT Manufacturer, Model, SystemFamily, PCSystemType FROM Win32_ComputerSystem").FirstOrDefault();
        foreach (var board in WmiHelper.Query(Cimv2, "SELECT Manufacturer, Product, Version FROM Win32_BaseBoard"))
        {
            var d = new Details();
            d.Add("Fabricant", Vendor(board.Text("Manufacturer")));
            d.Add("Modèle", board.Text("Product"));
            d.Add("Révision", Meaningful(board.Text("Version")));

            if (system is not null)
            {
                d.Add("Ordinateur", Meaningful($"{Vendor(system.Text("Manufacturer"))} {system.Text("Model")}".Trim()));
                d.Add("Type", system.Long("PCSystemType") switch
                {
                    1 => "PC de bureau",
                    2 => "Portable",
                    3 => "Station de travail",
                    _ => null,
                });
            }

            var array = WmiHelper.Query(Cimv2, "SELECT MemoryDevices, MaxCapacityEx, MaxCapacity FROM Win32_PhysicalMemoryArray").FirstOrDefault();
            if (array is not null)
            {
                d.Add("Emplacements mémoire", array.Text("MemoryDevices"));
                var maxKb = array.Long("MaxCapacityEx") is { } ex and > 0 ? ex : array.Long("MaxCapacity");
                d.Add("Mémoire maximale", maxKb is > 0 ? HardwareNames.Bytes(maxKb.Value * 1024.0) : null);
            }

            var name = $"{Vendor(board.Text("Manufacturer"))} {board.Text("Product")}".Trim();
            yield return new("Carte mère", name, d.Values);
        }
    }

    private static IEnumerable<HardwareComponent> Bios()
    {
        foreach (var bios in WmiHelper.Query(Cimv2, "SELECT Manufacturer, SMBIOSBIOSVersion, ReleaseDate, SMBIOSMajorVersion, SMBIOSMinorVersion FROM Win32_BIOS"))
        {
            var d = new Details();
            d.Add("Fabricant", bios.Text("Manufacturer"));
            d.Add("Version", bios.Text("SMBIOSBIOSVersion"));
            d.Add("Date", CimDate(bios.Text("ReleaseDate")));
            d.Add("Mode de démarrage", FirmwareMode());
            d.Add("Secure Boot", SecureBoot());
            d.Add("Puce TPM", Tpm());
            d.Add("SMBIOS", $"{bios.Text("SMBIOSMajorVersion")}.{bios.Text("SMBIOSMinorVersion")}".Trim('.'));
            yield return new("BIOS", bios.Text("SMBIOSBIOSVersion"), d.Values);
        }
    }

    private static string? FirmwareMode() => NativeMethods.FirmwareType() switch
    {
        1 => "BIOS ancien (Legacy)",
        2 => "UEFI",
        _ => null,
    };

    private static string? SecureBoot()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State");
            return key?.GetValue("UEFISecureBootEnabled") switch
            {
                1 => "Activé",
                0 => "Désactivé",
                _ => null,
            };
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    private static string? Tpm()
    {
        var tpm = WmiHelper.Query(@"root\CIMV2\Security\MicrosoftTpm", "SELECT SpecVersion, IsEnabled_InitialValue, IsActivated_InitialValue FROM Win32_Tpm").FirstOrDefault();
        if (tpm is null)
        {
            return "Absente ou désactivée";
        }

        var version = tpm.Text("SpecVersion").Split(',')[0].Trim();
        var enabled = tpm.Text("IsEnabled_InitialValue") == "True" && tpm.Text("IsActivated_InitialValue") == "True";
        return $"TPM {version}{(enabled ? ", active" : ", inactive")}";
    }

    private static IEnumerable<HardwareComponent> Monitors()
    {
        const string wmi = @"root\wmi";
        var sizes = WmiHelper.Query(wmi, "SELECT InstanceName, MaxHorizontalImageSize, MaxVerticalImageSize FROM WmiMonitorBasicDisplayParams")
            .GroupBy(r => r.Text("InstanceName"), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var index = 1;
        foreach (var monitor in WmiHelper.Query(wmi, "SELECT InstanceName, ManufacturerName, UserFriendlyName, YearOfManufacture FROM WmiMonitorID"))
        {
            var model = HardwareNames.EdidText(monitor.TryGetValue("UserFriendlyName", out var n) ? n as ushort[] : null);
            var brand = HardwareNames.MonitorBrand(HardwareNames.EdidText(monitor.TryGetValue("ManufacturerName", out var m) ? m as ushort[] : null));
            var d = new Details();
            d.Add("Marque", brand);
            if (sizes.TryGetValue(monitor.Text("InstanceName"), out var size)
                && HardwareNames.DiagonalInches(size.Long("MaxHorizontalImageSize"), size.Long("MaxVerticalImageSize")) is { } inches)
            {
                d.Add("Taille", string.Format(Fr, "{0:0} pouces ({1} × {2} cm)", inches, size.Text("MaxHorizontalImageSize"), size.Text("MaxVerticalImageSize")));
            }

            d.Add("Année de fabrication", monitor.Long("YearOfManufacture") is { } year and > 1990 ? year.ToString(CultureInfo.InvariantCulture) : null);
            var name = string.IsNullOrWhiteSpace(model) ? $"Écran {index}" : model;
            yield return new("Écran", name, d.Values);
            index++;
        }
    }

    private static IEnumerable<HardwareComponent> NetworkAdapters()
    {
        foreach (var nic in WmiHelper.Query(Cimv2, "SELECT Name, Manufacturer, Speed, NetConnectionStatus, NetConnectionID FROM Win32_NetworkAdapter WHERE PhysicalAdapter = TRUE"))
        {
            var name = nic.Text("Name");
            if (name.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase) || name.Contains("Virtual", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var connected = nic.Long("NetConnectionStatus") == 2;
            var d = new Details();
            d.Add("Connexion", nic.Text("NetConnectionID"));
            d.Add("Fabricant", Vendor(nic.Text("Manufacturer")));
            d.Add("État", connected ? "Connectée" : "Non connectée");
            d.Add("Débit du lien", connected && nic.Double("Speed") is { } bps and > 0 and < 1e12 ? HardwareNames.LinkSpeed(bps) : null);
            yield return new("Réseau", name, d.Values);
        }
    }

    private static IEnumerable<HardwareComponent> WindowsInfo()
    {
        string? displayVersion = null;
        string? build = null;
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            displayVersion = key?.GetValue("DisplayVersion") as string;
            if (key?.GetValue("CurrentBuildNumber") is string number)
            {
                build = key.GetValue("UBR") is int ubr ? $"{number}.{ubr}" : number;
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            // Version détaillée indisponible : on garde celle de WMI.
        }

        foreach (var os in WmiHelper.Query(Cimv2, "SELECT Caption, Version, BuildNumber, OSArchitecture, InstallDate, LastBootUpTime FROM Win32_OperatingSystem"))
        {
            var caption = os.Text("Caption").Replace("Microsoft ", string.Empty, StringComparison.Ordinal);
            var d = new Details();
            d.Add("Version", displayVersion);
            d.Add("Build", build ?? os.Text("BuildNumber"));
            d.Add("Architecture", os.Text("OSArchitecture"));
            d.Add("Installé le", CimDate(os.Text("InstallDate")));
            d.Add("Dernier démarrage", CimDateTime(os.Text("LastBootUpTime")));
            var name = displayVersion is null ? caption : $"{caption} {displayVersion}";
            yield return new("Système", name, d.Values);
        }
    }

    // ---------- Outils ----------

    /// <summary>Noms de fabricants harmonisés (WMI renvoie souvent des raisons sociales ou des identifiants).</summary>
    private static string Vendor(string raw)
    {
        var v = raw.Trim();
        return v switch
        {
            "GenuineIntel" or "Intel Corporation" => "Intel",
            "AuthenticAMD" or "Advanced Micro Devices, Inc." or "AMD" => "AMD",
            "NVIDIA" or "NVIDIA Corporation" => "NVIDIA",
            "Micro-Star International Co., Ltd." or "Micro-Star International Co.,Ltd." => "MSI",
            "ASUSTeK COMPUTER INC." or "ASUSTeK Computer INC." => "Asus",
            "Gigabyte Technology Co., Ltd." => "Gigabyte",
            "ASRock" => "ASRock",
            "Dell Inc." => "Dell",
            "HP" or "Hewlett-Packard" => "HP",
            "LENOVO" => "Lenovo",
            _ when v.StartsWith("Samsung", StringComparison.OrdinalIgnoreCase) || v == "00CE00000000" || v == "CE00000000000000" => "Samsung",
            _ when v.StartsWith("Kingston", StringComparison.OrdinalIgnoreCase) || v == "9801" => "Kingston",
            _ when v.StartsWith("Micron", StringComparison.OrdinalIgnoreCase) || v == "2C00" || v == "802C" => "Micron / Crucial",
            _ when v.StartsWith("SK Hynix", StringComparison.OrdinalIgnoreCase) || v == "AD00" || v == "80AD" => "SK hynix",
            _ when v.Contains("Corsair", StringComparison.OrdinalIgnoreCase) || v == "029E" => "Corsair",
            _ when v.Contains("G.Skill", StringComparison.OrdinalIgnoreCase) || v == "04CD" => "G.Skill",
            _ => v,
        };
    }

    /// <summary>Valeur utile ou null pour les remplissages d'usine (« To be filled by O.E.M. », « Default string »…).</summary>
    private static string? Meaningful(string value) =>
        string.IsNullOrWhiteSpace(value)
        || value.Contains("O.E.M", StringComparison.OrdinalIgnoreCase)
        || value.Equals("Default string", StringComparison.OrdinalIgnoreCase)
        || value.Equals("System Product Name", StringComparison.OrdinalIgnoreCase)
            ? null
            : value;

    private static string? CimDate(string cim)
    {
        if (string.IsNullOrWhiteSpace(cim))
        {
            return null;
        }

        try
        {
            return ManagementDateTimeConverter.ToDateTime(cim).ToString("d MMMM yyyy", Fr);
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or FormatException)
        {
            return null;
        }
    }

    private static string? CimDateTime(string cim)
    {
        if (string.IsNullOrWhiteSpace(cim))
        {
            return null;
        }

        try
        {
            return ManagementDateTimeConverter.ToDateTime(cim).ToString("d MMMM yyyy à HH:mm", Fr);
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or FormatException)
        {
            return null;
        }
    }

    private static long? ToLong(object? value) => value switch
    {
        long l => l,
        int i => (uint)i,
        byte[] { Length: 8 } b => BitConverter.ToInt64(b, 0),
        byte[] { Length: 4 } b => BitConverter.ToUInt32(b, 0),
        _ => null,
    };

    /// <summary>Détails ordonnés ; les valeurs vides sont ignorées.</summary>
    private sealed class Details
    {
        public Dictionary<string, string> Values { get; } = [];

        public void Add(string label, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                Values[label] = value.Trim();
            }
        }
    }
}
