namespace Replicator.Core.Scheduling;

public sealed record ScheduledTaskInventoryResult(
    IReadOnlyList<ScheduledTaskInventoryItem> Items,
    ScheduledTaskInventorySummary Summary,
    string RawOutput);
