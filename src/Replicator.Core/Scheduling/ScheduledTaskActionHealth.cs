namespace Replicator.Core.Scheduling;

public sealed record ScheduledTaskActionHealth(
    bool NeedsRepair,
    IReadOnlyList<string> RepairReasons,
    string ScriptPath,
    bool ScriptExists);
