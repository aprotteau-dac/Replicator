using System.Text.RegularExpressions;

namespace Replicator.Core.Scheduling;

public static class ScheduledTaskActionInspector
{
    private static readonly Regex FileArgumentPattern = new(
        """(?i)(?:^|\s)-File\s+(?:"(?<quoted>[^"]+)"|'(?<single>[^']+)'|(?<bare>\S+))""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static ScheduledTaskActionHealth Inspect(string taskToRun, string? expectedScriptPath)
    {
        taskToRun ??= string.Empty;
        var reasons = new List<string>();

        if (!taskToRun.Contains("-WindowStyle Hidden", StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add("Task action is missing -WindowStyle Hidden.");
        }

        if (!taskToRun.Contains("-NonInteractive", StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add("Task action is missing -NonInteractive.");
        }

        var scriptPath = ParseScriptPath(taskToRun);
        if (string.IsNullOrWhiteSpace(scriptPath))
        {
            reasons.Add("Task action is missing -File script path.");
        }

        var scriptExists = !string.IsNullOrWhiteSpace(scriptPath) && File.Exists(scriptPath);
        if (!string.IsNullOrWhiteSpace(scriptPath) && !scriptExists)
        {
            reasons.Add($"Task script is missing: {scriptPath}");
        }

        if (!string.IsNullOrWhiteSpace(expectedScriptPath)
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
