using Replicator.Core.Models;

namespace Replicator.Core.Availability;

public sealed class ProfileAvailabilityChecker
{
    public ProfileAvailabilityReport Check(BackupProfile profile)
    {
        var items = new List<PathAvailabilityItem>
        {
            CheckExistingDirectory("Source", profile.SourcePath)
        };

        if (profile.Target.Kind == BackupTargetKind.LocalPath)
        {
            var targetLabel = profile.Mode == ProfileMode.Shuttle ? "Shuttle path" : "Target";
            items.Add(CheckTargetDirectory(targetLabel, profile.Target.Path));
        }

        return new ProfileAvailabilityReport(items);
    }

    private static PathAvailabilityItem CheckExistingDirectory(string label, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return new PathAvailabilityItem(
                label,
                path,
                PathAvailabilityState.NotConfigured,
                AvailabilitySeverity.Error,
                $"{label} path is not configured.");
        }

        if (!TryExpandPath(label, path, out var expanded, out var pathIssue))
        {
            return pathIssue;
        }

        var rootIssue = CheckRoot(label, expanded);
        if (rootIssue is not null)
        {
            return rootIssue;
        }

        try
        {
            return Directory.Exists(expanded)
                ? new PathAvailabilityItem(label, expanded, PathAvailabilityState.Available, AvailabilitySeverity.Info, $"{label} is available.")
                : new PathAvailabilityItem(label, expanded, PathAvailabilityState.Missing, AvailabilitySeverity.Error, $"{label} path is unavailable: {expanded}");
        }
        catch (UnauthorizedAccessException)
        {
            return new PathAvailabilityItem(label, expanded, PathAvailabilityState.Inaccessible, AvailabilitySeverity.Error, $"{label} path is inaccessible: {expanded}");
        }
        catch (IOException exception)
        {
            return new PathAvailabilityItem(label, expanded, PathAvailabilityState.Inaccessible, AvailabilitySeverity.Error, $"{label} path cannot be checked: {exception.Message}");
        }
    }

    private static PathAvailabilityItem CheckTargetDirectory(string label, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return new PathAvailabilityItem(
                label,
                path,
                PathAvailabilityState.NotConfigured,
                AvailabilitySeverity.Error,
                $"{label} path is not configured.");
        }

        if (!TryExpandPath(label, path, out var expanded, out var pathIssue))
        {
            return pathIssue;
        }

        var rootIssue = CheckRoot(label, expanded);
        if (rootIssue is not null)
        {
            return rootIssue;
        }

        try
        {
            if (Directory.Exists(expanded))
            {
                return new PathAvailabilityItem(label, expanded, PathAvailabilityState.Available, AvailabilitySeverity.Info, $"{label} is available.");
            }

            var existingParent = FindExistingParent(expanded);
            return existingParent is null
                ? new PathAvailabilityItem(label, expanded, PathAvailabilityState.Missing, AvailabilitySeverity.Error, $"{label} path cannot be created because no parent folder is available: {expanded}")
                : new PathAvailabilityItem(label, expanded, PathAvailabilityState.Creatable, AvailabilitySeverity.Warning, $"{label} does not exist yet, but can be created under: {existingParent}");
        }
        catch (UnauthorizedAccessException)
        {
            return new PathAvailabilityItem(label, expanded, PathAvailabilityState.Inaccessible, AvailabilitySeverity.Error, $"{label} path is inaccessible: {expanded}");
        }
        catch (IOException exception)
        {
            return new PathAvailabilityItem(label, expanded, PathAvailabilityState.Inaccessible, AvailabilitySeverity.Error, $"{label} path cannot be checked: {exception.Message}");
        }
    }

    private static PathAvailabilityItem? CheckRoot(string label, string path)
    {
        var root = Path.GetPathRoot(path);
        if (string.IsNullOrWhiteSpace(root))
        {
            return new PathAvailabilityItem(label, path, PathAvailabilityState.Missing, AvailabilitySeverity.Error, $"{label} path is not rooted: {path}");
        }

        return Directory.Exists(root)
            ? null
            : new PathAvailabilityItem(label, path, PathAvailabilityState.DriveUnavailable, AvailabilitySeverity.Error, $"{label} drive is unavailable: {root}");
    }

    private static string? FindExistingParent(string path)
    {
        var directory = new DirectoryInfo(path);
        var parent = directory.Parent;
        while (parent is not null)
        {
            if (parent.Exists)
            {
                return parent.FullName;
            }

            parent = parent.Parent;
        }

        return null;
    }

    private static bool TryExpandPath(
        string label,
        string path,
        out string expanded,
        out PathAvailabilityItem issue)
    {
        try
        {
            expanded = ExpandPath(path);
            issue = null!;
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            expanded = path;
            issue = new PathAvailabilityItem(
                label,
                path,
                PathAvailabilityState.Inaccessible,
                AvailabilitySeverity.Error,
                $"{label} path cannot be checked: {exception.Message}");
            return false;
        }
    }

    private static string ExpandPath(string path)
    {
        return Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim()));
    }
}
