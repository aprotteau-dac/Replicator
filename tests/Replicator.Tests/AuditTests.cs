using System.Text.Json;
using Microsoft.Data.Sqlite;
using Replicator.Core;
using Replicator.Core.Audit;
using Replicator.Core.Execution;
using Replicator.Core.Models;
using Replicator.Core.Scripting;
using Replicator.Core.Shuttle;

internal static class AuditTests
{
    public static IEnumerable<(string Name, Func<Task> Test)> Cases =>
    [
        ("audit schema is idempotent and retains completed jobs", SchemaAndJobs),
        ("audit rejects future schemas without modifying them", FutureSchema),
        ("audit imports logs and enriches exact latest job idempotently", ImportAndEnrichment),
        ("audit preserves uncertainty and orphaned historical snapshots", UncertainAndOrphaned),
        ("audit skips malformed locked and unrelated artifacts independently", BadArtifacts),
        ("audit import rollback leaves artifacts eligible for retry", ImportRollback),
        ("audit import does not duplicate or override app jobs", AppImportDeduplication),
        ("audit events preserve all correlation fields", EventCorrelation),
        ("audit fallback rotates within its size bound and recovers", FallbackAndRecovery),
        ("audit shuttle stores bounded counters and linked manifests", ShuttleCapture),
        ("audit imports multiple batches without storing verbose log bodies", MultipleBatches),
        ("audit generated PowerShell preserves exact app log and status evidence", GeneratedScriptCapture),
        ("audit database lock falls back and recovers", DatabaseLock)
    ];

    internal static ReplicatorPaths Paths()
    {
        var paths = new ReplicatorPaths(Path.Combine(Environment.CurrentDirectory, "test-artifacts", Guid.NewGuid().ToString("N")));
        paths.EnsureCreated();
        return paths;
    }

    internal static object? Scalar(ReplicatorPaths paths, string sql)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = paths.DatabaseFile, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    private static BackupProfile Profile() => new() { Name = "Historical name", SourcePath = @"C:\source", Target = new() { Path = @"D:\target" } };

    private static string Log(ReplicatorPaths paths, BackupProfile profile, string suffix = "20260610-120000", string? footer = "2026-06-10 12:01:00Z Replication completed with robocopy exit code 1. Log: example")
    {
        var path = Path.Combine(paths.LogsDirectory, $"{PowerShellScriptGenerator.ProfileSlug(profile)}-{suffix}.log");
        File.WriteAllText(path, $"""
            Replicator backup run
            Profile: {profile.Name}
            Mode: Copy - files may be copied
            Source: {profile.SourcePath}
            Destination: {profile.Target.Path}
            Started: 2026-06-10 12:00:00Z
            Mirror deletes: False
            Excludes: node_modules
              Dirs : 2 1 1 0 0 0
             Files : 3 2 1 0 0 0
            {footer}
            """);
        return path;
    }

    private static void Import(ReplicatorPaths paths, params BackupProfile[] profiles)
    {
        var store = new SqliteAuditStore(paths.DatabaseFile);
        new AuditImporter(store, paths.LogsDirectory, store.WriteEvent).Import(profiles);
    }

    private static Task SchemaAndJobs()
    {
        var paths = Paths();
        var store = new SqliteAuditStore(paths.DatabaseFile);
        store.Initialize(); store.Initialize();
        var profile = Profile();
        var job = AuditJob.Start(profile, "backup");
        store.SaveJob(job);
        profile.Name = "Edited";
        job.Status = "succeeded"; job.CompletedUtc = DateTimeOffset.UtcNow; job.ProcessExitCode = 0;
        job.Error = new string('x', 8000);
        store.SaveJob(job);
        Check(Convert.ToInt64(Scalar(paths, "SELECT COUNT(*) FROM schema_metadata")) == 1, "Schema migrated twice.");
        Check(Convert.ToInt64(Scalar(paths, "SELECT COUNT(*) FROM jobs")) == 1, "Completion duplicated job.");
        Check((string)Scalar(paths, "SELECT status FROM jobs")! == "succeeded", "Job did not complete.");
        Check(((string)Scalar(paths, "SELECT profile_snapshot FROM jobs")!).Contains("Historical name"), "Snapshot changed with profile.");
        Check(Convert.ToInt64(Scalar(paths, "SELECT length(error) FROM jobs")) == 4096, "Error was not bounded.");
        store.DetachProfile(profile.Id);
        Check(Scalar(paths, "SELECT profile_id FROM jobs") is DBNull, "Profile deletion did not orphan history.");
        Check(Convert.ToInt64(Scalar(paths, "SELECT COUNT(*) FROM job_events")) == 2, "Lifecycle events missing.");
        return Task.CompletedTask;
    }

