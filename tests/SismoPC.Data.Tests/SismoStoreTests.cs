using SismoPC.Core.Detection;
using SismoPC.Core.Model;

namespace SismoPC.Data.Tests;

public sealed class SismoStoreTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 17, 14, 0, 0, TimeSpan.Zero);
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"sismopc-test-{Guid.NewGuid():N}.db");
    private readonly SismoStore _store;

    public SismoStoreTests()
    {
        _store = new SismoStore(_path);
    }

    public void Dispose()
    {
        _store.Dispose();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            File.Delete(_path + suffix);
        }
    }

    private static Snapshot Snap(DateTimeOffset at, double cpu) => new(
        at,
        new Dictionary<MetricKind, double> { [MetricKind.CpuPercent] = cpu, [MetricKind.MemoryUsedPercent] = 40 },
        [new ProcessUsage("chrome.exe", cpu / 2, 1_000_000, 0)]);

    [Fact]
    public void Ecrit_puis_resume_a_la_minute()
    {
        for (var i = 0; i < 24; i++) // 2 minutes à raison d'une mesure toutes les 5 s
        {
            _store.WriteSnapshot(Snap(T0.AddSeconds(5 * i), cpu: i));
        }

        _store.Aggregate(T0);

        var detail = _store.ReadSeries(MetricKind.CpuPercent, T0, T0.AddHours(1));
        detail.Count.ShouldBe(24);

        var week = _store.ReadSeries(MetricKind.CpuPercent, T0, T0.AddDays(7));
        week.Count.ShouldBe(2);
        week[0].Max.ShouldBe(11);
        week[1].Min.ShouldBe(12);
    }

    [Fact]
    public void Retrouve_les_programmes_a_un_instant()
    {
        _store.WriteSnapshot(Snap(T0, 10));
        _store.WriteSnapshot(Snap(T0.AddSeconds(5), 80));

        var procs = _store.ReadProcessesAt(T0.AddSeconds(4));

        procs.ShouldHaveSingleItem().CpuPercent.ShouldBe(40);
    }

    [Fact]
    public void Purge_supprime_le_detail_ancien()
    {
        _store.WriteSnapshot(Snap(T0.AddDays(-10), 50));
        _store.WriteSnapshot(Snap(T0, 50));

        _store.Purge(T0, new RetentionPolicy());

        _store.ReadSeries(MetricKind.CpuPercent, T0.AddDays(-11), T0.AddDays(-9).AddHours(-18)).ShouldBeEmpty();
        _store.ReadSeries(MetricKind.CpuPercent, T0.AddMinutes(-1), T0.AddMinutes(1)).Count.ShouldBe(1);
    }

    [Fact]
    public void Episode_enregistre_et_compte()
    {
        var e = new Episode(EpisodeKind.Slowdown, Resource.Disk, MetricKind.DiskActivePercent, T0, T0.AddMinutes(6), 100, null, "TiWorker.exe", 0.92);

        var id = _store.AddEpisode(e, "Disque saturé pendant 6 min", "phrase");

        id.ShouldBeGreaterThan(0);
        _store.CountSlowdowns(T0.AddDays(-7)).ShouldBe(1);
        var stored = _store.ReadEpisodes(T0.AddHours(-1), T0.AddHours(1)).ShouldHaveSingleItem();
        stored.Episode.ShouldBe(e);
        stored.Title.ShouldBe("Disque saturé pendant 6 min");
    }

    [Fact]
    public void Alertes_nouvelles_puis_levees()
    {
        var low = new HardwareAlert(AlertKind.LowDiskSpace, AlertSeverity.Warning, "D:", "8 %");

        _store.SyncAlerts([low], T0).ShouldHaveSingleItem();
        _store.SyncAlerts([low], T0.AddHours(1)).ShouldBeEmpty(); // déjà connue : pas de nouvelle notification
        _store.SyncAlerts([], T0.AddHours(2)).ShouldBeEmpty();
        _store.SyncAlerts([low], T0.AddHours(3)).ShouldHaveSingleItem(); // revenue : nouvelle alerte
    }

    [Fact]
    public void Normale_sauvegardee_et_rechargee()
    {
        var baseline = new Baseline();
        baseline.Learn(MetricKind.CpuPercent, T0, 3);
        baseline.Learn(MetricKind.CpuPercent, T0, 5);
        _store.SaveBaseline(baseline);

        var reloaded = new Baseline();
        _store.LoadBaseline(reloaded);

        var stats = reloaded.Slots[BaselineSlot.For(MetricKind.CpuPercent, T0)];
        stats.Count.ShouldBe(2);
        stats.Mean.ShouldBe(4);
    }

    [Fact]
    public void Inventaire_mis_a_jour_sans_doublon()
    {
        var cpu = new HardwareComponent("Processeur", "AMD Ryzen 7 5800X", new Dictionary<string, string> { ["Cœurs"] = "8" });
        _store.UpsertComponents([cpu], T0);
        _store.UpsertComponents([cpu with { Details = new Dictionary<string, string> { ["Cœurs"] = "8", ["Threads"] = "16" } }], T0.AddDays(1));

        var read = _store.ReadComponents().ShouldHaveSingleItem();
        read.Details["Threads"].ShouldBe("16");
    }
}
