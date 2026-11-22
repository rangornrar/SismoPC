using SismoPC.Core.Detection;
using SismoPC.Core.Health;
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
        _store.ReadOpenAlerts().ShouldHaveSingleItem().Subject.ShouldBe("D:");
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
    public void Tendances_mensuelles()
    {
        _store.AddSystemEvent(new DateTimeOffset(2026, 6, 3, 8, 0, 0, TimeSpan.Zero), "boot", "Démarrage", 38);
        _store.AddSystemEvent(new DateTimeOffset(2026, 11, 3, 8, 0, 0, TimeSpan.Zero), "boot", "Démarrage", 50);
        _store.AddSystemEvent(new DateTimeOffset(2026, 11, 10, 8, 0, 0, TimeSpan.Zero), "boot", "Démarrage", 54);
        _store.AddDiskHealth([new DiskHealth("SSD", true, false, 9, 40, 100)], new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));
        _store.AddDiskHealth([new DiskHealth("SSD", true, false, 12, 46, 200)], T0);

        var trends = _store.ReadTrends();

        var boot = trends.Single(t => t.Name == "Durée de démarrage");
        boot.Months.Select(m => m.Value).ShouldBe(new[] { 38.0, 52.0 });
        boot.IsDegraded.ShouldBeTrue();
        trends.Single(t => t.Name == "Usure du SSD").Last!.Value.Value.ShouldBe(12);

        var disk = _store.ReadLatestDiskHealth().ShouldHaveSingleItem();
        disk.WearPercent.ShouldBe(12);
        disk.IsSsd.ShouldBeTrue();
    }

    [Fact]
    public void Episode_relu_par_identifiant_et_ignore()
    {
        var e = new Episode(EpisodeKind.Anomaly, Resource.Cpu, MetricKind.CpuPercent, T0, T0.AddMinutes(20), 40, 3, "ms-teams.exe", 0.8);
        var id = _store.AddEpisode(e, "titre", "phrase");

        _store.ReadEpisode(id).ShouldNotBeNull().Episode.Culprit.ShouldBe("ms-teams.exe");
        _store.IgnoreEpisode(id);
        _store.ReadEpisode(id).ShouldBeNull();
        _store.ReadEpisode(999).ShouldBeNull();
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

    [Fact]
    public void Composant_retire_n_est_plus_affiche()
    {
        var oldGpu = new HardwareComponent("Carte graphique", "GeForce GTX 1060", new Dictionary<string, string>());
        var newGpu = new HardwareComponent("Carte graphique", "GeForce RTX 3070", new Dictionary<string, string>());
        _store.UpsertComponents([oldGpu], T0);
        _store.UpsertComponents([newGpu], T0.AddDays(30));

        _store.ReadComponents().ShouldHaveSingleItem().Name.ShouldBe("GeForce RTX 3070");
    }

    [Theory]
    [InlineData(10)]
    [InlineData(100)]
    public void Historique_purge_reste_consultable_au_zoom(int days)
    {
        var old = DateTimeOffset.UtcNow.AddDays(-days);
        _store.WriteSnapshot(Snap(old, 70));
        _store.Aggregate(old.AddMinutes(-1));
        _store.Purge(DateTimeOffset.UtcNow, new RetentionPolicy());
        _store.ReadSeries(MetricKind.CpuPercent, old.AddMinutes(-15), old.AddMinutes(15)).ShouldHaveSingleItem();
    }

    [Fact]
    public void Aucun_programme_sans_releve_proche()
    {
        _store.WriteSnapshot(Snap(T0, 70));
        _store.ReadProcessesAt(T0.AddDays(-30)).ShouldBeEmpty();
        _store.ReadProcessesAt(T0.AddSeconds(4)).ShouldHaveSingleItem();
    }

    [Fact]
    public void Capteur_inconnu_ne_resout_pas_alerte_et_message_est_actualise()
    {
        var hot = new HardwareAlert(AlertKind.CpuTemperature, AlertSeverity.Critical, "CPU", "100 C");
        _store.SyncAlerts([hot], T0);
        _store.SyncAlerts([], T0.AddSeconds(30), new HashSet<AlertKind> { AlertKind.CpuTemperature });
        _store.ReadOpenAlerts().ShouldHaveSingleItem();
        _store.SyncAlerts([hot with { Message = "110 C" }], T0.AddMinutes(1));
        _store.ReadOpenAlerts().Single().Message.ShouldBe("110 C");
        _store.SyncAlerts([], T0.AddMinutes(2));
        _store.ReadOpenAlerts().ShouldBeEmpty();
    }

    [Fact]
    public void Checkpoint_episode_est_actualise_sans_doublon()
    {
        var episode = new Episode(EpisodeKind.Anomaly, Resource.Cpu, MetricKind.CpuPercent, T0, T0.AddMinutes(5), 40, 3, null, 0);
        var id = _store.AddEpisode(episode, "actif", "actif");
        _store.AddEpisode(episode with { End = T0.AddMinutes(8) }, "clos", "clos").ShouldBe(id);
        var stored = _store.ReadEpisodes(T0, T0.AddHours(1)).ShouldHaveSingleItem();
        stored.Title.ShouldBe("clos");
        stored.Episode.End.ShouldBe(T0.AddMinutes(8));
    }
}
