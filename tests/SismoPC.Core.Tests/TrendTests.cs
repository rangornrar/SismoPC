using SismoPC.Core.Health;

namespace SismoPC.Core.Tests;

public class TrendTests
{
    private static Trend Boot(params double[] values) =>
        new("Durée de démarrage", "s", values.Select((v, i) => new MonthlyValue($"2026-{i + 6:00}", v)).ToList());

    [Fact]
    public void Demarrage_qui_passe_de_38_a_52_s_est_degrade()
    {
        var trend = Boot(38, 39, 41, 44, 48, 52);

        trend.Change!.Value.ShouldBe(0.368, tolerance: 0.001);
        trend.IsDegraded.ShouldBeTrue();
        trend.ChangeLabel.ShouldBe("+37 %");
    }

    [Fact]
    public void Hausse_moderee_non_degradee()
    {
        var trend = Boot(4.1, 4.6);
        trend.IsDegraded.ShouldBeFalse();
        trend.ChangeLabel.ShouldBe("+12 %");
    }

    [Fact]
    public void Un_seul_mois_pas_de_variation()
    {
        var trend = Boot(40);
        trend.Change.ShouldBeNull();
        trend.IsDegraded.ShouldBeFalse();
        trend.ChangeLabel.ShouldBe("—");
    }

    [Fact]
    public void Nom_du_mois_en_francais() => Trend.MonthLabel("2026-06").ShouldStartWith("juin");
}
