namespace SismoPC.Core.Model;

/// <summary>État de santé d'un disque physique.</summary>
/// <param name="Name">Nom du modèle.</param>
/// <param name="IsSsd">Vrai pour un SSD.</param>
/// <param name="PredictsFailure">Le disque signale lui-même un risque de panne (SMART).</param>
/// <param name="WearPercent">Usure d'un SSD, de 0 à 100, si connue.</param>
/// <param name="TemperatureC">Température, si connue.</param>
/// <param name="PowerOnHours">Heures de fonctionnement, si connues.</param>
public sealed record DiskHealth(
    string Name,
    bool IsSsd,
    bool PredictsFailure,
    double? WearPercent,
    double? TemperatureC,
    long? PowerOnHours);

/// <summary>Espace d'un volume (lettre de lecteur).</summary>
public sealed record VolumeSpace(string Letter, long TotalBytes, long FreeBytes)
{
    public double FreePercent => TotalBytes <= 0 ? 100 : 100.0 * FreeBytes / TotalBytes;
}

/// <summary>Composant matériel inventorié.</summary>
/// <param name="Type">Famille : Processeur, Carte graphique, Mémoire, Disque, Carte mère, BIOS, Système.</param>
/// <param name="Name">Désignation lisible.</param>
/// <param name="Details">Caractéristiques, libellé → valeur.</param>
public sealed record HardwareComponent(string Type, string Name, IReadOnlyDictionary<string, string> Details);

/// <summary>Photographie de l'état matériel utilisée pour les alertes.</summary>
public sealed record HardwareState(
    double? CpuTemperatureC,
    double? GpuTemperatureC,
    IReadOnlyList<DiskHealth> Disks,
    IReadOnlyList<VolumeSpace> Volumes);
