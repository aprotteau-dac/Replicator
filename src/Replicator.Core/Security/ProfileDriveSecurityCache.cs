using Replicator.Core.Models;

namespace Replicator.Core.Security;

public sealed class ProfileDriveSecurityCache
{
    private readonly Dictionary<string, DriveSecurityItem> _itemsByRoot = new(StringComparer.OrdinalIgnoreCase);

    public async Task WarmMissingAsync(
        IEnumerable<BackupProfile> profiles,
        IBitLockerStatusProvider provider,
        CancellationToken cancellationToken = default)
    {
        await CheckAsync(profiles, provider, overwriteExisting: false, cancellationToken);
    }

    public async Task RefreshAsync(
        IEnumerable<BackupProfile> profiles,
        IBitLockerStatusProvider provider,
        CancellationToken cancellationToken = default)
    {
        await CheckAsync(profiles, provider, overwriteExisting: true, cancellationToken);
    }

    public ProfileDriveSecurityReport Report(BackupProfile profile)
    {
        var items = ProfileDriveSecurityCandidates
            .Collect(profile)
            .GroupBy(candidate => candidate.Root, StringComparer.OrdinalIgnoreCase)
            .Select(group => CreateItem(group.First()))
            .ToList();

        return new ProfileDriveSecurityReport(items);
    }

    private async Task CheckAsync(
        IEnumerable<BackupProfile> profiles,
        IBitLockerStatusProvider provider,
        bool overwriteExisting,
        CancellationToken cancellationToken)
    {
        var candidates = ProfileDriveSecurityCandidates
            .Collect(profiles)
            .Where(candidate => !ProfileDriveSecurityCandidates.TryCreateStaticItem(candidate, out _))
            .GroupBy(candidate => candidate.Root, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Where(candidate => overwriteExisting || !_itemsByRoot.ContainsKey(candidate.Root))
            .ToList();

        if (candidates.Count == 0)
        {
            return;
        }

        if (provider is IBitLockerBatchStatusProvider batchProvider)
        {
            var results = await batchProvider.CheckAsync(candidates, cancellationToken);
            foreach (var (root, item) in results)
            {
                _itemsByRoot[root] = item;
            }

            return;
        }

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _itemsByRoot[candidate.Root] = await provider.CheckAsync(
                candidate.Label,
                candidate.Path,
                candidate.Root,
                cancellationToken);
        }
    }

    private DriveSecurityItem CreateItem(DriveSecurityCandidate candidate)
    {
        if (ProfileDriveSecurityCandidates.TryCreateStaticItem(candidate, out var staticItem))
        {
            return staticItem;
        }

        return _itemsByRoot.TryGetValue(candidate.Root, out var cached)
            ? Retarget(cached, candidate)
            : new DriveSecurityItem(
                candidate.Label,
                candidate.Path,
                candidate.Root,
                DriveSecurityState.Unknown,
                DriveSecuritySeverity.Warning,
                $"Drive security: {candidate.Label} BitLocker status has not been checked ({candidate.Root}).");
    }

    private static DriveSecurityItem Retarget(DriveSecurityItem cached, DriveSecurityCandidate candidate)
    {
        return new DriveSecurityItem(
            candidate.Label,
            candidate.Path,
            candidate.Root,
            cached.State,
            cached.Severity,
            MessageFor(candidate.Label, candidate.Root, cached.State, cached.Message));
    }

    private static string MessageFor(string label, string root, DriveSecurityState state, string cachedMessage)
    {
        if (cachedMessage.Contains("administrator check was canceled", StringComparison.OrdinalIgnoreCase))
        {
            return $"Drive security: {label} administrator check was canceled ({root}). Replicator can continue, but encryption state was not confirmed.";
        }

        return state switch
        {
            DriveSecurityState.Locked => $"Drive security: {label} is BitLocker locked ({root}).",
            DriveSecurityState.Protected => $"Drive security: {label} is BitLocker protected ({root}).",
            DriveSecurityState.Unprotected => $"Drive security: {label} is not BitLocker protected ({root}).",
            DriveSecurityState.PermissionRequired => $"Drive security: {label} BitLocker status requires elevated permissions ({root}). Replicator can continue, but encryption state was not confirmed. Use Check All as Admin to confirm.",
            DriveSecurityState.Unavailable => $"Drive security: {label} is unavailable ({root}).",
            DriveSecurityState.NotApplicable => $"Drive security: {label} is not a local Windows drive.",
            _ => $"Drive security: {label} BitLocker status unknown ({root})."
        };
    }
}
