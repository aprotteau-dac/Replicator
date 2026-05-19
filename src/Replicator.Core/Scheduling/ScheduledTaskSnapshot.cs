namespace Replicator.Core.Scheduling;

public sealed record ScheduledTaskSnapshot(
    string TaskName,
    ScheduledTaskState State,
    string NextRunTime,
    string LastRunTime,
    int LastResult,
    string RawOutput);
