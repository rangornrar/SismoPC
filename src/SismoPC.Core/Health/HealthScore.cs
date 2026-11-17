using SismoPC.Core.Model;

namespace SismoPC.Core.Health;

/// <summary>Une pénalité du score de santé, avec sa raison lisible.</summary>
public sealed record Penalty(string Reason, int Points);

/// <summary>Score de santé sur 100 et le détail des points perdus.</summary>
public sealed record HealthScore(int Value, IReadOnlyList<Penalty> Penalties)
{
    public string Label => Value switch
    {
        >= 85 => "Bon état",
        >= 60 => "À surveiller",
        _ => "Problème",
    };
}

/// <summary>Données d'entrée du score.</summary>
/// <param name="Alerts">Alertes matérielles en cours.</param>
/// <param name="OpenAnomalies">Anomalies en cours.</param>
/// <param name="SlowdownsLast7Days">Épisodes de ralentissement sur 7 jours.</param>
/// <param name="DegradedTrends">Indicateurs dégradés de plus de 20 % depuis le premier mois.</param>
/// <param name="IsLearning">Vrai pendant les 14 jours d'apprentissage.</param>
public sealed record HealthInputs(
    IReadOnlyList<HardwareAlert> Alerts,
    int OpenAnomalies,
    int SlowdownsLast7Days,
    IReadOnlyList<string> DegradedTrends,
    bool IsLearning);

/// <summary>
/// Calcule le score de santé selon les spécifications : 100 moins les pénalités, jamais négatif.
/// Pendant l'apprentissage, seules les alertes matérielles comptent.
/// </summary>
public static class HealthScoreCalculator
{
    public const int CriticalAlertPoints = 30;
    public const int WarningAlertPoints = 10;
    public const int AnomalyPoints = 5;
    public const int SlowdownPoints = 2;
    public const int SlowdownCap = 20;
    public const int DegradedTrendPoints = 10;

    public static HealthScore Compute(HealthInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        var penalties = new List<Penalty>();

        foreach (var alert in inputs.Alerts)
        {
            var points = alert.Severity == AlertSeverity.Critical ? CriticalAlertPoints : WarningAlertPoints;
            penalties.Add(new Penalty($"{alert.Subject} : {alert.Message}", points));
        }

        if (!inputs.IsLearning)
        {
            if (inputs.OpenAnomalies > 0)
            {
                penalties.Add(new Penalty(
                    inputs.OpenAnomalies == 1 ? "1 anomalie en cours" : $"{inputs.OpenAnomalies} anomalies en cours",
                    inputs.OpenAnomalies * AnomalyPoints));
            }

            if (inputs.SlowdownsLast7Days > 0)
            {
                penalties.Add(new Penalty(
                    inputs.SlowdownsLast7Days == 1
                        ? "1 épisode de ralentissement sur 7 jours"
                        : $"{inputs.SlowdownsLast7Days} épisodes de ralentissement sur 7 jours",
                    Math.Min(inputs.SlowdownsLast7Days * SlowdownPoints, SlowdownCap)));
            }

            foreach (var trend in inputs.DegradedTrends)
            {
                penalties.Add(new Penalty($"{trend} dégradé de plus de 20 %", DegradedTrendPoints));
            }
        }

        var value = Math.Max(0, 100 - penalties.Sum(p => p.Points));
        return new HealthScore(value, penalties);
    }
}
