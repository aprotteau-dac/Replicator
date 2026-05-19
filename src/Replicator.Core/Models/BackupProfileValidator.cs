namespace Replicator.Core.Models;

public static class BackupProfileValidator
{
    public static IReadOnlyList<ProfileValidationIssue> Validate(BackupProfile profile)
    {
        var issues = new List<ProfileValidationIssue>();

        if (profile.Id == Guid.Empty)
        {
            issues.Add(new(nameof(profile.Id), "Profile id is required."));
        }

        if (string.IsNullOrWhiteSpace(profile.Name))
        {
            issues.Add(new(nameof(profile.Name), "Profile name is required."));
        }

        if (string.IsNullOrWhiteSpace(profile.SourcePath))
        {
            issues.Add(new(nameof(profile.SourcePath), "Source path is required."));
        }

        if (profile.Engine != BackupEngineKind.NativePowerShell)
        {
            issues.Add(new(nameof(profile.Engine), "Only native PowerShell local replication is implemented in v1."));
        }

        if (profile.Target.Kind != BackupTargetKind.LocalPath)
        {
            issues.Add(new(nameof(profile.Target), "Only local path targets are implemented in v1."));
        }

        if (profile.Target.Kind == BackupTargetKind.LocalPath && string.IsNullOrWhiteSpace(profile.Target.Path))
        {
            issues.Add(new(nameof(profile.Target.Path), "Destination path is required."));
        }

        if (!string.IsNullOrWhiteSpace(profile.SourcePath) && !string.IsNullOrWhiteSpace(profile.Target.Path))
        {
            if (PathContains(profile.SourcePath, profile.Target.Path) || PathContains(profile.Target.Path, profile.SourcePath))
            {
                var label = profile.Mode == ProfileMode.Shuttle ? "Shuttle path" : "Destination";
                issues.Add(new(nameof(profile.Target.Path), $"{label} must not overlap the source path."));
            }
        }

        if (profile.Schedule.Cadence == ScheduleCadence.Hourly && profile.Schedule.IntervalHours is < 1 or > 23)
        {
            issues.Add(new(nameof(profile.Schedule.IntervalHours), "Hourly interval must be between 1 and 23."));
        }

        return issues;
    }

    private static bool PathContains(string parentPath, string childPath)
    {
        string parent;
        string child;

        try
        {
            parent = NormalizePath(parentPath);
            child = NormalizePath(childPath);
        }
        catch (Exception)
        {
            return false;
        }

        return child.Equals(parent, StringComparison.OrdinalIgnoreCase) ||
               child.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path)
    {
        var fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim()));
        return fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
