using System.Runtime.Versioning;
using SismoPC.Core.Model;

namespace SismoPC.Collectors;

/// <summary>
/// Santé des disques par l'API de stockage de Windows (MSFT_PhysicalDisk et MSFT_StorageReliabilityCounter) :
/// état, usure des SSD, température, heures de fonctionnement. Nécessite les droits administrateur.
/// </summary>
[SupportedOSPlatform("windows")]
public static class DiskHealthCollector
{
    private const string Storage = @"root\Microsoft\Windows\Storage";

    public static IReadOnlyList<DiskHealth> Read()
    {
        var reliability = WmiHelper.Query(Storage, "SELECT DeviceId, Wear, Temperature, PowerOnHours FROM MSFT_StorageReliabilityCounter")
            .GroupBy(r => r.Text("DeviceId"), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var disks = new List<DiskHealth>();
        foreach (var d in WmiHelper.Query(Storage, "SELECT DeviceId, FriendlyName, MediaType, HealthStatus FROM MSFT_PhysicalDisk"))
        {
            reliability.TryGetValue(d.Text("DeviceId"), out var r);

            // MediaType : 3 = disque dur, 4 = SSD. HealthStatus : 0 = sain, 1 = avertissement, 2 = défaillant.
            var isSsd = d.Long("MediaType") == 4;
            var failing = d.Long("HealthStatus") is 1 or 2;
            var temp = r?.Double("Temperature");

            disks.Add(new DiskHealth(
                Name: d.Text("FriendlyName"),
                IsSsd: isSsd,
                PredictsFailure: failing,
                WearPercent: isSsd ? r?.Double("Wear") : null,
                TemperatureC: temp is > 0 ? temp : null,
                PowerOnHours: r?.Long("PowerOnHours")));
        }

        return disks;
    }

    /// <summary>Espace libre de chaque volume fixe.</summary>
    public static IReadOnlyList<VolumeSpace> Volumes() =>
        DriveInfo.GetDrives()
            .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
            .Select(d => new VolumeSpace(d.Name.TrimEnd('\\'), d.TotalSize, d.AvailableFreeSpace))
            .ToList();
}
