using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Replicator.Core.Audit;

// All connections are short-lived. Transactions keep jobs, artifacts, and import cursors atomic.
public sealed class SqliteAuditStore(string databasePath)
{
    private readonly object _gate = new();

    private SqliteConnection Open()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databasePath))!);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath, Pooling = false, DefaultTimeout = 1, ForeignKeys = true
        }.ToString());
        try
        {
            connection.Open();
            using var transaction = connection.BeginTransaction();
            var version = Convert.ToInt32(Scalar(connection, transaction, "PRAGMA user_version"));
            if (version > 1) throw new InvalidOperationException($"Unsupported audit schema version {version}.");
            if (version == 0)
            {
                Execute(connection, transaction, """
                    CREATE TABLE schema_metadata(version INTEGER PRIMARY KEY, applied_utc TEXT NOT NULL);
                    INSERT INTO schema_metadata VALUES(1, strftime('%Y-%m-%dT%H:%M:%fZ','now'));
                    CREATE TABLE jobs(
                        id TEXT PRIMARY KEY, profile_id TEXT, profile_snapshot TEXT NOT NULL,
                        operation TEXT NOT NULL, status TEXT NOT NULL, source_path TEXT NOT NULL,
                        destination_path TEXT NOT NULL, started_utc TEXT, completed_utc TEXT,
                        elapsed_ms REAL, process_exit_code INTEGER, robocopy_exit_code INTEGER, reported_exit_code INTEGER,
                        summary TEXT NOT NULL, error TEXT NOT NULL, counters_json TEXT NOT NULL,
                        provenance TEXT NOT NULL, parser_confidence TEXT NOT NULL);
                    CREATE INDEX jobs_profile_started ON jobs(profile_id, started_utc);
                    CREATE TABLE job_events(id INTEGER PRIMARY KEY, job_id TEXT NOT NULL REFERENCES jobs(id),
                        timestamp_utc TEXT NOT NULL, code TEXT NOT NULL, message TEXT NOT NULL);
                    CREATE TABLE job_artifacts(id TEXT PRIMARY KEY, job_id TEXT NOT NULL REFERENCES jobs(id),
                        kind TEXT NOT NULL, path TEXT NOT NULL COLLATE NOCASE, size INTEGER,
                        last_write_utc TEXT, sha256 TEXT, last_seen_utc TEXT,
                        UNIQUE(job_id, kind, path));
                    CREATE INDEX artifacts_path ON job_artifacts(path);
                    CREATE TABLE system_events(id INTEGER PRIMARY KEY, timestamp_utc TEXT NOT NULL,
                        severity TEXT NOT NULL, category TEXT NOT NULL, code TEXT NOT NULL,
                        message TEXT NOT NULL, details TEXT, job_id TEXT, profile_id TEXT,
                        artifact_id TEXT, import_batch_id TEXT);
                    CREATE TABLE import_sources(path TEXT PRIMARY KEY COLLATE NOCASE, size INTEGER NOT NULL,
                        last_write_utc TEXT NOT NULL, sha256 TEXT NOT NULL, job_id TEXT NOT NULL REFERENCES jobs(id));
                    PRAGMA user_version=1;
                    """);
                WriteEvent(connection, transaction, new("storage.migrated", "Audit schema initialized at version 1.", "storage"));
            }
            transaction.Commit();
            return connection;
        }
        catch { connection.Dispose(); throw; }
    }

    public void Initialize()
    {
        lock (_gate) { using var connection = Open(); }
    }

    public void SaveJob(AuditJob job)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var transaction = connection.BeginTransaction();
            SaveJob(connection, transaction, job);
            transaction.Commit();
        }
    }

    public void WriteEvent(AuditSystemEvent entry)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var transaction = connection.BeginTransaction();
            WriteEvent(connection, transaction, entry);
            transaction.Commit();
        }
    }

    public void DetachProfile(Guid profileId)
    {
        lock (_gate)
        {
            using var connection = Open();
            Execute(connection, null, "UPDATE jobs SET profile_id=NULL WHERE profile_id=$id", ("$id", profileId.ToString()));
        }
    }

    public void ReconcileProfiles(IReadOnlyCollection<Guid> profileIds)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var transaction = connection.BeginTransaction();
            var ids = new List<string>();
            using (var command = Command(connection, transaction, "SELECT DISTINCT profile_id FROM jobs WHERE profile_id IS NOT NULL"))
            using (var reader = command.ExecuteReader())
                while (reader.Read()) ids.Add(reader.GetString(0));
            foreach (var id in ids.Where(id => !profileIds.Contains(Guid.Parse(id))))
                Execute(connection, transaction, "UPDATE jobs SET profile_id=NULL WHERE profile_id=$id", ("$id", id));
            transaction.Commit();
        }
    }

    public (int Imported, int Skipped) ImportBatch(IReadOnlyList<ImportedJob> items)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var transaction = connection.BeginTransaction();
            var imported = 0;
            var skipped = 0;
            foreach (var item in items)
            {
                var source = item.Source;
                var unchanged = Scalar(connection, transaction,
                    "SELECT job_id FROM import_sources WHERE path=$path AND size=$size AND last_write_utc=$utc AND sha256=$hash",
                    ("$path", source.Path), ("$size", source.Size), ("$utc", Utc(source.LastWriteUtc)), ("$hash", source.Sha256));
                if (unchanged is not null) { skipped++; continue; }

                // An app run reserves its exact log path before launching PowerShell. Import never duplicates it.
                var log = item.Job.Artifacts.First(a => a.Kind == "log").Path;
                var existingId = Scalar(connection, transaction,
                    "SELECT job_id FROM job_artifacts WHERE kind='log' AND path=$path LIMIT 1", ("$path", log)) as string;
                if (existingId is not null)
                {
                    item.Job.Id = existingId;
                    var provenance = Scalar(connection, transaction, "SELECT provenance FROM jobs WHERE id=$id", ("$id", existingId)) as string;
                    if (provenance == "app")
                    {
                        // Preserve direct outcomes (including canceled/failed). Import only adds evidence.
                        foreach (var artifact in item.Job.Artifacts) SaveArtifact(connection, transaction, existingId, artifact);
                    }
                    else if (item.Enrichment)
                    {
                        // Preserve parsed counters and the original snapshot if status has no replacement fields.
                        Execute(connection, transaction, """
                            UPDATE jobs SET status=$status, started_utc=COALESCE($started,started_utc), completed_utc=$completed,
                                reported_exit_code=$exit, summary=$summary, error=$error, parser_confidence=$confidence,
                                elapsed_ms=CASE WHEN COALESCE($started,started_utc) IS NOT NULL AND $completed IS NOT NULL
                                    THEN MAX(0,(julianday($completed)-julianday(COALESCE($started,started_utc)))*86400000) ELSE NULL END
                            WHERE id=$id
                            """, ("$status", item.Job.Status), ("$completed", Utc(item.Job.CompletedUtc)),
                            ("$started", Utc(item.Job.StartedUtc)), ("$exit", item.Job.ReportedExitCode),
                            ("$error", AuditJob.Bound(item.Job.Error)), ("$summary", AuditJob.Bound(item.Job.Summary)),
                            ("$confidence", item.Job.Confidence), ("$id", existingId));
                        foreach (var artifact in item.Job.Artifacts) SaveArtifact(connection, transaction, existingId, artifact);
                    }
                    else
                    {
                        SaveJob(connection, transaction, item.Job);
                        // A changed log may replace a prior status enrichment. Reapply its status on this scan.
                        Execute(connection, transaction, "DELETE FROM import_sources WHERE job_id=$id AND path<>$path",
                            ("$id", existingId), ("$path", source.Path));
                    }
                }
                else
                {
                    // Status alone is not history; retry it when its log becomes readable.
                    if (item.Enrichment) { skipped++; continue; }
                    SaveJob(connection, transaction, item.Job);
                }
                Execute(connection, transaction, """
                    INSERT INTO import_sources VALUES($path,$size,$utc,$hash,$job)
                    ON CONFLICT(path) DO UPDATE SET size=$size,last_write_utc=$utc,sha256=$hash,job_id=$job
                    """, ("$path", source.Path), ("$size", source.Size), ("$utc", Utc(source.LastWriteUtc)),
                    ("$hash", source.Sha256), ("$job", item.Job.Id));
                imported++;
            }
            transaction.Commit();
            return (imported, skipped);
        }
    }

    private static void SaveJob(SqliteConnection connection, SqliteTransaction transaction, AuditJob job)
    {
        Execute(connection, transaction, """
            INSERT INTO jobs VALUES($id,$profile,$snapshot,$operation,$status,$source,$destination,$started,$completed,
                $elapsed,$process,$robocopy,$reported,$summary,$error,$counters,$provenance,$confidence)
            ON CONFLICT(id) DO UPDATE SET status=$status,completed_utc=$completed,elapsed_ms=$elapsed,
                process_exit_code=$process,robocopy_exit_code=$robocopy,reported_exit_code=$reported,summary=$summary,error=$error,
                counters_json=$counters,parser_confidence=$confidence
            """, ("$id", job.Id), ("$profile", job.ProfileId?.ToString()), ("$snapshot", job.ProfileSnapshot),
            ("$operation", job.Operation), ("$status", job.Status), ("$source", job.SourcePath),
            ("$destination", job.DestinationPath), ("$started", Utc(job.StartedUtc)), ("$completed", Utc(job.CompletedUtc)),
            ("$elapsed", job.StartedUtc.HasValue && job.CompletedUtc.HasValue ? Math.Max(0, (job.CompletedUtc.Value - job.StartedUtc.Value).TotalMilliseconds) : null),
            ("$process", job.ProcessExitCode), ("$robocopy", job.RobocopyExitCode), ("$reported", job.ReportedExitCode), ("$summary", AuditJob.Bound(job.Summary)),
            ("$error", AuditJob.Bound(job.Error)), ("$counters", job.Counters), ("$provenance", job.Provenance), ("$confidence", job.Confidence));
        Execute(connection, transaction,
            "INSERT INTO job_events(job_id,timestamp_utc,code,message) VALUES($id,$utc,$code,$message)",
            ("$id", job.Id), ("$utc", Utc(DateTimeOffset.UtcNow)), ("$code", "job." + job.Status), ("$message", AuditJob.Bound(job.Summary)));
        foreach (var artifact in job.Artifacts) SaveArtifact(connection, transaction, job.Id, artifact);
    }

    private static void SaveArtifact(SqliteConnection connection, SqliteTransaction transaction, string jobId, AuditArtifact artifact) =>
        Execute(connection, transaction, """
            INSERT INTO job_artifacts VALUES($id,$job,$kind,$path,$size,$write,$hash,$seen)
            ON CONFLICT(job_id,kind,path) DO UPDATE SET size=COALESCE($size,size),
                last_write_utc=COALESCE($write,last_write_utc),
                sha256=CASE WHEN $hash IS NOT NULL THEN $hash
                    WHEN ($size IS NOT NULL AND $size IS NOT size) OR ($write IS NOT NULL AND $write IS NOT last_write_utc)
                    THEN NULL ELSE sha256 END,
                last_seen_utc=COALESCE($seen,last_seen_utc)
            """, ("$id", Guid.NewGuid().ToString("N")), ("$job", jobId), ("$kind", artifact.Kind),
            ("$path", Path.GetFullPath(artifact.Path)), ("$size", artifact.Size),
            ("$write", Utc(artifact.LastWriteUtc)), ("$hash", artifact.Sha256),
            ("$seen", artifact.Size.HasValue || artifact.LastWriteUtc.HasValue ? Utc(DateTimeOffset.UtcNow) : null));

    private static void WriteEvent(SqliteConnection connection, SqliteTransaction transaction, AuditSystemEvent entry) =>
        Execute(connection, transaction, """
            INSERT INTO system_events(timestamp_utc,severity,category,code,message,details,job_id,profile_id,artifact_id,import_batch_id)
            VALUES($utc,$severity,$category,$code,$message,$details,$job,$profile,$artifact,$batch)
            """, ("$utc", Utc(DateTimeOffset.UtcNow)), ("$severity", entry.Severity), ("$category", entry.Category),
            ("$code", entry.Code), ("$message", AuditJob.Bound(entry.Message)), ("$details", AuditJob.Bound(entry.Details)),
            ("$job", entry.JobId), ("$profile", entry.ProfileId?.ToString()), ("$artifact", entry.ArtifactId), ("$batch", entry.ImportBatchId));

    private static string? Utc(DateTimeOffset? value) => value?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    private static SqliteCommand Command(SqliteConnection connection, SqliteTransaction? transaction, string sql, params (string, object?)[] values)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in values) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command;
    }
    private static void Execute(SqliteConnection connection, SqliteTransaction? transaction, string sql, params (string, object?)[] values)
    { using var command = Command(connection, transaction, sql, values); command.ExecuteNonQuery(); }
    private static object? Scalar(SqliteConnection connection, SqliteTransaction? transaction, string sql, params (string, object?)[] values)
    { using var command = Command(connection, transaction, sql, values); return command.ExecuteScalar(); }
}
