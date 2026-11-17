using SismoPC.Core.Detection;
using SismoPC.Core.Model;
using static SismoPC.Core.Tests.TestData;

namespace SismoPC.Core.Tests;

public class AnomalyDetectorTests
{
    private static (AnomalyDetector Detector, Baseline Baseline) Trained(double usual)
    {
        var baseline = new Baseline { MinSamples = 10 };
        var detector = new AnomalyDetector(baseline, T0.AddDays(-20)) { TimeZone = TimeZoneInfo.Utc };

        // Normale : processeur autour de « usual » dans le créneau de 14 h.
        foreach (var s in Series(T0.AddDays(-7), MetricKind.CpuPercent, Enumerable.Range(0, 200).Select(i => usual + (i % 3) - 1)))
        {
            detector.Process(s);
        }

        return (detector, baseline);
    }

    [Fact]
    public void Valeur_durablement_hors_normale_cree_une_anomalie()
    {
        var (detector, _) = Trained(usual: 3);
        var values = Enumerable.Repeat(40.0, 61).Append(3.0); // 5 min hors normale, puis retour

        var anomalies = Series(T0, MetricKind.CpuPercent, values, Proc("ms-teams.exe", cpu: 35))
            .SelectMany(detector.Process)
            .ToList();

        var anomaly = anomalies.ShouldHaveSingleItem();
        anomaly.Kind.ShouldBe(EpisodeKind.Anomaly);
        anomaly.UsualValue!.Value.ShouldBe(3, tolerance: 0.5);
        anomaly.PeakValue.ShouldBe(40);
        anomaly.Culprit.ShouldBe("ms-teams.exe");
    }

    [Fact]
    public void Pas_d_anomalie_pendant_l_apprentissage()
    {
        var baseline = new Baseline { MinSamples = 1 };
        var detector = new AnomalyDetector(baseline, T0.AddDays(-2)) { TimeZone = TimeZoneInfo.Utc };

        detector.IsLearning(T0).ShouldBeTrue();
        var anomalies = Series(T0, MetricKind.CpuPercent, Enumerable.Repeat(99.0, 100))
            .SelectMany(detector.Process)
            .ToList();

        anomalies.ShouldBeEmpty();
    }

    [Fact]
    public void Ecart_court_ne_cree_pas_d_anomalie()
    {
        var (detector, _) = Trained(usual: 3);
        var values = Enumerable.Repeat(40.0, 10).Append(3.0); // 50 s seulement

        Series(T0, MetricKind.CpuPercent, values).SelectMany(detector.Process).ShouldBeEmpty();
    }

    [Fact]
    public void Plage_jamais_plus_etroite_que_le_minimum()
    {
        var baseline = new Baseline { MinSamples = 1, MinHalfWidth = 5 };
        for (var i = 0; i < 10; i++)
        {
            baseline.Learn(MetricKind.CpuPercent, T0, 3);
        }

        var range = baseline.UsualRange(MetricKind.CpuPercent, T0).ShouldNotBeNull();
        range.Low.ShouldBe(-2);
        range.High.ShouldBe(8);
    }
}
