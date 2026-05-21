namespace Replicator.Core.Scheduling;

public sealed record ScheduledTaskSnapshot(
    string TaskName,
    ScheduledTaskState State,
    string NextRunTime,
    string LastRunTime,
    int LastResult,
    string RawOutput)
{
    public string TaskToRun { get; init; } = string.Empty;

    public string ScriptPath { get; init; } = string.Empty;

    public bool ScriptExists { get; init; }

    public bool NeedsRepair { get; init; }

    public IReadOnlyList<string> RepairReasons { get; init; } = [];
}