    private static Task FutureSchema()
    {
        var paths = Paths();
        Scalar(paths, "PRAGMA user_version=99");
        try { new SqliteAuditStore(paths.DatabaseFile).Initialize(); throw new Exception("Accepted future schema."); }
        catch (InvalidOperationException) { }
        Check(Convert.ToInt64(Scalar(paths, "PRAGMA user_version")) == 99, "Rewrote future schema.");
        return Task.CompletedTask;
    }

    private static Task ImportAndEnrichment()
    {
        var paths = Paths(); var profile = Profile();
        var oldLog = Log(paths, profile);
        var latestLog = Log(paths, profile, "20260611-120000", null);
        var statusPath = Path.Combine(paths.LogsDirectory, PowerShellScriptGenerator.ProfileSlug(profile) + "-latest.json");
        File.WriteAllText(statusPath, JsonSerializer.Serialize(new BackupRunStatus(profile.Name, "Copy", profile.SourcePath,
            profile.Target.Path, latestLog, DateTimeOffset.Parse("2026-06-11T12:00:00Z"), DateTimeOffset.Parse("2026-06-11T12:02:00Z"), 0, true, "Finished latest")));
        Import(paths, profile); Import(paths, profile);
        Check(Convert.ToInt64(Scalar(paths, "SELECT COUNT(*) FROM jobs")) == 2, "Repeated import created duplicate jobs.");
        Check(Convert.ToInt64(Scalar(paths, "SELECT COUNT(*) FROM import_sources")) == 3, "Source fingerprints missing.");
        Check(Convert.ToInt64(Scalar(paths, "SELECT COUNT(*) FROM jobs WHERE summary='Finished latest'")) == 1, "Status enriched the wrong jobs.");
        Check(((string)Scalar(paths, "SELECT counters_json FROM jobs WHERE summary='Finished latest'")!).Contains("FilesCopied\":2"), "Status erased counters.");
        File.AppendAllText(latestLog, "\nAdditional evidence\n");
        Import(paths, profile);
        Check(Convert.ToInt64(Scalar(paths, "SELECT COUNT(*) FROM jobs WHERE summary='Finished latest' AND status='succeeded'")) == 1,
            "Changed log lost status enrichment.");
        File.Delete(oldLog); File.Delete(latestLog);
        Import(paths, profile);
        Check(Convert.ToInt64(Scalar(paths, "SELECT COUNT(*) FROM jobs")) == 2, "Deleting artifacts erased history.");
        Check(Convert.ToInt64(Scalar(paths, "SELECT COUNT(*) FROM job_artifacts WHERE size IS NOT NULL AND sha256 IS NOT NULL")) >= 2, "Artifact metadata missing.");
        return Task.CompletedTask;
    }

    private static Task UncertainAndOrphaned()
    {
        var paths = Paths(); var profile = Profile();
        Log(paths, profile, "20260610-120000", null);
        Log(paths, profile, "20260611-120000", "Ended : Wednesday, June 11, 2026");
        Import(paths);
        Check(Convert.ToInt64(Scalar(paths, "SELECT COUNT(*) FROM jobs WHERE profile_id IS NULL")) == 2, "Orphan history was dropped.");
        Check(Convert.ToInt64(Scalar(paths, "SELECT COUNT(*) FROM jobs WHERE status='unknown'")) == 1, "Incomplete log incorrectly succeeded.");
        Check(Convert.ToInt64(Scalar(paths, "SELECT COUNT(*) FROM jobs WHERE status='completed_unverified'")) == 1, "Unverified completion lost uncertainty.");
        var renamed = Profile(); renamed.Id = profile.Id; renamed.Name = "Renamed";
        var next = Log(paths, profile, "20260612-120000");
        Import(paths, renamed);
        Check(Convert.ToInt64(Scalar(paths, "SELECT COUNT(*) FROM jobs WHERE profile_id IS NOT NULL")) == 1, "Renamed profile not matched by stable ID.");
        Check(!((string)Scalar(paths, "SELECT profile_snapshot FROM jobs WHERE profile_id IS NOT NULL")!).Contains("Renamed"), "Historical snapshot replaced with today's name.");
        return Task.CompletedTask;
    }

    private static Task BadArtifacts()
    {
        var paths = Paths(); var profile = Profile();
        Log(paths, profile);
        var locked = Log(paths, profile, "20260611-120000");
        File.WriteAllText(Path.Combine(paths.LogsDirectory, "bad-latest.json"), "{bad json");
        File.WriteAllText(Path.Combine(paths.LogsDirectory, "bad-20260610-120000.log"), "not a backup");
        File.WriteAllText(paths.FallbackLogFile, "diagnostics");
        using (var handle = new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) Import(paths, profile);
        Check(Convert.ToInt64(Scalar(paths, "SELECT COUNT(*) FROM jobs")) == 1, "Bad artifact affected valid import.");
        Check(Convert.ToInt64(Scalar(paths, "SELECT COUNT(*) FROM system_events WHERE code='import.artifact_skipped'")) == 3, "Missing per-artifact diagnostics.");
        Import(paths, profile);
        Check(Convert.ToInt64(Scalar(paths, "SELECT COUNT(*) FROM jobs")) == 2, "Locked artifact was not retried.");
        return Task.CompletedTask;
    }

