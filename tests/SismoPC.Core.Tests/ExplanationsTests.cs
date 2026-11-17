using SismoPC.Core.Model;
using SismoPC.Core.Text;
using static SismoPC.Core.Tests.TestData;

namespace SismoPC.Core.Tests;

public class ExplanationsTests
{
    [Fact]
    public void Episode_disque_redige_comme_dans_les_maquettes()
    {
        var episode = new Episode(EpisodeKind.Slowdown, Resource.Disk, MetricKind.DiskActivePercent,
            T0.AddMinutes(5), T0.AddMinutes(11), 100, null, "TiWorker.exe", 0.92);

        Explanations.Title(episode).ShouldBe("Disque saturé pendant 6 min");
        Explanations.Sentence(episode, TimeZoneInfo.Utc)
            .ShouldBe("De 14:05 à 14:11, le disque était occupé à plus de 90 %, surtout par TiWorker.exe (92 % de l'activité).");
    }

    [Fact]
    public void Anomalie_mentionne_la_valeur_habituelle()
    {
        var episode = new Episode(EpisodeKind.Anomaly, Resource.Cpu, MetricKind.CpuPercent,
            T0, T0.AddMinutes(20), 40, 3, null, 0);

        Explanations.Title(episode).ShouldBe("Processeur anormalement haut pendant 20 min");
        Explanations.Sentence(episode, TimeZoneInfo.Utc)
            .ShouldBe("De 14:00 à 14:20, le processeur a atteint 40 %, alors qu'il est d'habitude autour de 3 % à cette heure-ci.");
    }

    [Theory]
    [InlineData(30, "30 s")]
    [InlineData(360, "6 min")]
    [InlineData(5400, "1 h 30")]
    public void Durees_lisibles(int seconds, string expected) =>
        Explanations.FormatDuration(TimeSpan.FromSeconds(seconds)).ShouldBe(expected);
}
