using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SismoPC.Core.Detection;
using SismoPC.Core.Health;
using SismoPC.Core.Model;

namespace SismoPC.Data;

/// <summary>Durées de conservation de l'historique (spécifications : 7 jours, 90 jours, 2 ans).</summary>
public sealed record RetentionPolicy
{
    public TimeSpan Detail { get; init; } = TimeSpan.FromDays(7);

    public TimeSpan Minute { get; init; } = TimeSpan.FromDays(90);

    public TimeSpan Hour { get; init; } = TimeSpan.FromDays(730);
}

/// <summary>Épisode enregistré, avec son identifiant et ses textes.</summary>
public sealed record StoredEpisode(long Id, Episode Episode, string Title, string Sentence);

/// <summary>Point agrégé d'une courbe.</summary>
public readonly record struct SeriesPoint(DateTimeOffset Timestamp, double Min, double Avg, double Max);

/// <summary>
/// Accès à la base SQLite de SismoPC. Le service est le seul à écrire ; l'interface ouvre la base en lecture seule.
/// </summary>
public sealed class SismoStore : IDisposable
{
    private readonly SqliteConnection _connection;

    public SismoStore(string databasePath, bool readOnly = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(databasePath);
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        };
        _connection = new SqliteConnection(builder.ToString());
        _connection.Open();

