using System.Globalization;
using SismoPC.Core.Model;

namespace SismoPC.Core.Health;

public sealed record MetricObservation(MetricKind Metric, double Peak, int Samples);
public sealed record IncidentFinding(string Title, string Observation, string Action, bool Elevated);
public sealed record IncidentAssessment(string Summary, IReadOnlyList<IncidentFinding> Findings, string Limit);

/// <summary>Interprétation déterministe des observations, sans déduire une panne ou une causalité.</summary>
public static class IncidentAnalysis
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    public static IncidentAssessment Assess(IReadOnlyList<MetricObservation> observations)
    {
        var findings = new List<IncidentFinding>();
        foreach (var observation in observations.Where(o => o.Samples > 0))
        {
            var rule = observation.Metric switch
            {
                MetricKind.CpuPercent => ("Processeur très sollicité", 90d, "%", "Consultez les programmes relevés à cet instant. Si la même activité revient, vérifiez les tâches lancées par ce programme."),
                MetricKind.MemoryUsedPercent => ("Mémoire très occupée", 90d, "%", "Fermez les applications inutilisées et observez si le ralentissement revient avec votre charge habituelle."),
                MetricKind.MemoryCommittedPercent => ("Mémoire engagée élevée", 90d, "%", "Vérifiez la consommation mémoire des applications ; cet indicateur n'est pas la quantité de RAM physique utilisée."),
                MetricKind.DiskLatencyMs => ("Réponses du disque lentes", 50d, "ms", "Vérifiez si une copie, une mise à jour ou une analyse était en cours. Un pic isolé ne démontre pas une panne du disque."),
                MetricKind.DiskActivePercent => ("Disque très sollicité", 90d, "%", "Comparez avec les copies, sauvegardes et mises à jour en cours avant de conclure à un défaut matériel."),
                MetricKind.CpuTemperatureC => ("Température du processeur élevée", 95d, "°C", "Vérifiez la ventilation et les températures dans Matériel. En cas d'alerte persistante, réduisez la charge et faites contrôler le refroidissement."),
                MetricKind.GpuTemperatureC => ("Température graphique élevée", 90d, "°C", "Vérifiez la ventilation et observez la température après réduction de la charge graphique."),
                _ => ("", double.PositiveInfinity, "", "")
            };
            if (observation.Peak >= rule.Item2)
                findings.Add(new IncidentFinding(rule.Item1,
                    string.Format(Fr, "Un maximum de {0:0.#} {1} a été relevé dans la période analysée.", observation.Peak, rule.Item3), rule.Item4, true));
        }

        var summary = observations.All(o => o.Samples == 0)
            ? "Les données disponibles ne permettent pas d'expliquer ce ralentissement."
            : findings.Count == 0
                ? "Aucun dépassement des seuils examinés n'explique clairement ce ralentissement."
                : findings.Count == 1 ? findings[0].Title : $"{findings.Count} observations méritent votre attention.";
        return new IncidentAssessment(summary, findings,
            "Ces observations ne prouvent ni une panne ni qu'un programme est la cause du ralentissement. La collecte peut être partielle et les pics brefs peuvent ne pas être enregistrés.");
    }

    public static IncidentFinding Explain(Episode episode)
    {
        var findings = Assess([new MetricObservation(episode.Metric, episode.PeakValue, 1)]).Findings;
        return findings.Count > 0 ? findings[0] : new IncidentFinding("Comportement inhabituel observé",
            "La mesure s'est écartée de la référence apprise sur ce PC ; cela ne suffit pas à établir un dysfonctionnement.",
            "Comparez avec votre activité à cet instant et surveillez une éventuelle répétition avant d'agir.", false);
    }
}