    private static Task ImportRollback()
    {
        var paths = Paths(); var profile = Profile(); var log = Log(paths, profile);
        var store = new SqliteAuditStore(paths.DatabaseFile); store.Initialize();
        Scalar(paths, "CREATE TRIGGER fail_import BEFORE INSERT ON import_sources BEGIN SELECT RAISE(ABORT, 'test write failure'); END");
        try { Import(paths, profile); throw new Exception("Expected storage failure."); }
        catch (SqliteException) { }
        Check(Convert.ToInt64(Scalar(paths, "SELECT COUNT(*) FROM jobs")) == 0, "Job committed without its import cursor.");
        Check(Convert.ToInt64(Scalar(paths, "SELECT COUNT(*) FROM import_sources")) == 0, "Failed import poisoned dedupe.");
        Scalar(paths, "DROP TRIGGER fail_import"); Import(paths, profile);
        Check(Convert.ToInt64(Scalar(paths, "SELECT COUNT(*) FROM jobs")) == 1, "Rollback was not retryable.");
        return Task.CompletedTask;
    }

    private static Task AppImportDeduplication()
    {
        var paths = Paths(); var profile = Profile(); var log = Log(paths, profile);
        var store = new SqliteAuditStore(paths.DatabaseFile);
        var job = AuditJob.Start(profile, "backup"); job.Artifacts.Add(new("log", log));
        job.Status = "failed"; job.Error = "Direct process failure"; store.SaveJob(job);
        Import(paths, profile); Import(paths, profile);
        Check(Convert.ToInt64(Scalar(paths, "SELECT COUNT(*) FROM jobs")) == 1, "Imported app run was duplicated.");
        Check((string)Scalar(paths, "SELECT status FROM jobs")! == "failed", "Import overrode direct evidence.");
        return Task.CompletedTask;
    }

    private static Task EventCorrelation()
    {
        var paths = Paths(); var id = Guid.NewGuid();
        new SqliteAuditStore(paths.DatabaseFile).WriteEvent(new("test.event", "Test", "test", "warning", "details", "job", id, "artifact", "batch"));
        Check(Convert.ToInt64(Scalar(paths, "SELECT COUNT(*) FROM system_events WHERE code='test.event' AND job_id='job' AND artifact_id='artifact' AND import_batch_id='batch' AND severity='warning'")) == 1, "Correlation was lost.");
        Check((string)Scalar(paths, "SELECT profile_id FROM system_events WHERE code='test.event'")! == id.ToString(), "Profile correlation missing.");
        return Task.CompletedTask;
    }

    private static async Task FallbackAndRecovery()
    {
        var paths = Paths(); Directory.CreateDirectory(paths.DatabaseFile);
        var service = new AuditService(paths);
        await service.EventAsync(new("test.failed_storage", "Still running"));
        Check(File.ReadAllText(paths.FallbackLogFile).Contains("fallback.activated"), "Fallback not activated.");
        Check(File.ReadAllText(paths.FallbackLogFile).Contains("test.failed_storage"), "Original event lost on fallback.");
        var sink = new FallbackDiagnosticSink(paths.FallbackLogFile);
        for (var i = 0; i < 350; i++) sink.Write(new("test.rotation", new string('a', 4096)));
        Check(new FileInfo(paths.FallbackLogFile).Length <= FallbackDiagnosticSink.MaximumBytes, "Current fallback exceeded cap.");
        Check(new FileInfo(Path.ChangeExtension(paths.FallbackLogFile, ".1.log")).Length <= FallbackDiagnosticSink.MaximumBytes, "Previous fallback exceeded cap.");
        Directory.Delete(paths.DatabaseFile);
        await service.EventAsync(new("test.recovered", "Recovered"));
        Check(Convert.ToInt64(Scalar(paths, "SELECT COUNT(*) FROM system_events WHERE code='storage.recovered'")) == 1, "Recovery not recorded.");
    }

