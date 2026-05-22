using Replicator.Core.Models;

namespace Replicator.Core.Security;

public static class ProfileDriveSecurityCandidates
{
    public static IReadOnlyList<DriveSecurityCandidate> Collect(BackupProfile profile)
    {
        var candidates = new List<DriveSecurityCandidate>();

        if (!string.IsNullOrWhiteSpace(profile.SourcePath))
        {
            candidates.Add(Create(profile.Id, "Source drive", profile.SourcePath));
        }

        if (profile.Target.Kind == BackupTargetKind.LocalPath && !string.IsNullOrWhiteSpace(profile.Target.Path))
        {
            var label = profile.Mode == ProfileMode.Shuttle ? "Shuttle drive" : "Target drive";
            candidates.Add(Create(profile.Id, label, profile.Target.Path));
        }

        return candidates;
    }

    public static IReadOnlyList<DriveSecurityCandidate> Collect(IEnumerable<BackupProfile> profiles)
    {
        return profiles.SelectMany(Collect).ToList();
    }

    public static bool TryCreateStaticItem(DriveSecurityCandidate candidate, out DriveSecurityItem item)
    {
        if (string.IsNullOrWhiteSpace(candidate.Root))
        {
            item = new DriveSecurityItem(
                candidate.Label,
                candidate.Path,
                candidate.Root,
                DriveSecurityState.NotApplicable,
                DriveSecuritySeverity.Warning,
                $"Drive security: {candidate.Label} path is not rooted.");
            return true;
        }

        if (!IsWindowsDriveRoot(candidate.Root))
        {
            item = new DriveSecurityItem(
                candidate.Label,
                candidate.Path,
                candidate.Root,
                DriveSecurityState.NotApplicable,
                DriveSecuritySeverity.Info,
                $"Drive security: {candidate.Label} is not a local Windows drive.");
            return true;
        }

        item = null!;
        return false;
    }

    public static bool IsWindowsDriveRoot(string root)
    {
        return root.Length >= 3 &&
               char.IsLetter(root[0]) &&
               root[1] == ':' &&
               (root[2] == Path.DirectorySeparatorChar || root[2] == Path.AltDirectorySeparatorChar);
    }

    private static DriveSecurityCandidate Create(Guid profileId, string label, string path)
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

        return new DriveSecurityCandidate(profileId, label, expanded, root);
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
}
