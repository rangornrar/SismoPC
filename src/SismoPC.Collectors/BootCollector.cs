using System.Diagnostics.Eventing.Reader;
using System.Runtime.Versioning;

namespace SismoPC.Collectors;

/// <summary>
/// Durée du démarrage de Windows, lue dans le journal « Diagnostics-Performance » (événement 100).
/// Nécessite les droits administrateur.
/// </summary>
[SupportedOSPlatform("windows")]
public static class BootCollector
{
    private const string Log = "Microsoft-Windows-Diagnostics-Performance/Operational";

    /// <summary>Démarrages enregistrés depuis <paramref name="since"/> : date et durée en secondes.</summary>
    public static IReadOnlyList<(DateTimeOffset At, double Seconds)> ReadBoots(DateTimeOffset since)
    {
        var boots = new List<(DateTimeOffset, double)>();
        try
        {
            var from = since.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture);
            var query = new EventLogQuery(Log, PathType.LogName,
                $"*[System[(EventID=100) and TimeCreated[@SystemTime>='{from}']]]");
            using var reader = new EventLogReader(query);
            for (var e = reader.ReadEvent(); e is not null; e = reader.ReadEvent())
            {
                using (e)
                {
                    // Propriété 5 : BootTime en millisecondes.
                    if (e.TimeCreated is { } at && e.Properties.Count > 5 && e.Properties[5].Value is { } ms)
                    {
                        boots.Add((new DateTimeOffset(at), Convert.ToDouble(ms, System.Globalization.CultureInfo.InvariantCulture) / 1000));
                    }
                }
            }
        }
        catch (Exception ex) when (ex is EventLogException or UnauthorizedAccessException)
        {
            // Journal inaccessible : la durée de démarrage restera inconnue.
        }

        return boots;
    }
}
