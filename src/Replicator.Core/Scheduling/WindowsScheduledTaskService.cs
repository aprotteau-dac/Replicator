using Replicator.Core.Execution;
using Replicator.Core.Models;

namespace Replicator.Core.Scheduling;

public sealed class WindowsScheduledTaskService(ProcessRunner processRunner) : IScheduledTaskService
{
    public async Task<TaskOperationResult> InstallOrUpdateAsync(
        BackupProfile profile,
        string scriptPath,
        CancellationToken cancellationToken = default)
    {
        if (profile.Schedule.Cadence == ScheduleCadence.Manual)
        {
            return new TaskOperationResult(false, "Manual profiles do not create scheduled tasks.");
        }

        var taskName = ScheduledTaskName.ForProfile(profile);
        var arguments = BuildCreateArguments(profile, scriptPath, taskName);
        var result = await processRunner.RunAsync("schtasks.exe", arguments, cancellationToken);

        if (!result.Succeeded)
        {
            return new TaskOperationResult(false, BuildFailureMessage("install", result), CombineOutput(result));
        }

        if (!profile.Schedule.Enabled)
        {
            await DisableAsync(profile, cancellationToken);
        }

        return new TaskOperationResult(true, $"Installed scheduled task {taskName}.", CombineOutput(result));
    }

    public async Task<TaskOperationResult> RunAsync(BackupProfile profile, CancellationToken cancellationToken = default)
    {
        var result = await processRunner.RunAsync(
            "schtasks.exe",
            ["/Run", "/TN", ScheduledTaskName.ForProfile(profile)],
            cancellationToken);

        return ToOperationResult("run", result, ScheduledTaskName.ForProfile(profile));
    }

    public async Task<TaskOperationResult> EnableAsync(BackupProfile profile, CancellationToken cancellationToken = default)
    {
        var result = await processRunner.RunAsync(
            "schtasks.exe",
            ["/Change", "/TN", ScheduledTaskName.ForProfile(profile), "/ENABLE"],
            cancellationToken);

        return ToOperationResult("enable", result, ScheduledTaskName.ForProfile(profile));
    }

    public async Task<TaskOperationResult> DisableAsync(BackupProfile profile, CancellationToken cancellationToken = default)
    {
        var result = await processRunner.RunAsync(
            "schtasks.exe",
            ["/Change", "/TN", ScheduledTaskName.ForProfile(profile), "/DISABLE"],
            cancellationToken);

        return ToOperationResult("disable", result, ScheduledTaskName.ForProfile(profile));
    }

    public async Task<TaskOperationResult> DeleteAsync(BackupProfile profile, CancellationToken cancellationToken = default)
    {
        var result = await processRunner.RunAsync(
            "schtasks.exe",
            ["/Delete", "/TN", ScheduledTaskName.ForProfile(profile), "/F"],
            cancellationToken);

        return ToOperationResult("delete", result, ScheduledTaskName.ForProfile(profile));
    }

    public async Task<ScheduledTaskSnapshot> QueryAsync(BackupProfile profile, CancellationToken cancellationToken = default)
    {
        var taskName = ScheduledTaskName.ForProfile(profile);
        var result = await processRunner.RunAsync(
            "schtasks.exe",
            ["/Query", "/TN", taskName, "/FO", "LIST", "/V"],
            cancellationToken);

        if (!result.Succeeded)
        {
            return new ScheduledTaskSnapshot(taskName, ScheduledTaskState.Missing, "", "", 0, CombineOutput(result));
        }

        var values = ParseListOutput(result.StandardOutput);
        var state = ParseState(values.GetValueOrDefault("Status"));
        var lastResult = int.TryParse(values.GetValueOrDefault("Last Result"), out var parsedLastResult)
            ? parsedLastResult
            : 0;

        return new ScheduledTaskSnapshot(
            taskName,
            state,
            values.GetValueOrDefault("Next Run Time") ?? "",
            values.GetValueOrDefault("Last Run Time") ?? "",
            lastResult,
            result.StandardOutput);
    }

    private static IReadOnlyList<string> BuildCreateArguments(BackupProfile profile, string scriptPath, string taskName)
    {
        var arguments = new List<string>
        {
            "/Create",
            "/TN",
            taskName,
            "/TR",
            $"powershell.exe -NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
            "/F"
        };

        switch (profile.Schedule.Cadence)
        {
            case ScheduleCadence.Daily:
                arguments.AddRange(["/SC", "DAILY", "/ST", FormatTime(profile.Schedule.TimeOfDay)]);
                break;
            case ScheduleCadence.Weekly:
                arguments.AddRange(["/SC", "WEEKLY", "/D", FormatDay(profile.Schedule.DayOfWeek), "/ST", FormatTime(profile.Schedule.TimeOfDay)]);
                break;
            case ScheduleCadence.Hourly:
                arguments.AddRange(["/SC", "HOURLY", "/MO", profile.Schedule.IntervalHours.ToString(), "/ST", FormatTime(profile.Schedule.TimeOfDay)]);
                break;
            case ScheduleCadence.Minutes:
                arguments.AddRange(["/SC", "MINUTE", "/MO", profile.Schedule.IntervalMinutes.ToString(), "/ST", FormatTime(profile.Schedule.TimeOfDay)]);
                break;
            default:
                throw new InvalidOperationException($"Unsupported schedule cadence: {profile.Schedule.Cadence}");
        }

        return arguments;
    }

    private static TaskOperationResult ToOperationResult(string action, ProcessResult result, string taskName)
    {
        return result.Succeeded
            ? new TaskOperationResult(true, $"Completed {action} for {taskName}.", CombineOutput(result))
            : new TaskOperationResult(false, BuildFailureMessage(action, result), CombineOutput(result));
    }

    private static string BuildFailureMessage(string action, ProcessResult result)
    {
        var output = CombineOutput(result).Trim();
        return string.IsNullOrWhiteSpace(output)
            ? $"Failed to {action} scheduled task. Exit code: {result.ExitCode}."
            : $"Failed to {action} scheduled task. Exit code: {result.ExitCode}. {output}";
    }

    private static Dictionary<string, string> ParseListOutput(string output)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
        {
            var index = line.IndexOf(':');
            if (index <= 0)
            {
                continue;
            }

            var key = line[..index].Trim();
            var value = line[(index + 1)..].Trim();
            values[key] = value;
        }

        return values;
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

    private static string FormatTime(TimeOnly time)
    {
        return time.ToString("HH\\:mm");
    }

    private static string FormatDay(DayOfWeek dayOfWeek)
    {
        return dayOfWeek switch
        {
            DayOfWeek.Monday => "MON",
            DayOfWeek.Tuesday => "TUE",
            DayOfWeek.Wednesday => "WED",
            DayOfWeek.Thursday => "THU",
            DayOfWeek.Friday => "FRI",
            DayOfWeek.Saturday => "SAT",
            DayOfWeek.Sunday => "SUN",
            _ => throw new ArgumentOutOfRangeException(nameof(dayOfWeek), dayOfWeek, null)
        };
    }

    private static string CombineOutput(ProcessResult result)
    {
        return string.Join(
            Environment.NewLine,
            new[] { result.StandardOutput, result.StandardError }.Where(value => !string.IsNullOrWhiteSpace(value)));
    }
}
