using SismoPC.Core.Health;
using SismoPC.Core.Model;
using SismoPC.Core.Reporting;

namespace SismoPC.Core.Tests;

public class IncidentAndReportTests
{
    private static readonly DateTimeOffset At = new(2026,11,23,12,0,0,TimeSpan.Zero);

    [Fact]
    public void Aucune_donnee_ne_devient_pas_un_diagnostic_rassurant()
    {
        var result = IncidentAnalysis.Assess([]);
        result.Findings.ShouldBeEmpty();
        result.Summary.ShouldContain("ne permettent pas");
    }

    [Fact]
    public void Pic_observe_donne_une_action_sans_declarer_une_panne()
    {
        var result = IncidentAnalysis.Assess([new(MetricKind.DiskLatencyMs,270,3)]);
        result.Findings.ShouldHaveSingleItem().Action.ShouldContain("ne démontre pas une panne");
        result.Limit.ShouldContain("ne prouvent");
    }

    [Fact]
    public void Une_observation_sans_echantillon_est_ignoree()
    {
        IncidentAnalysis.Assess([new(MetricKind.CpuPercent,100,0)]).Findings.ShouldBeEmpty();
    }

    private static ReportContent Content() => new(At.AddHours(-1),At,At,"0.4.0","SECRET_PC",[],
        [new Episode(EpisodeKind.Slowdown,Resource.Cpu,MetricKind.CpuPercent,At.AddMinutes(-2),At,98,null,"SECRET_PROGRAM.exe",.9)],
        [new(MetricKind.CpuPercent,98,3)],
        [new HardwareComponent("Disque","SSD [SECRET_SERIAL]",new Dictionary<string,string>())],
        [new ReportCurve("CPU","%",[new TimedValue(At.AddMinutes(-5),98,TimeSpan.FromMinutes(1))])],1);

    [Fact]
    public void Rapport_masque_les_donnees_sensibles_par_defaut_y_compris_dans_les_phrases()
    {
        var html = ReportDocument.Html(Content(),new ReportOptions(IncludeHardware:true));
        html.ShouldNotContain("SECRET_PC");
        html.ShouldNotContain("SECRET_PROGRAM");
        html.ShouldNotContain("SECRET_SERIAL");
        html.ShouldContain("Les périodes sans relevé");
    }

    [Fact]
    public void Inclusion_est_un_choix_explicite_et_html_est_echappe()
    {
        var content = Content() with { Machine = "<script>alert('x')</script>" };
        var html = ReportDocument.Html(content,new ReportOptions(true,true,true,true));
        html.ShouldContain("SECRET_PROGRAM.exe");
        html.ShouldContain("SECRET_SERIAL");
        html.ShouldContain("&lt;script&gt;");
        html.ShouldNotContain("<script>");
        html.ShouldContain("default-src 'none'");
    }

    [Fact]
    public void Rapport_vide_signale_les_limites_de_collecte()
    {
        var html = ReportDocument.Html(Content() with { Episodes=[], Observations=[], Curves=[] },new());
        html.ShouldContain("Aucune mesure disponible");
        html.ShouldContain("ne certifie pas");
    }
}
