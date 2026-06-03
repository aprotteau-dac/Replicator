using Replicator.Core.Models;

namespace Replicator.Core.Shuttle;

public interface IShuttleService
{
    Task<ShuttleOperationResult> PrepareAsync(
        BackupProfile profile,
        IProgress<ShuttleOperationProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<ShuttleOperationResult> DepartAsync(
        BackupProfile profile,
        IProgress<ShuttleOperationProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<ShuttleOperationResult> DockAsync(
        BackupProfile profile,
        IProgress<ShuttleOperationProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<ShuttleOperationResult> ReceiveAsync(
        BackupProfile profile,
        IProgress<ShuttleOperationProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