    private static async Task ShuttleCapture()
    {
        var paths = Paths(); var profile = Profile(); profile.Target.Path = Path.Combine(paths.RootDirectory, "shuttle");
        var manifest = new ShuttleManifest { TotalFiles = 5, CopiedFiles = 2, ConflictFiles = 1, Warnings = ["Test warning"] };
        var directory = Path.Combine(profile.Target.Path, "manifests"); Directory.CreateDirectory(directory);
        var manifestPath = Path.Combine(directory, $"20260610-test-prepare-{manifest.ManifestId:N}.json");
        File.WriteAllText(manifestPath, "{}");
        var job = AuditJob.Start(profile, "shuttle_prepare");
        var service = new AuditService(paths); await service.SaveJobAsync(job);
        await service.CompleteShuttleAsync(job, new(true, "Completed", manifest, new string('x', 10000)));
        Check(((string)Scalar(paths, "SELECT counters_json FROM jobs")!).Contains("ConflictFiles\":1"), "Shuttle counters missing.");
        Check(Convert.ToInt64(Scalar(paths, "SELECT length(summary) FROM jobs")) == 4096, "Shuttle details unbounded.");
        Check(Convert.ToInt64(Scalar(paths, "SELECT COUNT(*) FROM job_artifacts WHERE kind='manifest'")) == 1, "Manifest link missing.");
    }

    private static Task MultipleBatches()
    {
        var paths = Paths(); var profile = Profile();
        for (var i = 0; i < 70; i++)
            File.AppendAllText(Log(paths, profile, $"20260610-120000-{Guid.NewGuid():N}"), "\nVERBOSE_BODY_MUST_STAY_IN_FILE\n");
        Import(paths, profile); Import(paths, profile);
        Check(Convert.ToInt64(Scalar(paths, "SELECT COUNT(*) FROM jobs")) == 70, "Batched import lost or duplicated jobs.");
        Check(Convert.ToInt64(Scalar(paths, "SELECT COUNT(*) FROM system_events WHERE code='import.batch_completed'")) == 6, "Import did not commit in batches.");
        Check(Convert.ToInt64(Scalar(paths, "SELECT COUNT(*) FROM jobs WHERE summary LIKE '%VERBOSE_BODY%' OR counters_json LIKE '%VERBOSE_BODY%'")) == 0,
            "Verbose body persisted in database.");
        return Task.CompletedTask;
    }

    private static async Task GeneratedScriptCapture()
    {
        var paths = Paths(); var profile = Profile(); profile.SourcePath = paths.RootDirectory;
        profile.Target.Path = Path.Combine(paths.RootDirectory, "..", Guid.NewGuid().ToString("N"));
        profile.DryRun = true;
        var script = await new PowerShellScriptGenerator(paths.ScriptsDirectory, paths.LogsDirectory).WriteAsync(profile);
        var job = AuditJob.Start(profile, "dry_run");
        var log = Path.Combine(paths.LogsDirectory, $"{PowerShellScriptGenerator.ProfileSlug(profile)}-20260610-120000-{job.Id}.log");
        job.Artifacts.Add(new("log", log));
        var service = new AuditService(paths); await service.SaveJobAsync(job);
        var result = await new ProcessRunner().RunAsync("powershell.exe", ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script.Path, "-RunLogPath", log]);
        Check(result.Succeeded, $"Generated script failed: {result.StandardError}");
        Check(File.Exists(log), "Script ignored exact app log path.");
        Check(!Directory.Exists(profile.Target.Path), "Dry run wrote to target.");
        job.Status = "succeeded"; job.CompletedUtc = DateTimeOffset.UtcNow; job.ProcessExitCode = result.ExitCode;
        await service.CompleteBackupAsync(job); await service.ImportAsync([profile]);
        Check(Convert.ToInt64(Scalar(paths, "SELECT COUNT(*) FROM jobs")) == 1, "Real script evidence duplicated app job.");
        Check(Convert.ToInt64(Scalar(paths, "SELECT reported_exit_code FROM jobs")) == 0, "Status result not captured.");
        Check(Scalar(paths, "SELECT robocopy_exit_code FROM jobs") is DBNull, "Preflight result mislabeled as robocopy result.");
        Check(Convert.ToInt64(Scalar(paths, "SELECT COUNT(*) FROM job_artifacts WHERE kind='status'")) == 1, "Status artifact missing.");
    }

    private static async Task DatabaseLock()
    {
        var paths = Paths(); var service = new AuditService(paths); await service.InitializeAsync();
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = paths.DatabaseFile, Pooling = false }.ToString()))
        {
            connection.Open();
            using var transaction = connection.BeginTransaction();
            await service.EventAsync(new("test.locked", "Operation still proceeds"));
            Check(File.ReadAllText(paths.FallbackLogFile).Contains("test.locked"), "Locked database did not fall back.");
        }
        await service.EventAsync(new("test.unlocked", "Recovered"));
        Check(Convert.ToInt64(Scalar(paths, "SELECT COUNT(*) FROM system_events WHERE code='storage.recovered'")) == 1, "Lock recovery missing.");
    }
}
