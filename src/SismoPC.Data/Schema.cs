namespace SismoPC.Data;

/// <summary>Schéma de la base SQLite. Les dates sont stockées en millisecondes Unix (UTC).</summary>
internal static class Schema
{
    public const int Version = 2;

    public const string Create = """
        CREATE TABLE IF NOT EXISTS sample (
            ts     INTEGER NOT NULL,
            metric INTEGER NOT NULL,
            value  REAL    NOT NULL
        );
        CREATE INDEX IF NOT EXISTS ix_sample_metric_ts ON sample(metric, ts);
        CREATE INDEX IF NOT EXISTS ix_sample_ts ON sample(ts);

        CREATE TABLE IF NOT EXISTS sample_minute (
            ts     INTEGER NOT NULL,
            metric INTEGER NOT NULL,
            min    REAL    NOT NULL,
            avg    REAL    NOT NULL,
            max    REAL    NOT NULL,
            PRIMARY KEY (metric, ts)
        ) WITHOUT ROWID;

        CREATE TABLE IF NOT EXISTS sample_hour (
            ts     INTEGER NOT NULL,
            metric INTEGER NOT NULL,
            min    REAL    NOT NULL,
            avg    REAL    NOT NULL,
            max    REAL    NOT NULL,
            PRIMARY KEY (metric, ts)
        ) WITHOUT ROWID;

        CREATE TABLE IF NOT EXISTS process_sample (
            ts        INTEGER NOT NULL,
            name      TEXT    NOT NULL,
            cpu       REAL    NOT NULL,
            ws        INTEGER NOT NULL,
            disk      REAL    NOT NULL
        );
        CREATE INDEX IF NOT EXISTS ix_process_sample_ts ON process_sample(ts);

        CREATE TABLE IF NOT EXISTS episode (
            id        INTEGER PRIMARY KEY,
            kind      INTEGER NOT NULL,
            resource  INTEGER NOT NULL,
            metric    INTEGER NOT NULL,
            start_ts  INTEGER NOT NULL,
            end_ts    INTEGER NOT NULL,
            peak      REAL    NOT NULL,
            usual     REAL,
            culprit   TEXT,
            share     REAL    NOT NULL,
            title     TEXT    NOT NULL,
            sentence  TEXT    NOT NULL,
            ignored   INTEGER NOT NULL DEFAULT 0
        );
        CREATE INDEX IF NOT EXISTS ix_episode_start ON episode(start_ts);

        CREATE TABLE IF NOT EXISTS alert (
            id        INTEGER PRIMARY KEY,
            kind      INTEGER NOT NULL,
            severity  INTEGER NOT NULL,
            subject   TEXT    NOT NULL,
            message   TEXT    NOT NULL,
            raised    INTEGER NOT NULL,
            cleared   INTEGER,
            notified  INTEGER
        );

        CREATE TABLE IF NOT EXISTS component (
            type       TEXT    NOT NULL,
            name       TEXT    NOT NULL,
            details    TEXT    NOT NULL,
            first_seen INTEGER NOT NULL,
            last_seen  INTEGER NOT NULL,
            PRIMARY KEY (type, name)
        ) WITHOUT ROWID;

        CREATE TABLE IF NOT EXISTS component_instance (
            type TEXT NOT NULL,
            identity TEXT NOT NULL,
            name TEXT NOT NULL,
            details TEXT NOT NULL,
            first_seen INTEGER NOT NULL,
            last_seen INTEGER NOT NULL,
            PRIMARY KEY(type, identity)
        ) WITHOUT ROWID;

        CREATE TABLE IF NOT EXISTS baseline (
            metric  INTEGER NOT NULL,
            hour    INTEGER NOT NULL,
            weekend INTEGER NOT NULL,
            n       INTEGER NOT NULL,
            mean    REAL    NOT NULL,
            m2      REAL    NOT NULL,
            PRIMARY KEY (metric, hour, weekend)
        ) WITHOUT ROWID;

        CREATE TABLE IF NOT EXISTS disk_health (
            ts      INTEGER NOT NULL,
            name    TEXT    NOT NULL,
            wear    REAL,
            temp    REAL,
            hours   INTEGER,
            failing INTEGER NOT NULL,
            ssd     INTEGER NOT NULL DEFAULT 0
        );

        CREATE TABLE IF NOT EXISTS system_event (
            ts      INTEGER NOT NULL,
            kind    TEXT    NOT NULL,
            label   TEXT    NOT NULL,
            value   REAL
        );
        CREATE INDEX IF NOT EXISTS ix_system_event_ts ON system_event(ts);

        CREATE TABLE IF NOT EXISTS setting (
            key   TEXT PRIMARY KEY,
            value TEXT NOT NULL
        ) WITHOUT ROWID;
        """;
}
