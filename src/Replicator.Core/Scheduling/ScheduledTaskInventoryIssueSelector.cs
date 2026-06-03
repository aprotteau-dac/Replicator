namespace Replicator.Core.Scheduling;

public static class ScheduledTaskInventoryIssueSelector
{
    public static ScheduledTaskInventoryItem? ForProfile(ScheduledTaskInventoryResult? inventory, Guid profileId)
    {
        return inventory?.Items.FirstOrDefault(item =>
            item.ProfileId == profileId
            && item.InventoryState is ScheduledTaskInventoryState.NeedsRepair or ScheduledTaskInventoryState.Unknown);
    }
}
