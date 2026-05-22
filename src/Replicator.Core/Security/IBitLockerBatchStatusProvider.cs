namespace Replicator.Core.Security;

public interface IBitLockerBatchStatusProvider
{
    Task<IReadOnlyDictionary<string, DriveSecurityItem>> CheckAsync(
        IReadOnlyList<DriveSecurityCandidate> candidates,
        CancellationToken cancellationToken = default);
}
