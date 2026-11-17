using System.Globalization;

namespace SismoPC.Core.Health;

/// <summary>Valeur mensuelle d'un indicateur, par exemple (« 2026-06 », 38).</summary>
public readonly record struct MonthlyValue(string Month, double Value);

/// <summary>Évolution d'un indicateur de dégradation (US8). Pour ces indicateurs, plus haut = moins bien.</summary>
public sealed record Trend(string Name, string Unit, IReadOnlyList<MonthlyValue> Months)
{
    /// <summary>Seuil de dégradation des spécifications : +20 % depuis le premier mois.</summary>
    public const double DegradationThreshold = 0.20;

    public MonthlyValue? First => Months.Count > 0 ? Months[0] : null;

    public MonthlyValue? Last => Months.Count > 0 ? Months[^1] : null;

    /// <summary>Variation relative du dernier mois par rapport au premier, ou null s'il n'y a pas deux mois.</summary>
    public double? Change =>
        Months.Count >= 2 && Months[0].Value > 0 ? (Months[^1].Value - Months[0].Value) / Months[0].Value : null;

    public bool IsDegraded => Change is { } c && c > DegradationThreshold;

    /// <summary>Libellé de variation, par exemple « +37 % ».</summary>
    public string ChangeLabel => Change is { } c
        ? (c >= 0 ? "+" : "−") + Math.Abs(c * 100).ToString("0", CultureInfo.GetCultureInfo("fr-FR")) + " %"
        : "—";

    /// <summary>Nom du mois en français, par exemple « juin ».</summary>
    public static string MonthLabel(string month) =>
        DateTime.TryParseExact(month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.ToString("MMM", CultureInfo.GetCultureInfo("fr-FR"))
            : month;
}
