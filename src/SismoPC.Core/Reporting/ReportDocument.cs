using System.Globalization;
using System.Net;
using System.Text;
using SismoPC.Core.Health;
using SismoPC.Core.Model;
using SismoPC.Core.Text;

namespace SismoPC.Core.Reporting;

public sealed record ReportOptions(bool IncludeMachine = false, bool IncludePrograms = false, bool IncludeIdentifiers = false, bool IncludeHardware = false);
public sealed record ReportCurve(string Name, string Unit, IReadOnlyList<TimedValue> Values);
public sealed record ReportContent(DateTimeOffset From, DateTimeOffset To, DateTimeOffset Generated, string Version,
    string Machine, IReadOnlyList<HardwareAlert> Alerts, IReadOnlyList<Episode> Episodes,
    IReadOnlyList<MetricObservation> Observations, IReadOnlyList<HardwareComponent> Hardware,
    IReadOnlyList<ReportCurve> Curves, int ManualReports, string Title = "Bilan de votre PC");

/// <summary>Document autonome, sans script ni ressource distante, commun à l'aperçu et au PDF.</summary>
public static class ReportDocument
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
    private static string E(string? value) => WebUtility.HtmlEncode(value ?? "");
    private static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    private static string PublicName(string value, bool identifiers) => identifiers ? value : value.Split('[')[0].Trim();

    public static string Html(ReportContent report, ReportOptions options)
    {
        var html = new StringBuilder("""
            <!doctype html><html lang="fr"><head><meta charset="utf-8">
            <meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'">
            <title>Rapport SismoPC</title><style>
            @page{size:A4;margin:17mm 16mm}*{box-sizing:border-box}body{font:14px/1.55 'Segoe UI',Arial,sans-serif;color:#203547;background:#fff;margin:0 auto;max-width:940px;padding:32px}
            header{border-top:5px solid #0e6f80;padding-top:18px;margin-bottom:26px}.brand{font-size:15px;font-weight:700;letter-spacing:1px;color:#0e6f80}h1{font-size:30px;line-height:1.2;margin:8px 0}h2{font-size:19px;margin:25px 0 10px;color:#0e6f80;break-after:avoid}h3{font-size:15px;margin:10px 0 6px}
            .meta,.muted{color:#536878;font-size:12px}.summary{background:#edf5f6;border-left:4px solid #0e6f80;padding:18px 20px;margin:20px 0}.stats{display:flex;gap:16px;margin:16px 0}.stat{flex:1;background:#f3f6f8;padding:12px}.stat b{display:block;font-size:24px;color:#0e6f80}
            article,.curve{break-inside:avoid;border-bottom:1px solid #dde5eb;padding:10px 0 16px}table{border-collapse:collapse;width:100%;font-size:12px}td,th{padding:8px;text-align:left;vertical-align:top;border-bottom:1px solid #e0e7ec}th{background:#edf5f6}thead{display:table-header-group}svg{width:100%;height:auto}footer{margin-top:28px;border-top:1px solid #dde5eb;padding-top:12px;font-size:11px;color:#536878}.tag{font-size:11px;font-weight:700;color:#0e6f80}p{margin:6px 0 12px}.notice{background:#fff6df;padding:12px} @media print{body{padding:0;max-width:none;font-size:10pt}h1{font-size:24pt}h2{font-size:15pt}.stats,.summary,th{print-color-adjust:exact}a{color:inherit}}
            </style></head><body>
            """);
        html.Append(Fr, $"<header><div class='brand'>SISMO<span>PC</span> · COMPRENDRE SON PC</div><h1>{E(report.Title)}</h1><div class='meta'>Du {E(report.From.ToLocalTime().ToString("d MMMM yyyy HH:mm", Fr))} au {E(report.To.ToLocalTime().ToString("d MMMM yyyy HH:mm", Fr))}<br>Généré le {E(report.Generated.ToLocalTime().ToString("d MMMM yyyy HH:mm", Fr))} · SismoPC {E(report.Version)}</div>");
        if (options.IncludeMachine) html.Append(Fr, $"<p>Ordinateur : {E(report.Machine)}</p>");
        html.Append("</header>");
        var assessment = IncidentAnalysis.Assess(report.Observations);
        var availability = report.Observations.Any(o => o.Samples > 0);
        var summary = !availability ? "Aucune mesure disponible pour cette période." : assessment.Findings.Count == 0
            ? "Aucun dépassement des seuils examinés dans les mesures disponibles."
            : assessment.Findings.Count == 1 ? assessment.Findings[0].Title : $"{assessment.Findings.Count} observations méritent votre attention.";
        html.Append(Fr, $"<section class='summary'><strong>{E(summary)}</strong><p>{E(assessment.Limit)}</p></section>");
        html.Append(Fr, $"<div class='stats'><div class='stat'><b>{report.Episodes.Count}</b>épisodes enregistrés</div><div class='stat'><b>{report.ManualReports}</b>signalements utilisateur</div><div class='stat'><b>{report.Alerts.Count}</b>alertes actuellement ouvertes</div></div>");
        html.Append("<p class='muted'>Les alertes ouvertes décrivent le dernier état connu, pas nécessairement l'état pendant toute la période. Un rapport sans alerte ne certifie pas la santé du matériel.</p>");
        html.Append("<h2>Ce qui mérite votre attention</h2>");
        if (report.Alerts.Count == 0 && assessment.Findings.Count == 0)
            html.Append("<p>Aucun dépassement des seuils examinés ni alerte ouverte n'a été trouvé dans les données disponibles.</p>");
        foreach (var alert in report.Alerts)
            html.Append(Fr, $"<article><h3>{E(PublicName(alert.Subject, options.IncludeIdentifiers))}</h3><p>{E(alert.Message)}</p><span class='tag'>{E(alert.Severity == AlertSeverity.Critical ? "Alerte critique" : "À surveiller")}</span></article>");
        foreach (var finding in assessment.Findings)
            html.Append(Fr, $"<article><h3>{E(finding.Title)}</h3><p>{E(finding.Observation)}</p><p><strong>Action proposée :</strong> {E(finding.Action)}</p></article>");
        html.Append("<h2>Les mesures dans leur contexte</h2>");
        foreach (var curve in report.Curves) html.Append(Curve(curve, report.From, report.To));
        html.Append("<p class='muted'>Les périodes sans relevé sont représentées à zéro pour la lecture du graphique. Ces zéros ne sont pas des mesures : le PC peut être éteint, en veille ou la collecte interrompue. Les agrégats anciens ne permettent pas de reconstituer les interruptions plus brèves que leur résolution.</p>");
        html.Append("<h2>Historique des épisodes</h2>");
        if (report.Episodes.Count == 0) html.Append("<p>Aucun épisode enregistré dans cette période. Cela ne garantit pas l'absence d'incident si la collecte était partielle ou interrompue.</p>");
        foreach (var episode in report.Episodes.Take(200))
        {
            var visible = options.IncludePrograms ? episode : episode with { Culprit = null, CulpritShare = 0 };
            html.Append(Fr, $"<article><div class='tag'>{E(episode.Start.ToLocalTime().ToString("ddd d MMM HH:mm", Fr))}</div><h3>{E(Explanations.Title(visible))}</h3><p>{E(Explanations.Sentence(visible))}</p><p class='muted'>L'activité d'un programme est une contribution observée, pas une preuve de causalité.</p></article>");
        }
        if (report.Episodes.Count > 200) html.Append("<p class='notice'>Les 200 épisodes les plus récents sont détaillés. Réduisez la période pour consulter les autres événements.</p>");
        if (options.IncludeHardware)
        {
            html.Append("<h2>Configuration retenue pour ce rapport</h2><table><thead><tr><th>Composant</th><th>Description</th></tr></thead><tbody>");
            foreach (var component in report.Hardware.Where(c => options.IncludeMachine || c.Type != "Système"))
                html.Append(Fr, $"<tr><td>{E(component.Type)}</td><td>{E(PublicName(component.Name, options.IncludeIdentifiers))}</td></tr>");
            html.Append("</tbody></table>");
        }
        html.Append(Fr, $"<footer><strong>Confidentialité de cet export</strong><br>Nom du PC : {(options.IncludeMachine ? "inclus" : "masqué")} · Programmes : {(options.IncludePrograms ? "inclus" : "masqués")} · Identifiants matériels : {(options.IncludeIdentifiers ? "inclus dans les libellés" : "masqués")}<br>Rapport produit localement, sans transmission automatique. Interprétations fondées sur les seules mesures conservées. Ce document n'est ni un certificat de stabilité ni une prédiction de panne.</footer></body></html>");
        return html.ToString();
    }

    private static string Curve(ReportCurve curve, DateTimeOffset from, DateTimeOffset to)
    {
        if (curve.Values.Count == 0) return $"<section class='curve'><h3>{E(curve.Name)}</h3><p class='muted'>Aucun relevé disponible.</p></section>";
        var points = TimelineSeries.WithZeroGaps(curve.Values, from, to);
        var max = Math.Max(curve.Unit == "%" ? 100 : 1, curve.Values.Max(p => p.Value));
        var duration = Math.Max(1, (to - from).TotalSeconds);
        var coordinates = points.Select((p, i) => (p, i, x: Math.Clamp((p.At - from).TotalSeconds / duration * 680, 0, 680)))
            .GroupBy(p => (int)p.x).OrderBy(g => g.Key)
            .SelectMany(g => new[] { g.First(), g.MinBy(p => p.p.Value), g.MaxBy(p => p.p.Value), g.Last() }.DistinctBy(p => p.i).OrderBy(p => p.i))
            .Select(p => $"{Number(p.x + 35)},{Number(115 - Math.Clamp(p.p.Value / max, 0, 1) * 95)}");
        return $"<section class='curve'><h3>{E(curve.Name)} <span class='muted'>({E(curve.Unit)})</span></h3><svg viewBox='0 0 730 145' role='img' aria-label='{E(curve.Name)}'><path d='M35 20H715 M35 115H715' stroke='#dce5eb' fill='none'/><text x='0' y='24' font-size='10' fill='#536878'>{E(max.ToString("0", Fr))}</text><text x='0' y='118' font-size='10' fill='#536878'>0</text><polyline points='{string.Join(" ", coordinates)}' stroke='#0e6f80' stroke-width='1.4' fill='none'/><text x='35' y='138' font-size='10' fill='#536878'>{E(from.ToLocalTime().ToString("dd/MM HH:mm", Fr))}</text><text x='715' y='138' text-anchor='end' font-size='10' fill='#536878'>{E(to.ToLocalTime().ToString("dd/MM HH:mm", Fr))}</text></svg></section>";
    }
}
