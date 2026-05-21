using Replicator.Core.Models;

namespace Replicator.Core.Scheduling;

public interface IScheduledTaskInventoryService
{
    Task<ScheduledTaskInventoryResult> ScanAsync(
        IReadOnlyList<BackupProfile> profiles,
        IReadOnlyDictionary<Guid, string> expectedScriptPaths,
        CancellationToken cancellationToken = default);
}
