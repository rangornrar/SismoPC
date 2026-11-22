using System.Globalization;
using SismoPC.Collectors;
using SismoPC.Core.Detection;
using SismoPC.Core.Health;
using SismoPC.Core.Ipc;
using SismoPC.Core.Model;
using SismoPC.Core.Text;
using SismoPC.Data;

namespace SismoPC.Service;

/// <summary>
/// Boucle principale du service (US2) : collecte toutes les 5 s, températures toutes les 10 s,
/// détection, enregistrement, alertes matérielles, score de santé, notifications, résumés et purge.
/// </summary>
public sealed partial class CollectionWorker(EventHub hub, StatusProvider status, ILogger<CollectionWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan SlowdownNotificationGap = TimeSpan.FromHours(1);

    private readonly ProcessCollector _processes = new();
    private readonly Baseline _baseline = new();

    private SismoStore? _store;
    private PerformanceCollector? _performance;
    private TemperatureCollector? _temperatures;
    private SaturationDetector? _saturation;
    private AnomalyDetector? _anomalies;

    private (double? Cpu, double? Gpu) _lastTemps;
    private IReadOnlyList<DiskHealth> _disks = [];
    private IReadOnlyList<VolumeSpace> _volumes = [];
    private long _tick;

    private DateTimeOffset _lastAggregate;
    private DateTimeOffset _lastBaselineSave;
    private DateTimeOffset _lastDiskCheck;
    private DateTimeOffset _lastVolumeCheck;
    private DateTimeOffset _lastAlertSync;
    private DateTimeOffset _lastPurge;
    private DateTimeOffset _lastSlowdownNotification;
    private DateTimeOffset _lastTrends;
    private DateTimeOffset _lastInventory;
    private IReadOnlyList<string> _degradedTrends = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Laisse Windows considérer le service comme démarré avant l'inventaire (WMI peut prendre quelques secondes).
        await Task.Yield();
        try { Initialize(); }
        catch (Exception ex)
        {
            LogTickFailed(logger, ex);
            Environment.Exit(1); // Permet la récupération du SCM après une erreur fatale.
            return;
        }
        LogStarted(logger, SismoPaths.Database, _temperatures!.IsAvailable);

        using var timer = new PeriodicTimer(Interval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                try
                {
                    Tick(DateTimeOffset.UtcNow);
                }
#pragma warning disable CA1031 // Une erreur ponctuelle ne doit jamais arrêter la collecte.
                catch (Exception ex)
#pragma warning restore CA1031
                {
                    LogTickFailed(logger, ex);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Arrêt normal du service.
        }
        finally
        {
            Shutdown();
        }
    }

    private void Initialize()
    {
        Directory.CreateDirectory(SismoPaths.DataDirectory);
        _store = new SismoStore(SismoPaths.Database);
        _store.LoadBaseline(_baseline);

        var learningStart = _store.GetSetting("learning_started_at") is { } s
            ? DateTimeOffset.Parse(s, CultureInfo.InvariantCulture)
            : DateTimeOffset.UtcNow;
        _store.SetSetting("learning_started_at", learningStart.ToString("O", CultureInfo.InvariantCulture));

        _saturation = new SaturationDetector(SaturationRule.Defaults());
        _anomalies = new AnomalyDetector(_baseline, learningStart);
        _performance = new PerformanceCollector();
        _temperatures = new TemperatureCollector();

        var now = DateTimeOffset.UtcNow;
        RefreshInventory(now);
        RecordBoots(now);
        RefreshDisks(now);
        RefreshVolumes(now);
        _lastAggregate = _lastBaselineSave = _lastPurge = now;
    }

    private void Tick(DateTimeOffset now)
    {
        var store = _store!;
        _tick++;

        var metrics = _performance!.Read(now);
        if (_tick % 2 == 0)
        {
            _lastTemps = _temperatures!.Read();
        }

        if (_lastTemps.Cpu is { } cpuTemp)
        {
            metrics[MetricKind.CpuTemperatureC] = cpuTemp;
        }

        if (_lastTemps.Gpu is { } gpuTemp)
        {
            metrics[MetricKind.GpuTemperatureC] = gpuTemp;
        }

        var snapshot = new Snapshot(now, metrics, _processes.Read(now));
        store.WriteSnapshot(snapshot);

        foreach (var episode in _saturation!.Process(snapshot).Concat(_anomalies!.Process(snapshot)))
        {
            Save(episode, now);
        }

        if (_tick % 12 == 0)
        {
            foreach (var active in _saturation.CurrentEpisodes().Concat(_anomalies.CurrentEpisodes()))
                store.AddEpisode(active, Explanations.Title(active), Explanations.Sentence(active));
        }

        if (now - _lastInventory >= TimeSpan.FromDays(1))
        {
            RefreshInventory(now);
        }

        if (now - _lastDiskCheck >= TimeSpan.FromHours(1))
        {
            RefreshDisks(now);
        }

        if (now - _lastVolumeCheck >= TimeSpan.FromMinutes(5))
        {
            RefreshVolumes(now);
        }

        // Températures critiques : vérifiées toutes les 30 s, pas seulement toutes les heures.
        if (now - _lastAlertSync >= TimeSpan.FromSeconds(30))
        {
            SyncAlerts(now);
            PublishStatus(now, snapshot);
        }

        if (now - _lastAggregate >= TimeSpan.FromMinutes(1))
        {
            store.Aggregate(now.AddHours(-1));
            _lastAggregate = now;
        }

        if (now - _lastBaselineSave >= TimeSpan.FromMinutes(10))
        {
            store.SaveBaseline(_baseline);
            _lastBaselineSave = now;
        }

        if (now - _lastPurge >= TimeSpan.FromDays(1))
        {
            store.Purge(now, new RetentionPolicy());
            _lastPurge = now;
        }
    }

    private void Save(Episode episode, DateTimeOffset now)
    {
        var title = Explanations.Title(episode);
        var sentence = Explanations.Sentence(episode);
        var id = _store!.AddEpisode(episode, title, sentence);
        LogEpisode(logger, title, sentence);

        // Réglages des spécifications : anomalies notifiées, ralentissements non (au plus un par heure si activés).
        var notify = episode.Kind == EpisodeKind.Anomaly
            ? Setting(SettingKeys.NotifyAnomaly, defaultValue: true)
            : Setting(SettingKeys.NotifySlowdown, defaultValue: false) && now - _lastSlowdownNotification >= SlowdownNotificationGap;

        if (notify)
        {
            if (episode.Kind == EpisodeKind.Slowdown)
            {
                _lastSlowdownNotification = now;
            }

            hub.Publish(new PipeEvent(
                episode.Kind == EpisodeKind.Anomaly ? "anomaly" : "slowdown", "info", title, sentence, id));
        }
    }

    private void SyncAlerts(DateTimeOffset now)
    {
        _lastAlertSync = now;
        var state = new HardwareState(_lastTemps.Cpu, _lastTemps.Gpu, _disks, _volumes);
        var unknown = new HashSet<AlertKind>();
        if (_lastTemps.Cpu is null) unknown.Add(AlertKind.CpuTemperature);
        if (_lastTemps.Gpu is null) unknown.Add(AlertKind.GpuTemperature);
        if (_disks.Count == 0) unknown.UnionWith([AlertKind.DiskTemperature, AlertKind.DiskSmartFailure, AlertKind.SsdWear]);
        foreach (var alert in _store!.SyncAlerts(HardwareAlertRules.Evaluate(state), now, unknown))
        {
            LogAlert(logger, alert.Subject, alert.Message);
            hub.Publish(new PipeEvent(
                "alert",
                alert.Severity == AlertSeverity.Critical ? "critical" : "warning",
                AlertTitle(alert),
                alert.Message));
        }
    }

    private void PublishStatus(DateTimeOffset now, Snapshot snapshot)
    {
        var anomalies = _anomalies!;
        var score = HealthScoreCalculator.Compute(new HealthInputs(
            _store!.ReadOpenAlerts(),
            anomalies.OpenCount(now),
            _store.CountSlowdowns(now.AddDays(-7)),
            DegradedTrends(now),
            anomalies.IsLearning(now)));

        status.Update(new LiveStatus(
            now,
            snapshot.Metrics.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
            score.Value,
            _lastTemps.Cpu is null || _lastTemps.Gpu is null || _disks.Count == 0 || !snapshot.Metrics.ContainsKey(MetricKind.CpuPercent) || !snapshot.Metrics.ContainsKey(MetricKind.MemoryUsedPercent) || !snapshot.Metrics.ContainsKey(MetricKind.DiskActivePercent) ? "Surveillance partielle - capteurs indisponibles" : score.Label,
            score.Penalties,
            anomalies.IsLearning(now),
            anomalies.LearningStartedAt + anomalies.LearningPeriod,
            _temperatures!.IsAvailable));
    }

    /// <summary>Indicateurs dégradés de plus de 20 % (US8), recalculés une fois par heure.</summary>
    private IReadOnlyList<string> DegradedTrends(DateTimeOffset now)
    {
        if (now - _lastTrends >= TimeSpan.FromHours(1))
        {
            _lastTrends = now;
            _degradedTrends = _store!.ReadTrends().Where(t => t.IsDegraded).Select(t => t.Name).ToList();
        }

        return _degradedTrends;
    }

    private static string AlertTitle(HardwareAlert alert) => alert.Kind switch
    {
        AlertKind.CpuTemperature => "Processeur trop chaud",
        AlertKind.GpuTemperature => "Carte graphique trop chaude",
        AlertKind.DiskTemperature => $"Disque trop chaud : {alert.Subject}",
        AlertKind.DiskSmartFailure => $"Risque de panne : {alert.Subject}",
        AlertKind.SsdWear => $"SSD usé : {alert.Subject}",
        AlertKind.LowDiskSpace => $"Espace disque faible sur {alert.Subject}",
        _ => alert.Subject,
    };

    private bool Setting(string key, bool defaultValue) =>
        _store!.GetSetting(key) is { } v ? string.Equals(v, "true", StringComparison.OrdinalIgnoreCase) : defaultValue;

    /// <summary>Inventaire au démarrage puis une fois par jour (changement de matériel, de pilote, de version de Windows).</summary>
    private void RefreshInventory(DateTimeOffset now)
    {
        _lastInventory = now;
        _store!.UpsertComponents(InventoryCollector.Read((family, ex) => LogInventoryFailed(logger, family, ex)), now);
    }

    private void RefreshDisks(DateTimeOffset now)
    {
        _lastDiskCheck = now;
        _disks = DiskHealthCollector.Read();
        _store!.AddDiskHealth(_disks, now);
    }

    private void RefreshVolumes(DateTimeOffset now)
    {
        _lastVolumeCheck = now;
        _volumes = DiskHealthCollector.Volumes();
    }

    private void RecordBoots(DateTimeOffset now)
    {
        var since = _store!.GetSetting("boots_read_until") is { } s
            ? DateTimeOffset.Parse(s, CultureInfo.InvariantCulture)
            : now.AddDays(-30);
        foreach (var (at, seconds) in BootCollector.ReadBoots(since))
        {
            _store.AddSystemEvent(at, "boot", "Démarrage de Windows", seconds);
        }

        _store.SetSetting("boots_read_until", now.ToString("O", CultureInfo.InvariantCulture));
    }

    private void Shutdown()
    {
        if (_store is null)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var episode in (_saturation?.Flush() ?? []).Concat(_anomalies?.Flush() ?? []))
        {
            Save(episode, now);
        }

        _store.SaveBaseline(_baseline);
        _performance?.Dispose();
        _temperatures?.Dispose();
        _store.Dispose();
        LogStopped(logger);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "SismoPC démarré, base {Database}, températures disponibles : {Temperatures}")]
    private static partial void LogStarted(ILogger logger, string database, bool temperatures);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Inventaire : lecture impossible de la famille {Family}")]
    private static partial void LogInventoryFailed(ILogger logger, string family, Exception ex);

    [LoggerMessage(Level = LogLevel.Information, Message = "SismoPC arrêté")]
    private static partial void LogStopped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Erreur pendant une collecte")]
    private static partial void LogTickFailed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Information, Message = "Épisode : {Title} — {Sentence}")]
    private static partial void LogEpisode(ILogger logger, string title, string sentence);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Alerte : {Subject} — {Message}")]
    private static partial void LogAlert(ILogger logger, string subject, string message);
}
