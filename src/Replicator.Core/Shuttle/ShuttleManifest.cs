namespace Replicator.Core.Shuttle;

public sealed class ShuttleManifest
{
    public int SchemaVersion { get; set; } = 2;

    public Guid ManifestId { get; set; } = Guid.NewGuid();

    public Guid ProfileId { get; set; }

    public string ProfileName { get; set; } = string.Empty;

    public ShuttleOperationKind Operation { get; set; }

    public bool ReadyToDock { get; set; }

    public string FromMachineId { get; set; } = string.Empty;

    public string FromMachineName { get; set; } = string.Empty;

    public string ToMachineId { get; set; } = string.Empty;

    public string ToMachineName { get; set; } = string.Empty;

    public string SourcePath { get; set; } = string.Empty;

    public string ShuttlePath { get; set; } = string.Empty;

    public string PayloadPath { get; set; } = string.Empty;

    public string DriveRoot { get; set; } = string.Empty;

    public string DriveLabel { get; set; } = string.Empty;

    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset CompletedAt { get; set; } = DateTimeOffset.UtcNow;

    public int TotalFiles { get; set; }

    public int CopiedFiles { get; set; }

    public int SkippedFiles { get; set; }

    public int NewFiles { get; set; }

    public int ChangedFiles { get; set; }

    public int ConflictFiles { get; set; }

    public List<ShuttleManifestEntry> Entries { get; set; } = [];

    public List<string> Warnings { get; set; } = [];
}

public sealed class ShuttleManifestEntry
{
    public string RelativePath { get; set; } = string.Empty;

    public long Length { get; set; }

    public DateTime LastWriteTimeUtc { get; set; }

    public string Sha256 { get; set; } = string.Empty;
}
