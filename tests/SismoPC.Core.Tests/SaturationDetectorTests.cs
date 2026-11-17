using SismoPC.Core.Detection;
using SismoPC.Core.Model;
using static SismoPC.Core.Tests.TestData;

namespace SismoPC.Core.Tests;

public class SaturationDetectorTests
{
    private static SaturationDetector NewDetector() => new(SaturationRule.Defaults());

    [Fact]
    public void Saturation_plus_longue_que_30_s_cree_un_episode_avec_le_responsable()
    {
        var detector = NewDetector();
        var procs = new[] { Proc("TiWorker.exe", disk: 90e6), Proc("MsMpEng.exe", disk: 10e6) };
        var values = Enumerable.Repeat(99.0, 8).Append(5.0); // 8 × 5 s = 35 s au-dessus du seuil

        var episodes = Series(T0, MetricKind.DiskActivePercent, values, procs)
            .SelectMany(detector.Process)
            .ToList();

        var episode = episodes.ShouldHaveSingleItem();
        episode.Kind.ShouldBe(EpisodeKind.Slowdown);
        episode.Resource.ShouldBe(Resource.Disk);
        episode.Culprit.ShouldBe("TiWorker.exe");
        episode.CulpritShare.ShouldBe(0.9, tolerance: 0.001);
        episode.Duration.ShouldBe(TimeSpan.FromSeconds(35));
    }

    [Fact]
    public void Pic_bref_ne_cree_pas_d_episode()
    {
        var detector = NewDetector();
        var values = new[] { 99.0, 99.0, 99.0, 10.0 }; // 10 s seulement

        var episodes = Series(T0, MetricKind.CpuPercent, values, Proc("chrome.exe", cpu: 80))
            .SelectMany(detector.Process)
            .ToList();

        episodes.ShouldBeEmpty();
    }

    [Fact]
    public void Flush_clot_un_episode_en_cours()
    {
        var detector = NewDetector();
        foreach (var s in Series(T0, MetricKind.CpuPercent, Enumerable.Repeat(97.0, 10), Proc("chrome.exe", cpu: 80)))
        {
            detector.Process(s).ShouldBeEmpty();
        }

        var episode = detector.Flush().ShouldHaveSingleItem();
        episode.PeakValue.ShouldBe(97);
        episode.Culprit.ShouldBe("chrome.exe");
    }

    [Fact]
    public void Mesure_absente_est_ignoree()
    {
        var detector = NewDetector();
        var snapshot = Snap(T0, MetricKind.NetworkBytesPerSecond, 1e9);

        detector.Process(snapshot).ShouldBeEmpty();
    }
}
