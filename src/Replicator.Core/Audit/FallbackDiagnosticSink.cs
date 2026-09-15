using System.Text;
using System.Text.Json;

namespace Replicator.Core.Audit;

public sealed class FallbackDiagnosticSink(string path)
{
    public const int MaximumBytes = 1024 * 1024;
    private static readonly object Gate = new();

    // Diagnostics must never become another failure in the operation being diagnosed.
    public void Write(AuditSystemEvent entry)
    {
        try
        {
            var line = JsonSerializer.Serialize(new
            {
                TimestampUtc = DateTimeOffset.UtcNow,
                entry.Code, entry.Category, entry.Severity,
                Message = AuditJob.Bound(entry.Message), Details = AuditJob.Bound(entry.Details),
                entry.JobId, entry.ProfileId, entry.ArtifactId, entry.ImportBatchId
            }) + Environment.NewLine;
            lock (Gate)
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
                if (File.Exists(path) && new FileInfo(path).Length + Encoding.UTF8.GetByteCount(line) > MaximumBytes)
                {
                    File.Move(path, System.IO.Path.ChangeExtension(path, ".1.log"), overwrite: true);
                }
                File.AppendAllText(path, line, new UTF8Encoding(false));
            }
        }
        catch (Exception) { }
    }
}
