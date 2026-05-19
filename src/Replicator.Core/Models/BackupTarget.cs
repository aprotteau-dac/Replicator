namespace Replicator.Core.Models;

public sealed class BackupTarget
{
    public BackupTargetKind Kind { get; set; } = BackupTargetKind.LocalPath;

    public string Path { get; set; } = string.Empty;

    public string Endpoint { get; set; } = string.Empty;

    public string Bucket { get; set; } = string.Empty;

    public string Prefix { get; set; } = string.Empty;

    public string CredentialName { get; set; } = string.Empty;
}
