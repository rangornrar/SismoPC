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

    /// <summary>Texte d'un tableau de caractères EDID (UserFriendlyName, ManufacturerName de WmiMonitorID).</summary>
    public static string EdidText(IEnumerable<ushort>? chars) =>
        chars is null ? string.Empty : new string(chars.TakeWhile(c => c != 0).Select(c => (char)c).ToArray()).Trim();
}
