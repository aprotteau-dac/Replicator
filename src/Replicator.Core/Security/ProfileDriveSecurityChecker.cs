using Replicator.Core.Models;

namespace Replicator.Core.Security;

public sealed class ProfileDriveSecurityChecker(IBitLockerStatusProvider bitLockerStatusProvider)
{
    public async Task<ProfileDriveSecurityReport> CheckAsync(
        BackupProfile profile,
        CancellationToken cancellationToken = default)
    {
        var candidates = GetCandidates(profile)
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
        DriveCandidate candidate,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(candidate.Root))
        {
            return new DriveSecurityItem(
                candidate.Label,
                candidate.Path,
                candidate.Root,
                DriveSecurityState.NotApplicable,
                DriveSecuritySeverity.Warning,
                $"Drive security: {candidate.Label} path is not rooted.");
        }

        if (!IsWindowsDriveRoot(candidate.Root))
        {
            return new DriveSecurityItem(
                candidate.Label,
                candidate.Path,
                candidate.Root,
                DriveSecurityState.NotApplicable,
                DriveSecuritySeverity.Info,
                $"Drive security: {candidate.Label} is not a local Windows drive.");
        }

        return await bitLockerStatusProvider.CheckAsync(
            candidate.Label,
            candidate.Path,
            candidate.Root,
            cancellationToken);
    }

    private static IEnumerable<DriveCandidate> GetCandidates(BackupProfile profile)
    {
        if (!string.IsNullOrWhiteSpace(profile.SourcePath))
        {
            yield return CreateCandidate("Source drive", profile.SourcePath);
        }

        if (profile.Target.Kind == BackupTargetKind.LocalPath && !string.IsNullOrWhiteSpace(profile.Target.Path))
        {
            var label = profile.Mode == ProfileMode.Shuttle ? "Shuttle drive" : "Target drive";
            yield return CreateCandidate(label, profile.Target.Path);
        }
    }

    private static DriveCandidate CreateCandidate(string label, string path)
    {
        var expanded = ExpandPath(path);
        string root;
        try
        {
            root = Path.GetPathRoot(expanded) ?? "";
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            root = "";
        }

        return new DriveCandidate(label, expanded, root);
    }

    private static string ExpandPath(string path)
    {
        try
        {
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim()));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path;
        }
    }

    private static bool IsWindowsDriveRoot(string root)
    {
        return root.Length >= 3 &&
               char.IsLetter(root[0]) &&
               root[1] == ':' &&
               (root[2] == Path.DirectorySeparatorChar || root[2] == Path.AltDirectorySeparatorChar);
    }

    private sealed record DriveCandidate(string Label, string Path, string Root);
}
