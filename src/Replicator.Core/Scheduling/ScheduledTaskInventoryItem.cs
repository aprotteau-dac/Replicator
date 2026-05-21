namespace Replicator.Core.Scheduling;

public sealed record ScheduledTaskInventoryItem(
    string TaskName,
    Guid? ProfileId,
    string ProfileName,
    ScheduledTaskInventoryState InventoryState,
    ScheduledTaskState TaskState,
    string NextRunTime,
    string LastRunTime,
    int LastResult,
    string TaskToRun,
    string ScriptPath,
    bool ScriptExists,
    IReadOnlyList<string> RepairReasons,
    string Reason,
    string RawOutput)
{
    public bool CanRepair => ProfileId.HasValue
        && InventoryState == ScheduledTaskInventoryState.NeedsRepair
        && TaskState != ScheduledTaskState.Running;
}
