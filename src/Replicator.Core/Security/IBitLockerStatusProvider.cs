namespace Replicator.Core.Security;

public interface IBitLockerStatusProvider
{
    Task<DriveSecurityItem> CheckAsync(
        string label,
        string path,
        string root,
        CancellationToken cancellationToken = default);
}
