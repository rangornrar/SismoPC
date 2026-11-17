using System.Globalization;
using System.Management;
using System.Runtime.Versioning;

namespace SismoPC.Collectors;

[SupportedOSPlatform("windows")]
internal static class WmiHelper
{
    /// <summary>Exécute une requête WMI ; renvoie une liste vide si la classe est absente ou l'accès refusé.</summary>
    public static List<Dictionary<string, object?>> Query(string scope, string wql)
    {
        var rows = new List<Dictionary<string, object?>>();
        try
        {
            using var searcher = new ManagementObjectSearcher(scope, wql);
            using var results = searcher.Get();
            foreach (var obj in results)
            {
                using (obj)
                {
                    var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                    foreach (var prop in obj.Properties)
                    {
                        row[prop.Name] = prop.Value;
                    }

                    rows.Add(row);
                }
            }
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            // Classe indisponible sur ce PC : on continue sans.
        }

        return rows;
    }

    public static string Text(this Dictionary<string, object?> row, string key) =>
        row.TryGetValue(key, out var v) && v is not null ? Convert.ToString(v, CultureInfo.InvariantCulture)?.Trim() ?? string.Empty : string.Empty;

    public static long? Long(this Dictionary<string, object?> row, string key) =>
        row.TryGetValue(key, out var v) && v is not null ? Convert.ToInt64(v, CultureInfo.InvariantCulture) : null;

    public static double? Double(this Dictionary<string, object?> row, string key) =>
        row.TryGetValue(key, out var v) && v is not null ? Convert.ToDouble(v, CultureInfo.InvariantCulture) : null;
}