        if (!readOnly)
        {
            var existingVersion = Convert.ToInt32(Command("PRAGMA user_version").ExecuteScalar(), CultureInfo.InvariantCulture);
            if (existingVersion > Schema.Version) throw new InvalidOperationException("Base créée par une version plus récente de SismoPC.");
            Execute("PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;");
            Execute(Schema.Create);
            Execute($"PRAGMA user_version={Schema.Version}");
            SetSetting("schema_version", Schema.Version.ToString(CultureInfo.InvariantCulture));
        }
    }

    public void Dispose() => _connection.Dispose();

    // ---------- Mesures ----------

    public void WriteSnapshot(Snapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var ts = snapshot.Timestamp.ToUnixTimeMilliseconds();
        using var tx = _connection.BeginTransaction();

        using (var cmd = Command("INSERT INTO sample(ts, metric, value) VALUES ($ts, $m, $v)", tx))
        {
            var pm = cmd.Parameters.Add("$m", SqliteType.Integer);
            var pv = cmd.Parameters.Add("$v", SqliteType.Real);
            cmd.Parameters.AddWithValue("$ts", ts);
            foreach (var (metric, value) in snapshot.Metrics)
            {
                pm.Value = (int)metric;
                pv.Value = value;
                cmd.ExecuteNonQuery();
            }
        }

        using (var cmd = Command("INSERT INTO process_sample(ts, name, cpu, ws, disk) VALUES ($ts, $n, $c, $w, $d)", tx))
        {
            cmd.Parameters.AddWithValue("$ts", ts);
            var pn = cmd.Parameters.Add("$n", SqliteType.Text);
            var pc = cmd.Parameters.Add("$c", SqliteType.Real);
            var pw = cmd.Parameters.Add("$w", SqliteType.Integer);
            var pd = cmd.Parameters.Add("$d", SqliteType.Real);
            foreach (var p in snapshot.TopProcesses)
            {
                pn.Value = p.Name;
                pc.Value = p.CpuPercent;
                pw.Value = p.WorkingSetBytes;
                pd.Value = p.DiskBytesPerSecond;
                cmd.ExecuteNonQuery();
            }
        }

        tx.Commit();
    }

    /// <summary>Résume au pas de la minute et de l'heure les mesures depuis <paramref name="since"/>.</summary>
    public void Aggregate(DateTimeOffset since)
    {
        var from = since.ToUnixTimeMilliseconds();
        Execute("""
            INSERT OR REPLACE INTO sample_minute(ts, metric, min, avg, max)
            SELECT (ts / 60000) * 60000 AS bucket, metric, MIN(value), AVG(value), MAX(value)
            FROM sample WHERE ts >= ($from / 60000) * 60000
            GROUP BY bucket, metric;

            INSERT OR REPLACE INTO sample_hour(ts, metric, min, avg, max)
            SELECT (ts / 3600000) * 3600000 AS bucket, metric, MIN(min), AVG(avg), MAX(max)
            FROM sample_minute WHERE ts >= ($from / 3600000) * 3600000
            GROUP BY bucket, metric;
            """, ("$from", from));
    }

    /// <summary>Supprime ce qui dépasse les durées de conservation.</summary>
    public void Purge(DateTimeOffset now, RetentionPolicy retention)
    {
        ArgumentNullException.ThrowIfNull(retention);
        Execute("""
            DELETE FROM sample WHERE ts < $d;
            DELETE FROM process_sample WHERE ts < $d;
            DELETE FROM sample_minute WHERE ts < $m;
            DELETE FROM sample_hour WHERE ts < $h;
            """,
            ("$d", (now - retention.Detail).ToUnixTimeMilliseconds()),
            ("$m", (now - retention.Minute).ToUnixTimeMilliseconds()),
            ("$h", (now - retention.Hour).ToUnixTimeMilliseconds()));
    }

    /// <summary>Courbe d'une mesure : détail, minute ou heure selon la longueur de la période.</summary>
    public IReadOnlyList<SeriesPoint> ReadSeries(MetricKind metric, DateTimeOffset from, DateTimeOffset to)
    {
        // Les résolutions anciennes restent consultables, même dans une petite fenêtre.
        var span = to - from;
        var sql = span <= TimeSpan.FromHours(6)
            ? """
              SELECT ts, value, value, value FROM sample WHERE metric=$m AND ts BETWEEN $f AND $t
              UNION ALL
              SELECT ts,min,avg,max FROM sample_minute a WHERE metric=$m AND ts BETWEEN ($f / 60000) * 60000 AND $t
                AND NOT EXISTS (SELECT 1 FROM sample d WHERE d.metric=$m AND d.ts >= a.ts AND d.ts < a.ts+60000)
              UNION ALL
              SELECT ts,min,avg,max FROM sample_hour a WHERE metric=$m AND ts BETWEEN ($f / 3600000) * 3600000 AND $t
                AND NOT EXISTS (SELECT 1 FROM sample_minute d WHERE d.metric=$m AND d.ts >= a.ts AND d.ts < a.ts+3600000)
                AND NOT EXISTS (SELECT 1 FROM sample d WHERE d.metric=$m AND d.ts >= a.ts AND d.ts < a.ts+3600000)
              ORDER BY ts
              """
            : span <= TimeSpan.FromDays(30)
                ? """
                  SELECT ts,min,avg,max FROM sample_minute WHERE metric=$m AND ts BETWEEN ($f / 60000) * 60000 AND $t
                  UNION ALL
                  SELECT ts,min,avg,max FROM sample_hour a WHERE metric=$m AND ts BETWEEN ($f / 3600000) * 3600000 AND $t
                    AND NOT EXISTS (SELECT 1 FROM sample_minute d WHERE d.metric=$m AND d.ts >= a.ts AND d.ts < a.ts+3600000)
                  ORDER BY ts
                  """
                : "SELECT ts,min,avg,max FROM sample_hour WHERE metric=$m AND ts BETWEEN ($f / 3600000) * 3600000 AND $t ORDER BY ts";

        using var cmd = Command(sql);
        cmd.Parameters.AddWithValue("$m", (int)metric);
        cmd.Parameters.AddWithValue("$f", from.ToUnixTimeMilliseconds());
        cmd.Parameters.AddWithValue("$t", to.ToUnixTimeMilliseconds());
        using var reader = cmd.ExecuteReader();
        var points = new List<SeriesPoint>();
        while (reader.Read())
        {
            points.Add(new SeriesPoint(
                DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(0)),
                reader.GetDouble(1), reader.GetDouble(2), reader.GetDouble(3)));
        }

        return points;
    }

    /// <summary>Programmes enregistrés à l'instant le plus proche de <paramref name="at"/> (US3).</summary>
    public IReadOnlyList<ProcessUsage> ReadProcessesAt(DateTimeOffset at)
    {
        using var cmd = Command("""
            SELECT name, cpu, ws, disk FROM process_sample
            WHERE ts = (SELECT ts FROM process_sample WHERE ts BETWEEN $at - 10000 AND $at + 10000 ORDER BY ABS(ts - $at) LIMIT 1)
            ORDER BY cpu DESC
            """);
        cmd.Parameters.AddWithValue("$at", at.ToUnixTimeMilliseconds());
        using var reader = cmd.ExecuteReader();
        var list = new List<ProcessUsage>();
        while (reader.Read())
        {
            list.Add(new ProcessUsage(reader.GetString(0), reader.GetDouble(1), reader.GetInt64(2), reader.GetDouble(3)));
        }

        return list;
    }

    // ---------- Épisodes ----------

    public long AddEpisode(Episode e, string title, string sentence)
    {
        ArgumentNullException.ThrowIfNull(e);
        using (var existing = Command("SELECT id FROM episode WHERE kind=$k AND metric=$m AND start_ts=$s LIMIT 1"))
        {
            existing.Parameters.AddWithValue("$k", (int)e.Kind);
            existing.Parameters.AddWithValue("$m", (int)e.Metric);
            existing.Parameters.AddWithValue("$s", e.Start.ToUnixTimeMilliseconds());
            if (existing.ExecuteScalar() is long id)
            {
                Execute("UPDATE episode SET end_ts=$end, peak=$peak, culprit=$culprit, share=$share, title=$title, sentence=$sentence WHERE id=$id",
                    ("$end",e.End.ToUnixTimeMilliseconds()),("$peak",e.PeakValue),("$culprit",(object?)e.Culprit ?? DBNull.Value),
                    ("$share",e.CulpritShare),("$title",title),("$sentence",sentence),("$id",id));
                return id;
            }
        }
        using var cmd = Command("""
            INSERT INTO episode(kind, resource, metric, start_ts, end_ts, peak, usual, culprit, share, title, sentence)
            VALUES ($k, $r, $m, $s, $e, $p, $u, $c, $sh, $ti, $se);
            SELECT last_insert_rowid();
            """);
        cmd.Parameters.AddWithValue("$k", (int)e.Kind);
        cmd.Parameters.AddWithValue("$r", (int)e.Resource);
        cmd.Parameters.AddWithValue("$m", (int)e.Metric);
        cmd.Parameters.AddWithValue("$s", e.Start.ToUnixTimeMilliseconds());
        cmd.Parameters.AddWithValue("$e", e.End.ToUnixTimeMilliseconds());
        cmd.Parameters.AddWithValue("$p", e.PeakValue);
        cmd.Parameters.AddWithValue("$u", (object?)e.UsualValue ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$c", (object?)e.Culprit ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$sh", e.CulpritShare);
        cmd.Parameters.AddWithValue("$ti", title);
        cmd.Parameters.AddWithValue("$se", sentence);
        return (long)cmd.ExecuteScalar()!;
    }

    public IReadOnlyList<StoredEpisode> ReadEpisodes(DateTimeOffset from, DateTimeOffset to)
    {
        using var cmd = Command("""
            SELECT id, kind, resource, metric, start_ts, end_ts, peak, usual, culprit, share, title, sentence
            FROM episode WHERE start_ts BETWEEN $f AND $t AND ignored = 0 ORDER BY start_ts DESC
            """);
        cmd.Parameters.AddWithValue("$f", from.ToUnixTimeMilliseconds());
        cmd.Parameters.AddWithValue("$t", to.ToUnixTimeMilliseconds());
        using var r = cmd.ExecuteReader();
        var list = new List<StoredEpisode>();
        while (r.Read())
        {
            var episode = new Episode(
                (EpisodeKind)r.GetInt32(1), (Resource)r.GetInt32(2), (MetricKind)r.GetInt32(3),
                DateTimeOffset.FromUnixTimeMilliseconds(r.GetInt64(4)), DateTimeOffset.FromUnixTimeMilliseconds(r.GetInt64(5)),
                r.GetDouble(6), r.IsDBNull(7) ? null : r.GetDouble(7), r.IsDBNull(8) ? null : r.GetString(8), r.GetDouble(9));
            list.Add(new StoredEpisode(r.GetInt64(0), episode, r.GetString(10), r.GetString(11)));
        }

        return list;
    }

    public int CountSlowdowns(DateTimeOffset since)
    {
        using var cmd = Command("SELECT COUNT(*) FROM episode WHERE kind = 0 AND ignored = 0 AND start_ts >= $s");
        cmd.Parameters.AddWithValue("$s", since.ToUnixTimeMilliseconds());
        return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    // ---------- Alertes ----------

    /// <summary>Aligne les alertes en base sur celles en cours ; renvoie les nouvelles (à notifier).</summary>
    public IReadOnlyList<HardwareAlert> SyncAlerts(IReadOnlyList<HardwareAlert> current, DateTimeOffset now, IReadOnlySet<AlertKind>? unknown = null)
    {
        ArgumentNullException.ThrowIfNull(current);
        var open = new List<(long Id, AlertKind Kind, string Subject)>();
        using (var cmd = Command("SELECT id, kind, subject FROM alert WHERE cleared IS NULL"))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
            {
                open.Add((r.GetInt64(0), (AlertKind)r.GetInt32(1), r.GetString(2)));
            }
        }

        var ms = now.ToUnixTimeMilliseconds();
        var raised = new List<HardwareAlert>();
        using var tx = _connection.BeginTransaction();

        foreach (var o in open.Where(o => unknown?.Contains(o.Kind) != true && !current.Any(c => c.Kind == o.Kind && c.Subject == o.Subject)))
        {
            using var cmd = Command("UPDATE alert SET cleared = $now WHERE id = $id", tx);
            cmd.Parameters.AddWithValue("$now", ms);
            cmd.Parameters.AddWithValue("$id", o.Id);
            cmd.ExecuteNonQuery();
        }

        foreach (var c in current)
        {
            using var update = Command("UPDATE alert SET severity = $s, message = $m WHERE kind = $k AND subject = $su AND cleared IS NULL", tx);
            update.Parameters.AddWithValue("$s", (int)c.Severity);
            update.Parameters.AddWithValue("$m", c.Message);
            update.Parameters.AddWithValue("$k", (int)c.Kind);
            update.Parameters.AddWithValue("$su", c.Subject);
            update.ExecuteNonQuery();
        }

        foreach (var c in current.Where(c => !open.Any(o => o.Kind == c.Kind && o.Subject == c.Subject)))
        {
            using var cmd = Command("INSERT INTO alert(kind, severity, subject, message, raised) VALUES ($k, $s, $su, $m, $now)", tx);
            cmd.Parameters.AddWithValue("$k", (int)c.Kind);
            cmd.Parameters.AddWithValue("$s", (int)c.Severity);
            cmd.Parameters.AddWithValue("$su", c.Subject);
            cmd.Parameters.AddWithValue("$m", c.Message);
            cmd.Parameters.AddWithValue("$now", ms);
            cmd.ExecuteNonQuery();
            raised.Add(c);
        }

        tx.Commit();
        return raised;
    }

    /// <summary>Alertes encore en cours (non levées).</summary>
    public IReadOnlyList<HardwareAlert> ReadOpenAlerts()
    {
        using var cmd = Command("SELECT kind, severity, subject, message FROM alert WHERE cleared IS NULL ORDER BY severity DESC, raised");
        using var r = cmd.ExecuteReader();
        var list = new List<HardwareAlert>();
        while (r.Read())
        {
            list.Add(new HardwareAlert((AlertKind)r.GetInt32(0), (AlertSeverity)r.GetInt32(1), r.GetString(2), r.GetString(3)));
        }

        return list;
    }

    // ---------- Inventaire ----------

    public void UpsertComponents(IEnumerable<HardwareComponent> components, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(components);
        var ms = now.ToUnixTimeMilliseconds();
        using var tx = _connection.BeginTransaction();
        foreach (var c in components)
        {
            using var cmd = Command("""
                INSERT INTO component(type, name, details, first_seen, last_seen) VALUES ($t, $n, $d, $now, $now)
                ON CONFLICT(type, name) DO UPDATE SET details = excluded.details, last_seen = excluded.last_seen
                """, tx);
            cmd.Parameters.AddWithValue("$t", c.Type);
            cmd.Parameters.AddWithValue("$n", c.Name);
            cmd.Parameters.AddWithValue("$d", JsonSerializer.Serialize(c.Details));
            cmd.Parameters.AddWithValue("$now", ms);
            cmd.ExecuteNonQuery();
        }

        tx.Commit();
    }

    public IReadOnlyList<HardwareComponent> ReadComponents()
    {
        // Inventaire courant uniquement : un composant retiré reste en base (historique) mais n'est plus affiché.
        using var cmd = Command("""
            SELECT type, name, details FROM component
            WHERE last_seen = (SELECT MAX(last_seen) FROM component)
            ORDER BY type, name
            """);
        using var r = cmd.ExecuteReader();
        var list = new List<HardwareComponent>();
        while (r.Read())
        {
            var details = JsonSerializer.Deserialize<Dictionary<string, string>>(r.GetString(2)) ?? [];
            list.Add(new HardwareComponent(r.GetString(0), r.GetString(1), details));
        }

        return list;
    }

    // ---------- Normale ----------

    public void SaveBaseline(Baseline baseline)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        using var tx = _connection.BeginTransaction();
        foreach (var (slot, stats) in baseline.Slots)
        {
            using var cmd = Command("INSERT OR REPLACE INTO baseline(metric, hour, weekend, n, mean, m2) VALUES ($m, $h, $w, $n, $mean, $m2)", tx);
            cmd.Parameters.AddWithValue("$m", (int)slot.Metric);
            cmd.Parameters.AddWithValue("$h", slot.Hour);
            cmd.Parameters.AddWithValue("$w", slot.Weekend ? 1 : 0);
            cmd.Parameters.AddWithValue("$n", stats.Count);
            cmd.Parameters.AddWithValue("$mean", stats.Mean);
            cmd.Parameters.AddWithValue("$m2", stats.M2);
            cmd.ExecuteNonQuery();
        }

        tx.Commit();
    }

    public void LoadBaseline(Baseline baseline)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        using var cmd = Command("SELECT metric, hour, weekend, n, mean, m2 FROM baseline");
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            baseline.Load(
                new BaselineSlot((MetricKind)r.GetInt32(0), r.GetInt32(1), r.GetInt32(2) == 1),
                new RunningStats(r.GetInt64(3), r.GetDouble(4), r.GetDouble(5)));
        }
    }

    // ---------- Santé des disques et événements ----------

    public void AddDiskHealth(IEnumerable<DiskHealth> disks, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(disks);
        using var tx = _connection.BeginTransaction();
        foreach (var d in disks)
        {
            using var cmd = Command("INSERT INTO disk_health(ts, name, wear, temp, hours, failing, ssd) VALUES ($ts, $n, $w, $t, $h, $f, $s)", tx);
            cmd.Parameters.AddWithValue("$ts", now.ToUnixTimeMilliseconds());
            cmd.Parameters.AddWithValue("$n", d.Name);
            cmd.Parameters.AddWithValue("$w", (object?)d.WearPercent ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$t", (object?)d.TemperatureC ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$h", (object?)d.PowerOnHours ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$f", d.PredictsFailure ? 1 : 0);
            cmd.Parameters.AddWithValue("$s", d.IsSsd ? 1 : 0);
            cmd.ExecuteNonQuery();
        }

        tx.Commit();
    }

    public void AddSystemEvent(DateTimeOffset at, string kind, string label, double? value = null)
    {
        Execute("INSERT INTO system_event(ts, kind, label, value) VALUES ($ts, $k, $l, $v)",
            ("$ts", at.ToUnixTimeMilliseconds()), ("$k", kind), ("$l", label), ("$v", (object?)value ?? DBNull.Value));
    }

    public IReadOnlyList<(DateTimeOffset At, string Kind, string Label, double? Value)> ReadSystemEvents(DateTimeOffset from, DateTimeOffset to)
    {
        using var cmd = Command("SELECT ts, kind, label, value FROM system_event WHERE ts BETWEEN $f AND $t ORDER BY ts");
        cmd.Parameters.AddWithValue("$f", from.ToUnixTimeMilliseconds());
        cmd.Parameters.AddWithValue("$t", to.ToUnixTimeMilliseconds());
        using var r = cmd.ExecuteReader();
        var list = new List<(DateTimeOffset, string, string, double?)>();
        while (r.Read())
        {
            list.Add((DateTimeOffset.FromUnixTimeMilliseconds(r.GetInt64(0)), r.GetString(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetDouble(3)));
        }

        return list;
    }

    /// <summary>Dernier relevé de santé de chaque disque.</summary>
    public IReadOnlyList<DiskHealth> ReadLatestDiskHealth()
    {
        using var cmd = Command("""
            SELECT d.name, d.wear, d.temp, d.hours, d.failing, d.ssd FROM disk_health d
            JOIN (SELECT name, MAX(ts) AS ts FROM disk_health GROUP BY name) last ON last.name = d.name AND last.ts = d.ts
            ORDER BY d.name
            """);
        using var r = cmd.ExecuteReader();
        var list = new List<DiskHealth>();
        while (r.Read())
        {
            list.Add(new DiskHealth(
                r.GetString(0),
                IsSsd: r.GetInt32(5) == 1,
                PredictsFailure: r.GetInt32(4) == 1,
                WearPercent: r.IsDBNull(1) ? null : r.GetDouble(1),
                TemperatureC: r.IsDBNull(2) ? null : r.GetDouble(2),
                PowerOnHours: r.IsDBNull(3) ? null : r.GetInt64(3)));
        }

        return list;
    }

    public StoredEpisode? ReadEpisode(long id)
    {
        using var cmd = Command("SELECT start_ts FROM episode WHERE id = $id");
        cmd.Parameters.AddWithValue("$id", id);
        if (cmd.ExecuteScalar() is not long start)
        {
            return null;
        }

        var at = DateTimeOffset.FromUnixTimeMilliseconds(start);
        return ReadEpisodes(at, at).FirstOrDefault(e => e.Id == id);
    }

    public void IgnoreEpisode(long id) => Execute("UPDATE episode SET ignored = 1 WHERE id = $id", ("$id", id));

    // ---------- Tendances (US8) ----------

    /// <summary>Durée moyenne de démarrage par mois, en secondes.</summary>
    public IReadOnlyList<MonthlyValue> ReadMonthlyBootSeconds() => ReadMonthly("""
        SELECT strftime('%Y-%m', ts / 1000, 'unixepoch') AS month, AVG(value)
        FROM system_event WHERE kind = 'boot' AND value IS NOT NULL GROUP BY month ORDER BY month
        """);

    /// <summary>Valeur « au repos » d'une mesure par mois : moyenne des minimums horaires.</summary>
    public IReadOnlyList<MonthlyValue> ReadMonthlyAtRest(MetricKind metric) => ReadMonthly("""
        SELECT strftime('%Y-%m', ts / 1000, 'unixepoch') AS month, AVG(min)
        FROM sample_hour WHERE metric = $m GROUP BY month ORDER BY month
        """, ("$m", (int)metric));

    /// <summary>Usure maximale des SSD par mois, en pour cent.</summary>
    public IReadOnlyList<MonthlyValue> ReadMonthlySsdWear() => ReadMonthly("""
        SELECT strftime('%Y-%m', ts / 1000, 'unixepoch') AS month, MAX(wear)
        FROM disk_health WHERE wear IS NOT NULL GROUP BY month ORDER BY month
        """);

    /// <summary>Les quatre indicateurs de dégradation des spécifications.</summary>
    public IReadOnlyList<Trend> ReadTrends() =>
    [
        new Trend("Durée de démarrage", "s", ReadMonthlyBootSeconds()),
        new Trend("Mémoire au repos", "%", ReadMonthlyAtRest(MetricKind.MemoryUsedPercent)),
        new Trend("Température du processeur au repos", "°C", ReadMonthlyAtRest(MetricKind.CpuTemperatureC)),
        new Trend("Usure du SSD", "%", ReadMonthlySsdWear()),
    ];

    private List<MonthlyValue> ReadMonthly(string sql, params (string Name, object Value)[] parameters)
    {
        using var cmd = Command(sql);
        foreach (var (name, value) in parameters)
        {
            cmd.Parameters.AddWithValue(name, value);
        }

        using var r = cmd.ExecuteReader();
        var list = new List<MonthlyValue>();
        while (r.Read())
        {
            if (!r.IsDBNull(1))
            {
                list.Add(new MonthlyValue(r.GetString(0), r.GetDouble(1)));
            }
        }

        return list;
    }

    /// <summary>Taille de la base sur le disque, fichiers WAL compris.</summary>
    public long DatabaseSizeBytes()
    {
        var path = _connection.DataSource;
        return new[] { path, path + "-wal", path + "-shm" }
            .Where(File.Exists)
            .Sum(p => new FileInfo(p).Length);
    }

    // ---------- Réglages ----------

    public string? GetSetting(string key)
    {
        using var cmd = Command("SELECT value FROM setting WHERE key = $k");
        cmd.Parameters.AddWithValue("$k", key);
        return cmd.ExecuteScalar() as string;
    }

    public void SetSetting(string key, string value) =>
        Execute("INSERT OR REPLACE INTO setting(key, value) VALUES ($k, $v)", ("$k", key), ("$v", value));

    // ---------- Outils ----------

    private SqliteCommand Command(string sql, SqliteTransaction? tx = null)
    {
        var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Transaction = tx;
        return cmd;
    }

    private void Execute(string sql, params (string Name, object Value)[] parameters)
    {
        using var cmd = Command(sql);
        foreach (var (name, value) in parameters)
        {
            cmd.Parameters.AddWithValue(name, value);
        }

        cmd.ExecuteNonQuery();
    }
}
