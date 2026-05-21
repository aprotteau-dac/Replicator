namespace Replicator.Core.Scripting;

public sealed record BackupRunStatus(
    string ProfileName,
    string Mode,
    string Source,
    string Destination,
    string LogPath,
    DateTimeOffset? StartedAt,
    DateTimeOffset? UpdatedAt,
    int? ExitCode,
    bool Succeeded,
    string Message)
{
    public string ToDisplayString()
    {
        var exit = ExitCode.HasValue ? $"exit {ExitCode.Value}" : "exit unavailable";
        var result = Succeeded ? "succeeded" : "failed";
        var updated = UpdatedAt.HasValue ? $" Updated {UpdatedAt.Value.LocalDateTime:g}." : string.Empty;

        return $"{Mode} {result} ({exit}). {Message}{updated}".Trim();
    }
}
