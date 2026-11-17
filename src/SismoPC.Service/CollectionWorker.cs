using System.Globalization;
using SismoPC.Collectors;
using SismoPC.Core.Detection;
using SismoPC.Core.Health;
using SismoPC.Core.Model;
using SismoPC.Core.Text;
using SismoPC.Data;

namespace SismoPC.Service;

/// <summary>
/// Boucle principale du service (US2) : collecte toutes les 5 s, détection, enregistrement,
/// résumés à la minute, santé du matériel toutes les heures, purge une fois par jour.
/// </summary>
public sealed partial class CollectionWorker(ILogger<CollectionWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    private SismoStore? _store;
    private PerformanceCollector? _performance;
    private readonly ProcessCollector _processes = new();
    private readonly Baseline _baseline = new();
    private SaturationDetector? _saturation;
    private AnomalyDetector? _anomalies;

    private DateTimeOffset _lastAggregate;
    private DateTimeOffset _lastBaselineSave;
    private DateTimeOffset _lastHealth;
    private DateTimeOffset _lastPurge;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Laisse Windows considérer le service comme démarré avant l'inventaire (WMI peut prendre quelques secondes).
        await Task.Yield();
        Initialize();
        LogStarted(logger, SismoPaths.Database);

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

        var now = DateTimeOffset.UtcNow;
        _store.UpsertComponents(InventoryCollector.Read(), now);
        RecordBoots(now);
        CheckHardware(now);
        _lastAggregate = _lastBaselineSave = _lastPurge = now;
    }

    private void Tick(DateTimeOffset now)
    {
        var store = _store!;
        var snapshot = new Snapshot(now, _performance!.Read(now), _processes.Read(now));
        store.WriteSnapshot(snapshot);

        foreach (var episode in _saturation!.Process(snapshot).Concat(_anomalies!.Process(snapshot)))
        {
            Save(episode);
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

        if (now - _lastHealth >= TimeSpan.FromHours(1))
        {
            CheckHardware(now);
        }

        if (now - _lastPurge >= TimeSpan.FromDays(1))
        {
            store.Purge(now, new RetentionPolicy());
            _lastPurge = now;
        }
    }

    private void Save(Episode episode)
    {
        var title = Explanations.Title(episode);
        var sentence = Explanations.Sentence(episode);
        _store!.AddEpisode(episode, title, sentence);
        LogEpisode(logger, title, sentence);
        // Itération suivante : transmettre l'épisode au Notifier par le canal nommé (US7).
    }

    private void CheckHardware(DateTimeOffset now)
    {
        _lastHealth = now;
        var disks = DiskHealthCollector.Read();
        _store!.AddDiskHealth(disks, now);

        // Les températures processeur et carte graphique arrivent avec LibreHardwareMonitorLib (itération suivante).
        var state = new HardwareState(null, null, disks, DiskHealthCollector.Volumes());
        foreach (var alert in _store.SyncAlerts(HardwareAlertRules.Evaluate(state), now))
        {
            LogAlert(logger, alert.Subject, alert.Message);
        }
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

        foreach (var episode in _saturation?.Flush() ?? [])
        {
            Save(episode);
        }

        _store.SaveBaseline(_baseline);
        _performance?.Dispose();
        _store.Dispose();
        LogStopped(logger);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "SismoPC démarré, base {Database}")]
    private static partial void LogStarted(ILogger logger, string database);

    [LoggerMessage(Level = LogLevel.Information, Message = "SismoPC arrêté")]
    private static partial void LogStopped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Erreur pendant une collecte")]
    private static partial void LogTickFailed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Information, Message = "Épisode : {Title} — {Sentence}")]
    private static partial void LogEpisode(ILogger logger, string title, string sentence);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Alerte : {Subject} — {Message}")]
    private static partial void LogAlert(ILogger logger, string subject, string message);
}
