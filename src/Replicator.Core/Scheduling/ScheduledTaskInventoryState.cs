namespace Replicator.Core.Scheduling;

public enum ScheduledTaskInventoryState
{
    Ready,
    NeedsRepair,
    Orphaned,
    Running,
    Unknown
}
