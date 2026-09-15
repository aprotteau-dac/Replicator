using System.Text.Json;
using Replicator.Core.Models;

namespace Replicator.Core.Audit;

public sealed class AuditJob
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public Guid? ProfileId { get; set; }
    public string ProfileSnapshot { get; set; } = "{}";
    public string Operation { get; set; } = "backup";
    public string Status { get; set; } = "running";
    public string SourcePath { get; set; } = "";
    public string DestinationPath { get; set; } = "";
    public DateTimeOffset? StartedUtc { get; set; }
    public DateTimeOffset? CompletedUtc { get; set; }
    public int? ProcessExitCode { get; set; }
    public int? RobocopyExitCode { get; set; }
    public int? ReportedExitCode { get; set; }
    public string Summary { get; set; } = "";
    public string Error { get; set; } = "";
    public string Counters { get; set; } = "{}";
    public string Provenance { get; set; } = "app";
    public string Confidence { get; set; } = "direct";
    public List<AuditArtifact> Artifacts { get; set; } = [];

    public static AuditJob Start(BackupProfile profile, string operation) => new()
    {
        ProfileId = profile.Id,
        ProfileSnapshot = JsonSerializer.Serialize(profile),
        Operation = operation,
        SourcePath = profile.SourcePath,
        DestinationPath = profile.Target.Path,
        StartedUtc = DateTimeOffset.UtcNow
    };

    internal static string Bound(string? text, int length = 4096) =>
        string.IsNullOrEmpty(text) ? "" : text[..Math.Min(text.Length, length)];
}

public sealed record AuditArtifact(string Kind, string Path, long? Size = null,
    DateTimeOffset? LastWriteUtc = null, string? Sha256 = null);

public sealed record AuditSystemEvent(string Code, string Message, string Category = "application",
    string Severity = "info", string? Details = null, string? JobId = null,
    Guid? ProfileId = null, string? ArtifactId = null, string? ImportBatchId = null);

public sealed record ImportSource(string Path, long Size, DateTimeOffset LastWriteUtc, string Sha256);
public sealed record ImportedJob(AuditJob Job, ImportSource Source, bool Enrichment = false);
