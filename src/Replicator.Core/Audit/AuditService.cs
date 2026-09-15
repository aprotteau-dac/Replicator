using System.Text.Json;
using Replicator.Core.Models;
using Replicator.Core.Scripting;
using Replicator.Core.Shuttle;

namespace Replicator.Core.Audit;

// This is the failure boundary used by the shell. SQLite work is synchronous internally and always off the UI thread.
public sealed class AuditService
{
    private readonly SqliteAuditStore _store;
    private readonly FallbackDiagnosticSink _fallback;
    private readonly string _logsDirectory;
    private readonly object _importGate = new();
    private Task _importTask = Task.CompletedTask;
    private int _failed;

    public AuditService(ReplicatorPaths paths)
    {
        _store = new(paths.DatabaseFile);
        _fallback = new(paths.FallbackLogFile);
        _logsDirectory = paths.LogsDirectory;
    }

    public Task SaveJobAsync(AuditJob job) => SafeAsync(() => _store.SaveJob(job), "storage.job_failed", job.Id);
    public Task InitializeAsync() => SafeAsync(() =>
    {
        _store.Initialize();
        _store.WriteEvent(new("storage.initialized", "Audit storage initialized.", "storage"));
    }, "storage.initialization_failed");
    public Task EventAsync(AuditSystemEvent entry) => SafeAsync(() => _store.WriteEvent(entry), "storage.event_failed", entry.JobId, entry);
    public Task DetachProfileAsync(Guid id) => SafeAsync(() => _store.DetachProfile(id), "storage.detach_failed");

    public Task ImportAsync(IReadOnlyList<BackupProfile> profiles)
    {
        // Coalesce refresh requests; never queue competing full directory scans.
        lock (_importGate)
        {
            if (!_importTask.IsCompleted) return _importTask;
            _importTask = SafeAsync(() => new AuditImporter(_store, _logsDirectory, RecordEvent).Import(profiles), "import.failed");
            return _importTask;
        }
    }

    public Task CompleteBackupAsync(AuditJob job) => SafeAsync(() =>
    {
        var log = job.Artifacts.FirstOrDefault(a => a.Kind == "log");
        if (log is not null && File.Exists(log.Path))
        {
            try
            {
                using var stream = File.OpenRead(log.Path);
                var parsed = AuditLogParser.Read(stream, log.Path, []);
                job.Counters = parsed.Counters;
                job.RobocopyExitCode = parsed.RobocopyExitCode;
                if (string.IsNullOrEmpty(job.Error)) job.Error = parsed.Error;
                var file = new FileInfo(log.Path);
                job.Artifacts.Remove(log);
                job.Artifacts.Add(log with { Size = file.Length, LastWriteUtc = file.LastWriteTimeUtc });
            }
            catch (Exception exception) { RecordEvent(new("audit.log_parse_failed", exception.Message, "audit", "warning", JobId: job.Id)); }
        }
        if (log is not null)
        {
            try
            {
                var slug = AuditLogParser.Slug(log.Path);
                var statusPath = Path.Combine(_logsDirectory, slug + "-latest.json");
                if (File.Exists(statusPath) && new FileInfo(statusPath).Length <= 1024 * 1024)
                {
                    using var stream = File.OpenRead(statusPath);
                    var status = JsonSerializer.Deserialize<BackupRunStatus>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (status is not null && string.Equals(status.LogPath, log.Path, StringComparison.OrdinalIgnoreCase))
                    {
                        job.ReportedExitCode = status.ExitCode;
                        job.Summary = status.Message;
                        var file = new FileInfo(statusPath);
                        job.Artifacts.Add(new("status", statusPath, file.Length, file.LastWriteTimeUtc));
                    }
                }
            }
            catch (Exception exception) { RecordEvent(new("audit.status_parse_failed", exception.Message, "audit", "warning", JobId: job.Id)); }
        }
        _store.SaveJob(job);
    }, "storage.job_failed", job.Id);

    public Task CompleteShuttleAsync(AuditJob job, ShuttleOperationResult result) => SafeAsync(() =>
    {
        job.Status = result.Succeeded ? "succeeded" : "failed";
        job.CompletedUtc = DateTimeOffset.UtcNow;
        job.Summary = AuditJob.Bound(result.Message + Environment.NewLine + result.Details);
        job.Error = result.Succeeded ? "" : AuditJob.Bound(result.Message);
        var manifest = result.Manifest;
        if (manifest is not null)
        {
            job.Counters = JsonSerializer.Serialize(new { manifest.TotalFiles, manifest.CopiedFiles, manifest.SkippedFiles,
                manifest.NewFiles, manifest.ChangedFiles, manifest.ConflictFiles,
                WarningCount = manifest.Warnings.Count, Warnings = manifest.Warnings.Take(20).Select(w => AuditJob.Bound(w, 512)) });
            try
            {
                var directory = Path.Combine(Environment.ExpandEnvironmentVariables(job.DestinationPath), "manifests");
                if (Directory.Exists(directory))
                {
                    var path = Directory.EnumerateFiles(directory, $"*-{manifest.ManifestId:N}.json").FirstOrDefault();
                    if (path is not null)
                    {
                        var file = new FileInfo(path);
                        job.Artifacts.Add(new("manifest", file.FullName, file.Length, file.LastWriteTimeUtc));
                    }
                }
            }
            catch (Exception exception) { RecordEvent(new("audit.manifest_unavailable", exception.Message, "audit", "warning", JobId: job.Id)); }
        }
        _store.SaveJob(job);
    }, "storage.job_failed", job.Id);

    private void RecordEvent(AuditSystemEvent entry)
    {
        try { _store.WriteEvent(entry); }
        catch (Exception exception) { Failure("storage.event_failed", exception, entry.JobId, entry); }
    }

    private Task SafeAsync(Action action, string code, string? jobId = null, AuditSystemEvent? original = null) => Task.Run(() =>
    {
        try
        {
            action();
            if (Interlocked.Exchange(ref _failed, 0) != 0)
                _store.WriteEvent(new("storage.recovered", "Audit storage is available again.", "storage"));
        }
        catch (Exception exception) { Failure(code, exception, jobId, original); }
    });

    private void Failure(string code, Exception exception, string? jobId, AuditSystemEvent? original = null)
    {
        if (Interlocked.Exchange(ref _failed, 1) == 0)
            _fallback.Write(new("fallback.activated", "Audit storage unavailable; operations continue using file logs.", "storage", "warning"));
        _fallback.Write(new(code, exception.Message, "storage", "error", exception.ToString(), JobId: jobId));
        if (original is not null) _fallback.Write(original);
    }
}
