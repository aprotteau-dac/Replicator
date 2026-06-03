using System.Text.RegularExpressions;

namespace Replicator.Core.Scheduling;

public static class ScheduledTaskActionInspector
{
    private static readonly Regex FileArgumentPattern = new(
        """(?i)(?:^|\s)-File\s+(?:"(?<quoted>[^"]+)"|'(?<single>[^']+)'|(?<bare>\S+))""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex PowerShellExecutablePattern = new(
        """(?i)(?:^|\s|\\|")powershell(?:\.exe)?(?=\s|"|$)""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex WScriptLauncherPattern = new(
        """(?i)(?:"[^"]*\\?wscript(?:\.exe)?"|(?:\S*\\)?wscript(?:\.exe)?)\s+(?:(?://\S+)\s+)*(?:"(?<quoted>[^"]+)"|'(?<single>[^']+)'|(?<bare>\S+))""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static ScheduledTaskActionHealth Inspect(string taskToRun, string? expectedScriptPath)
    {
        taskToRun ??= string.Empty;
        var reasons = new List<string>();
        var scriptPath = ParseScriptPath(taskToRun);
        var launcherPath = ParseLauncherPath(taskToRun);

        if (string.IsNullOrWhiteSpace(launcherPath))
        {
            reasons.Add(PowerShellExecutablePattern.IsMatch(taskToRun)
                ? "Task action uses the console PowerShell host; repair to the windowless launcher."
                : "Task action is missing the windowless launcher.");

            if (!taskToRun.Contains("-WindowStyle Hidden", StringComparison.OrdinalIgnoreCase))
            {
                reasons.Add("Task action is missing -WindowStyle Hidden.");
            }

            if (!taskToRun.Contains("-NonInteractive", StringComparison.OrdinalIgnoreCase))
            {
                reasons.Add("Task action is missing -NonInteractive.");
            }
        }
        else
        {
            if (!File.Exists(launcherPath))
            {
                reasons.Add($"Task hidden launcher is missing: {launcherPath}");
            }

            if (string.IsNullOrWhiteSpace(expectedScriptPath))
            {
                scriptPath = Path.ChangeExtension(launcherPath, ".ps1");
            }
            else
            {
                var expectedLauncherPath = PowerShellScheduledTaskLauncher.LauncherPathFor(expectedScriptPath);
                scriptPath = expectedScriptPath;

                if (!PathsEqual(launcherPath, expectedLauncherPath))
                {
                    reasons.Add($"Task hidden launcher path does not match expected profile launcher: {launcherPath}");
                }

                InspectLauncherContent(launcherPath, expectedScriptPath, reasons);
            }
        }

        var scriptExists = !string.IsNullOrWhiteSpace(scriptPath) && File.Exists(scriptPath);
        if (!string.IsNullOrWhiteSpace(scriptPath) && !scriptExists)
        {
            reasons.Add($"Task script is missing: {scriptPath}");
        }

        if (string.IsNullOrWhiteSpace(scriptPath))
        {
            reasons.Add("Task action is missing script path.");
        }

        if (string.IsNullOrWhiteSpace(launcherPath)
            && !string.IsNullOrWhiteSpace(expectedScriptPath)
            && !string.IsNullOrWhiteSpace(scriptPath)
            && !PathsEqual(scriptPath, expectedScriptPath))
        {
            reasons.Add($"Task script path does not match expected profile script: {scriptPath}");
        }

        return new ScheduledTaskActionHealth(reasons.Count > 0, reasons, scriptPath, scriptExists);
    }

    private static string ParseScriptPath(string taskToRun)
    {
        var match = FileArgumentPattern.Match(taskToRun);
        if (!match.Success)
        {
            return string.Empty;
        }

        if (match.Groups["quoted"].Success)
        {
            return match.Groups["quoted"].Value;
        }

        if (match.Groups["single"].Success)
        {
            return match.Groups["single"].Value;
        }

        return match.Groups["bare"].Success ? match.Groups["bare"].Value : string.Empty;
    }

    private static string ParseLauncherPath(string taskToRun)
    {
        var match = WScriptLauncherPattern.Match(taskToRun);
        if (!match.Success)
        {
            return string.Empty;
        }

        if (match.Groups["quoted"].Success)
        {
            return match.Groups["quoted"].Value;
        }

        if (match.Groups["single"].Success)
        {
            return match.Groups["single"].Value;
        }

        return match.Groups["bare"].Success ? match.Groups["bare"].Value : string.Empty;
    }

    private static void InspectLauncherContent(string launcherPath, string expectedScriptPath, ICollection<string> reasons)
    {
        if (!File.Exists(launcherPath))
        {
            return;
        }

        string content;
        try
        {
            content = File.ReadAllText(launcherPath);
        }
        catch (IOException exception)
        {
            reasons.Add($"Task hidden launcher could not be inspected: {exception.Message}");
            return;
        }
        catch (UnauthorizedAccessException exception)
        {
            reasons.Add($"Task hidden launcher could not be inspected: {exception.Message}");
            return;
        }

        if (!content.Contains(expectedScriptPath, StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add("Task hidden launcher does not invoke the expected profile script.");
        }

        if (!content.Contains("-WindowStyle Hidden", StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add("Task hidden launcher is missing -WindowStyle Hidden.");
        }

        if (!content.Contains("shell.Run(command, 0, True)", StringComparison.Ordinal))
        {
            reasons.Add("Task hidden launcher does not wait for the backup process.");
        }
    }

    private static bool PathsEqual(string left, string right)
    {
        try
        {
            return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
        catch (NotSupportedException)
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
        catch (PathTooLongException)
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }
}
