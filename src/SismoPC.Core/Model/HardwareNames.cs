using System.Globalization;

namespace SismoPC.Core.Model;

/// <summary>
/// Traduit les codes techniques renvoyés par Windows (SMBIOS, WMI, EDID) en libellés lisibles.
/// Sans dépendance à Windows, pour être testé partout.
/// </summary>
public static class HardwareNames
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>Type de mémoire (SMBIOSMemoryType de Win32_PhysicalMemory).</summary>
    public static string? MemoryType(long? smbiosType) => smbiosType switch
    {
        20 => "DDR",
        21 => "DDR2",
        24 => "DDR3",
        26 => "DDR4",
        27 => "LPDDR",
        28 => "LPDDR2",
        29 => "LPDDR3",
        30 => "LPDDR4",
        34 => "DDR5",
        35 => "LPDDR5",
        _ => null,
    };

    /// <summary>Format de barrette (FormFactor de Win32_PhysicalMemory).</summary>
    public static string? MemoryFormFactor(long? formFactor) => formFactor switch
    {
        8 => "DIMM (PC de bureau)",
        12 => "SO-DIMM (portable)",
        _ => null,
    };

    /// <summary>Bus d'un disque (BusType de MSFT_PhysicalDisk).</summary>
    public static string? DiskBus(long? busType) => busType switch
    {
        3 => "ATA",
        6 => "Fibre Channel",
        7 => "USB",
        8 => "RAID",
        10 => "SAS",
        11 => "SATA",
        12 => "Carte SD",
        13 => "MMC",
        17 => "NVMe",
        _ => null,
    };

    /// <summary>Type de média (MediaType de MSFT_PhysicalDisk).</summary>
    public static string? DiskMedia(long? mediaType) => mediaType switch
    {
        3 => "Disque dur",
        4 => "SSD",
        5 => "SCM",
        _ => null,
    };

    /// <summary>Marque d'un écran à partir de son code fabricant EDID (3 lettres).</summary>
    public static string MonitorBrand(string code) => code.Trim().ToUpperInvariant() switch
    {
        "ACI" or "AUS" => "Asus",
        "ACR" => "Acer",
        "AOC" => "AOC",
        "APP" => "Apple",
        "AUO" => "AU Optronics",
        "BNQ" => "BenQ",
        "BOE" => "BOE",
        "CMN" => "Chimei Innolux",
        "DEL" => "Dell",
        "GSM" => "LG",
        "HPN" or "HWP" => "HP",
        "HSD" => "HannStar",
        "IVM" => "Iiyama",
        "LEN" => "Lenovo",
        "LGD" => "LG Display",
        "MSI" => "MSI",
        "PHL" => "Philips",
        "SAM" or "SEC" => "Samsung",
        "SHP" => "Sharp",
        "SNY" => "Sony",
        "VSC" => "ViewSonic",
        var other => other,
    };

    /// <summary>Diagonale d'un écran en pouces, à partir de ses dimensions en centimètres.</summary>
    public static double? DiagonalInches(long? widthCm, long? heightCm) =>
        widthCm is > 0 && heightCm is > 0
            ? Math.Sqrt((double)(widthCm.Value * widthCm.Value + heightCm.Value * heightCm.Value)) / 2.54
            : null;

    /// <summary>Taille en octets lisible : « 8 Go », « 512 Mo ».</summary>
    public static string Bytes(double bytes) => bytes switch
    {
        >= 1024d * 1024 * 1024 => string.Format(Fr, "{0:0.#} Go", bytes / (1024d * 1024 * 1024)),
        >= 1024d * 1024 => string.Format(Fr, "{0:0} Mo", bytes / (1024d * 1024)),
        _ => string.Format(Fr, "{0:0} Ko", bytes / 1024d),
    };

    /// <summary>Capacité de disque en unités décimales, comme sur l'étiquette : « 1 To », « 512 Go ».</summary>
    public static string DiskSize(double bytes) => bytes >= 1e12
        ? string.Format(Fr, "{0:0.#} To", bytes / 1e12)
        : string.Format(Fr, "{0:0} Go", bytes / 1e9);

    /// <summary>Débit réseau lisible : « 1 Gbit/s », « 100 Mbit/s ».</summary>
    public static string LinkSpeed(double bitsPerSecond) => bitsPerSecond >= 1e9
        ? string.Format(Fr, "{0:0.#} Gbit/s", bitsPerSecond / 1e9)
        : string.Format(Fr, "{0:0} Mbit/s", bitsPerSecond / 1e6);

    /// <summary>
    /// Fabricant d'après un identifiant de vendeur PCI. Pour une carte graphique, le vendeur du sous-système
    /// est le fabricant de la carte (Asus, MSI…), le vendeur de la puce étant NVIDIA, AMD ou Intel.
    /// </summary>
    public static string? PciVendor(uint vendorId) => vendorId switch
    {
        0x10DE => "NVIDIA",
        0x1002 => "AMD",
        0x8086 => "Intel",
        0x1043 => "Asus",
        0x1462 => "MSI",
        0x1458 => "Gigabyte",
        0x3842 => "EVGA",
        0x19DA => "Zotac",
        0x174B or 0x1DA2 => "Sapphire",
        0x196E => "PNY",
        0x1569 => "Palit",
        0x10B0 => "Gainward",
        0x1B4C => "KFA2 / Galax",
        0x7377 => "Colorful",
        0x1849 => "ASRock",
        0x1682 => "XFX",
        0x148C => "PowerColor",
        0x1028 => "Dell",
        0x103C => "HP",
        0x17AA => "Lenovo",
        0x1025 => "Acer",
        0x144D => "Samsung",
        0x1414 => "Microsoft",
        _ => null,
    };

    /// <summary>
    /// Lit vendeur, puce et vendeur du sous-système dans un identifiant PnP, par exemple
    /// « PCI\VEN_10DE&amp;DEV_2484&amp;SUBSYS_146210DE&amp;REV_A1\… » → (0x10DE, 0x2484, 0x10DE).
    /// Dans SUBSYS_ddddvvvv, les 4 derniers chiffres sont le vendeur du sous-système.
    /// </summary>
    public static (uint Vendor, uint Device, uint SubsystemVendor)? ParsePciId(string? pnpDeviceId)
    {
        if (string.IsNullOrEmpty(pnpDeviceId))
        {
            return null;
        }

        static uint? Hex(string id, string key, int start, int length)
        {
            var i = id.IndexOf(key, StringComparison.OrdinalIgnoreCase);
            return i >= 0 && i + key.Length + start + length <= id.Length
                   && uint.TryParse(id.AsSpan(i + key.Length + start, length), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)
                ? v
                : null;
        }

        var ven = Hex(pnpDeviceId, "VEN_", 0, 4);
        var dev = Hex(pnpDeviceId, "DEV_", 0, 4);
        var sub = Hex(pnpDeviceId, "SUBSYS_", 4, 4);
        return ven is null || dev is null ? null : (ven.Value, dev.Value, sub ?? 0);
    }

    /// <summary>Marque d'un disque d'après le début de son nom de modèle.</summary>
    public static string? DiskBrand(string model)
    {
        var m = model.Trim().ToUpperInvariant();
        (string Prefix, string Brand)[] prefixes =
        [
            ("SAMSUNG", "Samsung"), ("WDC", "Western Digital"), ("WD", "Western Digital"), ("WESTERN DIGITAL", "Western Digital"),
            ("ST", "Seagate"), ("SEAGATE", "Seagate"), ("CT", "Crucial"), ("CRUCIAL", "Crucial"), ("MICRON", "Micron"),
            ("KINGSTON", "Kingston"), ("SKHYNIX", "SK hynix"), ("SK HYNIX", "SK hynix"), ("HFS", "SK hynix"),
            ("TOSHIBA", "Toshiba"), ("KIOXIA", "Kioxia"), ("INTEL", "Intel"), ("SANDISK", "SanDisk"),
            ("CORSAIR", "Corsair"), ("SABRENT", "Sabrent"), ("ADATA", "ADATA"), ("XPG", "ADATA"), ("PNY", "PNY"),
            ("TEAM", "Teamgroup"), ("LEXAR", "Lexar"), ("HGST", "HGST"), ("HITACHI", "Hitachi"), ("NETAC", "Netac"),
        ];
        return prefixes.FirstOrDefault(p => m.StartsWith(p.Prefix, StringComparison.Ordinal)).Brand;
    }

    /// <summary>Premier jour (lundi) d'une semaine de fabrication EDID, pour dater un écran.</summary>
    public static DateTime? WeekToDate(long? year, long? week)
    {
        if (year is not (>= 1990 and <= 2100))
        {
            return null;
        }

        return week is >= 1 and <= 53
            ? ISOWeek.ToDateTime((int)year.Value, (int)Math.Min(week.Value, ISOWeek.GetWeeksInYear((int)year.Value)), DayOfWeek.Monday)
            : new DateTime((int)year.Value, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);
    }

    /// <summary>Texte d'un tableau de caractères EDID (UserFriendlyName, ManufacturerName de WmiMonitorID).</summary>
    public static string EdidText(IEnumerable<ushort>? chars) =>
        chars is null ? string.Empty : new string(chars.TakeWhile(c => c != 0).Select(c => (char)c).ToArray()).Trim();
}
