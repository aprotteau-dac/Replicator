using Replicator.Core.Execution;
using Replicator.Core.Models;

namespace Replicator.Core.Scheduling;

public sealed class WindowsScheduledTaskInventoryService(IProcessRunner processRunner) : IScheduledTaskInventoryService
{
    private const string ReplicatorTaskPrefix = @"\Replicator\";

    public async Task<ScheduledTaskInventoryResult> ScanAsync(
        IReadOnlyList<BackupProfile> profiles,
        IReadOnlyDictionary<Guid, string> expectedScriptPaths,
        CancellationToken cancellationToken = default)
    {
        var result = await processRunner.RunAsync(
            "schtasks.exe",
            ["/Query", "/FO", "LIST", "/V"],
            cancellationToken);

        if (!result.Succeeded)
        {
            var output = CombineOutput(result);
            var item = new ScheduledTaskInventoryItem(
                @"\Replicator",
                null,
                "Task Scheduler",
                ScheduledTaskInventoryState.Unknown,
                ScheduledTaskState.Unknown,
                string.Empty,
                string.Empty,
                result.ExitCode,
                string.Empty,
                string.Empty,
                false,
                [],
                $"Failed to query scheduled tasks. {output}".Trim(),
                output);

            return BuildResult([item], output);
        }

        var profileByTaskName = profiles.ToDictionary(ScheduledTaskName.ForProfile, StringComparer.OrdinalIgnoreCase);
        var items = ParseRecords(result.StandardOutput)
            .Where(record => record.TryGetValue("TaskName", out var taskName)
                && taskName.StartsWith(ReplicatorTaskPrefix, StringComparison.OrdinalIgnoreCase))
            .Select(record => ToInventoryItem(record, profileByTaskName, expectedScriptPaths))
            .OrderBy(item => item.InventoryState)
            .ThenBy(item => item.TaskName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return BuildResult(items, result.StandardOutput);
    }

    private static ScheduledTaskInventoryItem ToInventoryItem(
        IReadOnlyDictionary<string, string> record,
        IReadOnlyDictionary<string, BackupProfile> profileByTaskName,
        IReadOnlyDictionary<Guid, string> expectedScriptPaths)
    {
        var taskName = record.GetValueOrDefault("TaskName") ?? string.Empty;
        var taskState = ParseState(record.GetValueOrDefault("Status"));
        var lastResult = int.TryParse(record.GetValueOrDefault("Last Result"), out var parsedLastResult)
            ? parsedLastResult
            : 0;
        var nextRun = record.GetValueOrDefault("Next Run Time") ?? string.Empty;
        var lastRun = record.GetValueOrDefault("Last Run Time") ?? string.Empty;
        var taskToRun = record.GetValueOrDefault("Task To Run") ?? string.Empty;
        var rawOutput = string.Join(Environment.NewLine, record.Select(pair => $"{pair.Key}: {pair.Value}"));

        if (!profileByTaskName.TryGetValue(taskName, out var profile))
        {
            return new ScheduledTaskInventoryItem(
                taskName,
                null,
                "No matching profile",
                ScheduledTaskInventoryState.Orphaned,
                taskState,
                nextRun,
                lastRun,
                lastResult,
                taskToRun,
                string.Empty,
                false,
                [],
                "No profile matches this task name.",
                rawOutput);
        }

        expectedScriptPaths.TryGetValue(profile.Id, out var expectedScriptPath);
        var actionHealth = ScheduledTaskActionInspector.Inspect(taskToRun, expectedScriptPath);
        var inventoryState = taskState == ScheduledTaskState.Running
            ? ScheduledTaskInventoryState.Running
            : actionHealth.NeedsRepair
                ? ScheduledTaskInventoryState.NeedsRepair
                : taskState == ScheduledTaskState.Unknown
                    ? ScheduledTaskInventoryState.Unknown
                    : ScheduledTaskInventoryState.Ready;

        var reason = inventoryState switch
        {
            ScheduledTaskInventoryState.Ready => "Task action is current.",
            ScheduledTaskInventoryState.Running => actionHealth.NeedsRepair
                ? $"Task is running. {string.Join(" ", actionHealth.RepairReasons)}"
                : "Task is running.",
            ScheduledTaskInventoryState.NeedsRepair => string.Join(" ", actionHealth.RepairReasons),
            ScheduledTaskInventoryState.Unknown => "Task state could not be determined.",
            _ => string.Empty
        };

        return new ScheduledTaskInventoryItem(
            taskName,
            profile.Id,
            profile.Name,
            inventoryState,
            taskState,
            nextRun,
            lastRun,
            lastResult,
            taskToRun,
            actionHealth.ScriptPath,
            actionHealth.ScriptExists,
            actionHealth.RepairReasons,
            reason,
            rawOutput);
    }

    private static ScheduledTaskInventoryResult BuildResult(IReadOnlyList<ScheduledTaskInventoryItem> items, string rawOutput)
    {
        var summary = new ScheduledTaskInventorySummary(
            items.Count,
            items.Count(item => item.InventoryState == ScheduledTaskInventoryState.Ready),
            items.Count(item => item.InventoryState == ScheduledTaskInventoryState.NeedsRepair),
            items.Count(item => item.InventoryState == ScheduledTaskInventoryState.Orphaned),
            items.Count(item => item.InventoryState == ScheduledTaskInventoryState.Running),
            items.Count(item => item.InventoryState == ScheduledTaskInventoryState.Unknown));

        return new ScheduledTaskInventoryResult(items, summary, rawOutput);
    }

    private static IReadOnlyList<Dictionary<string, string>> ParseRecords(string output)
    {
        var records = new List<Dictionary<string, string>>();
        var current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in output.Split(["\r\n", "\n"], StringSplitOptions.None))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                AddCurrentRecord(records, current);
                current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                continue;
            }

            var index = line.IndexOf(':');
            if (index <= 0)
            {
                continue;
            }

            var key = line[..index].Trim();
            var value = line[(index + 1)..].Trim();
            if (key.Equals("TaskName", StringComparison.OrdinalIgnoreCase) && current.ContainsKey("TaskName"))
            {
                AddCurrentRecord(records, current);
                current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            current[key] = value;
        }

        AddCurrentRecord(records, current);
        return records;
    }

    private static void AddCurrentRecord(ICollection<Dictionary<string, string>> records, Dictionary<string, string> current)
    {
        if (current.Count > 0)
        {
            records.Add(current);
        }
    }

    private static ScheduledTaskState ParseState(string? status)
    {
        return status?.Trim().ToUpperInvariant() switch
        {
            "READY" => ScheduledTaskState.Ready,
            "RUNNING" => ScheduledTaskState.Running,
            "DISABLED" => ScheduledTaskState.Disabled,
            "" or null => ScheduledTaskState.Unknown,
            _ => ScheduledTaskState.Unknown
        };
    }

    private static string CombineOutput(ProcessResult result)
    {
        return string.Join(
            Environment.NewLine,
            new[] { result.StandardOutput, result.StandardError }.Where(value => !string.IsNullOrWhiteSpace(value)));
    }
}
