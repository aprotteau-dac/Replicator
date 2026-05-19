namespace Replicator.Core.Scripting;

public sealed record BackupRunSummary(
    string LogPath,
    DateTime LastWriteTime,
    string Mode,
    int? TotalDirectories,
    int? CopiedDirectories,
    int? FailedDirectories,
    int? TotalFiles,
    int? CopiedFiles,
    int? FailedFiles,
    string BytesLine,
    string Tail)
{
    public string ToDisplayString()
    {
        var mode = string.IsNullOrWhiteSpace(Mode) ? "Unknown mode" : Mode;
        var files = TotalFiles.HasValue
            ? $"Files total {TotalFiles}, copied/listed {CopiedFiles ?? 0}, failed {FailedFiles ?? 0}"
            : "File summary unavailable";
        var directories = TotalDirectories.HasValue
            ? $"Directories total {TotalDirectories}, copied/listed {CopiedDirectories ?? 0}, failed {FailedDirectories ?? 0}"
            : "Directory summary unavailable";

        return $"{mode}. {files}. {directories}.";
    }
}
