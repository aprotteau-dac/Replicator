using System.Security.Cryptography;
using System.Text.Json;
using Replicator.Core.Models;
using Replicator.Core.Scripting;

namespace Replicator.Core.Audit;

public sealed class AuditImporter(SqliteAuditStore store, string logsDirectory, Action<AuditSystemEvent> logEvent)
{
    public void Import(IReadOnlyList<BackupProfile> profiles)
    {
        store.Initialize();
        store.ReconcileProfiles(profiles.Select(p => p.Id).ToArray());
        if (!Directory.Exists(logsDirectory)) return;
        // Enumerate lazily and commit small batches. No transaction is held while files are read/hashed.
        ImportFiles(Directory.EnumerateFiles(logsDirectory, "*.log").Where(p => AuditLogParser.Slug(p) is not null), profiles, false);
        ImportFiles(Directory.EnumerateFiles(logsDirectory, "*-latest.json"), profiles, true);
    }

    private void ImportFiles(IEnumerable<string> paths, IReadOnlyList<BackupProfile> profiles, bool enrichment)
    {
        foreach (var batch in paths.Chunk(32))
        {
            var batchId = Guid.NewGuid().ToString("N");
            logEvent(new("import.batch_started", $"Inspecting {batch.Length} artifacts.", "import", ImportBatchId: batchId));
            var items = new List<ImportedJob>();
            var failures = 0;
            foreach (var path in batch)
            {
                try { items.Add(Read(path, profiles, enrichment)); }
                catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException)
                {
                    failures++;
                    logEvent(new("import.artifact_skipped", "Artifact could not be imported; it will be retried.", "import", "warning",
                        $"{path}: {exception.Message}", ArtifactId: Path.GetFullPath(path), ImportBatchId: batchId));
                }
            }
            // Deliberately let database errors stop the import. The transaction rolls back every cursor in this batch.
            (int Imported, int Skipped) result;
            try { result = store.ImportBatch(items); }
            catch (Exception exception)
            {
                logEvent(new("import.batch_failed", "Batch rolled back; remaining artifacts will be retried.", "import", "error",
                    exception.Message, ImportBatchId: batchId));
                throw;
            }
            logEvent(new("import.batch_completed", $"Imported {result.Imported}; duplicate/deferred {result.Skipped}; unreadable {failures}.",
                "import", ImportBatchId: batchId));
        }
    }

    private ImportedJob Read(string path, IReadOnlyList<BackupProfile> profiles, bool enrichment)
    {
        var fullPath = Path.GetFullPath(path);
        using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var size = stream.Length;
        if (enrichment && size > 1024 * 1024) throw new InvalidDataException("Status file exceeds 1 MB.");
        var write = new DateTimeOffset(File.GetLastWriteTimeUtc(fullPath));
        var hash = Convert.ToHexString(SHA256.HashData(stream));
        stream.Position = 0;
        AuditJob job;
        if (enrichment)
        {
            var status = JsonSerializer.Deserialize<BackupRunStatus>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidDataException("Empty status JSON.");
            if (string.IsNullOrWhiteSpace(status.LogPath) || !Path.IsPathFullyQualified(status.LogPath))
                throw new InvalidDataException("Status has no absolute log path.");
            var logPath = Path.GetFullPath(status.LogPath);
            var slug = Path.GetFileName(fullPath)[..^"-latest.json".Length];
            if (!string.Equals(AuditLogParser.Slug(logPath), slug, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Path.GetDirectoryName(logPath), Path.GetFullPath(logsDirectory).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Status does not reference a matching log in the logs directory.");
            // A started status is neither failure nor completion. Leave it eligible for a later refresh.
            if (!status.ExitCode.HasValue || !status.UpdatedAt.HasValue)
                throw new InvalidDataException("Status has no terminal outcome yet.");
            job = new AuditJob
            {
                Status = status.Succeeded ? "succeeded" : "failed", StartedUtc = status.StartedAt?.ToUniversalTime(),
                CompletedUtc = status.UpdatedAt.Value.ToUniversalTime(), ReportedExitCode = status.ExitCode,
                Summary = status.Message, Error = status.Succeeded ? "" : status.Message, Confidence = "status_json",
                Artifacts = [new("log", logPath), new("status", fullPath, size, write, hash)]
            };
        }
        else
        {
            job = AuditLogParser.Read(stream, fullPath, profiles);
            job.Artifacts = [new("log", fullPath, size, write, hash)];
        }
        if (stream.Length != size || File.GetLastWriteTimeUtc(fullPath) != write.UtcDateTime)
            throw new IOException("Artifact changed during import.");
        return new(job, new(fullPath, size, write, hash), enrichment);
    }
}
