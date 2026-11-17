using SismoPC.Core.Health;
using SismoPC.Core.Model;

namespace SismoPC.Core.Tests;

public class HealthTests
{
    [Fact]
    public void Une_alerte_et_deux_ralentissements()
    {
        var alerts = new[] { new HardwareAlert(AlertKind.LowDiskSpace, AlertSeverity.Warning, "D:", "74 Go libres") };
        var score = HealthScoreCalculator.Compute(new HealthInputs(alerts, OpenAnomalies: 0, SlowdownsLast7Days: 2, DegradedTrends: [], IsLearning: false));

        // -10 (espace disque) -4 (2 épisodes à -2) = 86
        score.Value.ShouldBe(86);
        score.Penalties.Count.ShouldBe(2);
        score.Label.ShouldBe("Bon état");
    }

    [Fact]
    public void Les_ralentissements_sont_plafonnes_a_20_points()
    {
        var score = HealthScoreCalculator.Compute(new HealthInputs([], 0, SlowdownsLast7Days: 50, [], false));
        score.Value.ShouldBe(80);
    }

    [Fact]
    public void Le_score_n_est_jamais_negatif()
    {
        var alerts = Enumerable.Range(0, 5)
            .Select(i => new HardwareAlert(AlertKind.DiskSmartFailure, AlertSeverity.Critical, $"Disque {i}", "panne"))
            .ToList();
        HealthScoreCalculator.Compute(new HealthInputs(alerts, 0, 0, [], false)).Value.ShouldBe(0);
    }

    [Fact]
    public void Pendant_l_apprentissage_seules_les_alertes_comptent()
    {
        var score = HealthScoreCalculator.Compute(new HealthInputs([], OpenAnomalies: 3, SlowdownsLast7Days: 5, ["Démarrage"], IsLearning: true));
        score.Value.ShouldBe(100);
    }

    [Fact]
    public void Alertes_materielles_selon_les_seuils()
    {
        var state = new HardwareState(
            CpuTemperatureC: 97,
            GpuTemperatureC: 60,
            Disks: [new DiskHealth("Samsung SSD 980", IsSsd: true, PredictsFailure: false, WearPercent: 85, TemperatureC: 46, PowerOnHours: 9000)],
            Volumes: [new VolumeSpace("D:", 931_000_000_000, 74_000_000_000), new VolumeSpace("C:", 1_000_000_000_000, 312_000_000_000)]);

        var alerts = HardwareAlertRules.Evaluate(state);

        alerts.Select(a => a.Kind).ShouldBe(new[] { AlertKind.CpuTemperature, AlertKind.SsdWear, AlertKind.LowDiskSpace }, ignoreOrder: true);
        alerts.Single(a => a.Kind == AlertKind.LowDiskSpace).Subject.ShouldBe("D:");
    }
}
