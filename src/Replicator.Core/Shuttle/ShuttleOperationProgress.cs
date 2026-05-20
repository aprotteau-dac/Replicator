namespace Replicator.Core.Shuttle;

public sealed record ShuttleOperationProgress(
    ShuttleOperationKind Operation,
    int ProcessedFiles,
    int TotalFiles,
    string Message)
{
    public double PercentComplete => TotalFiles <= 0
        ? 0
        : Math.Clamp((double)ProcessedFiles / TotalFiles * 100, 0, 100);
}
