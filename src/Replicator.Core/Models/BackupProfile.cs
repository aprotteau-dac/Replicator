namespace Replicator.Core.Models;

public sealed class BackupProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "New backup profile";

    public string SourcePath { get; set; } = string.Empty;

    public ProfileMode Mode { get; set; } = ProfileMode.Backup;

    public BackupTarget Target { get; set; } = new();

    public BackupEngineKind Engine { get; set; } = BackupEngineKind.NativePowerShell;

    public List<string> ExcludePatterns { get; set; } = [];

    public BackupSchedule Schedule { get; set; } = new();

    public bool DryRun { get; set; } = true;

    public bool MirrorDeletes { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
