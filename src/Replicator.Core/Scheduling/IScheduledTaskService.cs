using Replicator.Core.Models;

namespace Replicator.Core.Scheduling;

public interface IScheduledTaskService
{
    Task<TaskOperationResult> InstallOrUpdateAsync(
        BackupProfile profile,
        string scriptPath,
        CancellationToken cancellationToken = default);

    Task<TaskOperationResult> RunAsync(BackupProfile profile, CancellationToken cancellationToken = default);

    Task<TaskOperationResult> EnableAsync(BackupProfile profile, CancellationToken cancellationToken = default);

    Task<TaskOperationResult> DisableAsync(BackupProfile profile, CancellationToken cancellationToken = default);

    Task<TaskOperationResult> DeleteAsync(BackupProfile profile, CancellationToken cancellationToken = default);

    Task<ScheduledTaskSnapshot> QueryAsync(BackupProfile profile, CancellationToken cancellationToken = default);
}
