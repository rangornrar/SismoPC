using System.Globalization;

namespace SismoPC.Core.Model;

/// <summary>Nature d'un indice de date.</summary>
public enum DateClueKind
{
    /// <summary>Le PC était forcément en service à cette date (heures d'utilisation d'un disque, installation de Windows).</summary>
    InUseSince,

    /// <summary>Date de fabrication d'un élément (écran, batterie) : peut différer de celle du PC.</summary>
    Manufactured,

    /// <summary>Date de publication du BIOS installé : proche de la fabrication s'il n'a jamais été mis à jour.</summary>
    Firmware,
}

/// <summary>Un indice pour dater le PC.</summary>
public sealed record DateClue(string Label, DateTime Date, DateClueKind Kind, string Explanation);

/// <summary>
/// Datation du PC. Windows ne connaît pas de date de fabrication globale : on rassemble des indices
/// et on n'affirme que ce qui est sûr, « en service depuis au moins », à partir des indices <see cref="DateClueKind.InUseSince"/>.
/// </summary>
public static class PcAge
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>Date la plus ancienne à laquelle le PC était certainement en service, ou null sans indice fiable.</summary>
    public static DateTime? InUseSince(IEnumerable<DateClue> clues, DateTime now) =>
        clues.Where(c => c.Kind == DateClueKind.InUseSince && c.Date <= now)
             .Select(c => (DateTime?)c.Date)
             .Min();

    /// <summary>Phrase de synthèse, par exemple « En service depuis au moins mars 2021 (environ 5 ans) ».</summary>
    public static string Summary(IEnumerable<DateClue> clues, DateTime now)
    {
        var list = clues.ToList();
        if (InUseSince(list, now) is { } since)
        {
            return $"En service depuis au moins {since.ToString("MMMM yyyy", Fr)} ({AgeText(since, now)})";
        }

        var firmware = list.Where(c => c.Kind == DateClueKind.Firmware).Select(c => (DateTime?)c.Date).Min();
        return firmware is { } f
            ? $"BIOS publié en {f.ToString("MMMM yyyy", Fr)} ({AgeText(f, now)})"
            : "Date inconnue";
    }

    /// <summary>Âge lisible : « moins d'un mois », « environ 8 mois », « environ 3 ans ».</summary>
    public static string AgeText(DateTime since, DateTime now)
    {
        var months = ((now.Year - since.Year) * 12) + now.Month - since.Month - (now.Day < since.Day ? 1 : 0);
        return months switch
        {
            < 1 => "moins d'un mois",
            < 12 => $"environ {months} mois",
            < 18 => "environ 1 an",
            _ => $"environ {(int)Math.Round(months / 12.0, MidpointRounding.AwayFromZero)} ans",
        };
    }

    /// <summary>Date « en service depuis au plus tard », d'après des heures de fonctionnement.</summary>
    public static DateTime FromPowerOnHours(long hours, DateTime now) => now.AddHours(-hours);
}
