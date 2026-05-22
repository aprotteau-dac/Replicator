using Replicator.Core.Models;

namespace Replicator.Core.Security;

public sealed class ProfileDriveSecurityChecker(IBitLockerStatusProvider bitLockerStatusProvider)
{
    public async Task<ProfileDriveSecurityReport> CheckAsync(
        BackupProfile profile,
        CancellationToken cancellationToken = default)
    {
        var candidates = ProfileDriveSecurityCandidates
            .Collect(profile)
            .GroupBy(candidate => candidate.Root, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        var items = new List<DriveSecurityItem>();
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            items.Add(await CheckCandidateAsync(candidate, cancellationToken));
        }

        return new ProfileDriveSecurityReport(items);
    }

    private async Task<DriveSecurityItem> CheckCandidateAsync(
        DriveSecurityCandidate candidate,
        CancellationToken cancellationToken)
    {
        if (ProfileDriveSecurityCandidates.TryCreateStaticItem(candidate, out var staticItem))
        {
            return staticItem;
        }

        return await bitLockerStatusProvider.CheckAsync(
            candidate.Label,
            candidate.Path,
            candidate.Root,
            cancellationToken);
    }
}
