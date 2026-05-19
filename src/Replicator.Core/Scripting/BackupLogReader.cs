using Replicator.Core.Models;

namespace Replicator.Core.Scripting;

public sealed class BackupLogReader(string logsDirectory)
{
    public BackupRunSummary? ReadLatest(BackupProfile profile)
    {
        if (!Directory.Exists(logsDirectory))
        {
            return null;
        }

        var slug = PowerShellScriptGenerator.ProfileSlug(profile);
        var latestLog = Directory
            .EnumerateFiles(logsDirectory, $"{slug}-*.log")
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .FirstOrDefault();

        return latestLog is null ? null : Read(latestLog.FullName);
    }

    public BackupRunSummary Read(string logPath)
    {
        var lines = File.Exists(logPath)
            ? File.ReadAllLines(logPath)
            : [];

        var mode = ReadHeaderValue(lines, "Mode") ?? "";
        var directories = ReadRobocopyMetric(lines, "Dirs");
        var files = ReadRobocopyMetric(lines, "Files");
        var bytesLine = lines.FirstOrDefault(line => line.TrimStart().StartsWith("Bytes :", StringComparison.OrdinalIgnoreCase))?.Trim() ?? "";
        var tail = string.Join(Environment.NewLine, lines.TakeLast(120));
        var fileInfo = new FileInfo(logPath);

        return new BackupRunSummary(
            logPath,
            fileInfo.LastWriteTime,
            mode,
            directories.Total,
            directories.Copied,
            directories.Failed,
            files.Total,
            files.Copied,
            files.Failed,
            bytesLine,
            tail);
    }

    private static string? ReadHeaderValue(IEnumerable<string> lines, string key)
    {
        var prefix = key + ":";
        var line = lines.FirstOrDefault(candidate => candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        return line is null ? null : line[prefix.Length..].Trim();
    }

    private static RobocopyMetric ReadRobocopyMetric(IEnumerable<string> lines, string label)
    {
        var line = lines.FirstOrDefault(candidate => candidate.TrimStart().StartsWith(label + " :", StringComparison.OrdinalIgnoreCase));
        if (line is null)
        {
            return new RobocopyMetric(null, null, null);
        }

        var parts = line
            .Split([' ', '\t', ':'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();

        return parts.Length >= 7 &&
               int.TryParse(parts[1], out var total) &&
               int.TryParse(parts[2], out var copied) &&
               int.TryParse(parts[5], out var failed)
            ? new RobocopyMetric(total, copied, failed)
            : new RobocopyMetric(null, null, null);
    }

    private sealed record RobocopyMetric(int? Total, int? Copied, int? Failed);
}
