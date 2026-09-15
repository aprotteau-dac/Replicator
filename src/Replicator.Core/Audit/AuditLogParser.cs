using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Replicator.Core.Models;

namespace Replicator.Core.Audit;

public static partial class AuditLogParser
{
    [GeneratedRegex(@"^(?<slug>.+)-\d{8}-\d{6}(?:-[a-fA-F0-9]{32})?\.log$", RegexOptions.IgnoreCase)]
    private static partial Regex LogName();
    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}Z (?:Replication completed with robocopy|Robocopy failed with) exit code (\d+)\.", RegexOptions.IgnoreCase)]
    private static partial Regex ExitCode();
    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}Z ERROR:")]
    private static partial Regex ErrorLine();

    public static string? Slug(string path)
    {
        var match = LogName().Match(Path.GetFileName(path));
        return match.Success ? match.Groups["slug"].Value : null;
    }

    public static AuditJob Read(Stream stream, string path, IReadOnlyList<BackupProfile> profiles)
    {
        var slug = Slug(path) ?? throw new InvalidDataException("Not a timestamped Replicator log.");
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var counters = new Dictionary<string, long>();
        var recognized = false;
        var completed = false;
        int? exit = null;
        DateTimeOffset? completedUtc = null;
        string error = "";
        using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        while (reader.ReadLine() is { } line)
        {
            if (line == "Replicator backup run") recognized = true;
            foreach (var key in new[] { "Profile", "Mode", "Source", "Destination", "Started", "Mirror deletes", "Excludes" })
                if (line.StartsWith(key + ":", StringComparison.OrdinalIgnoreCase))
                    headers.TryAdd(key, AuditJob.Bound(line[(key.Length + 1)..].Trim()));
            var trimmed = line.TrimStart();
            foreach (var label in new[] { "Dirs", "Files", "Bytes" })
            {
                if (!trimmed.StartsWith(label + " :", StringComparison.OrdinalIgnoreCase)) continue;
                var parts = trimmed.Split([' ', '\t', ':'], StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 7) continue;
                var names = new[] { "Total", "Copied", "Skipped", "Mismatch", "Failed", "Extras" };
                for (var i = 0; i < names.Length; i++)
                    if (long.TryParse(parts[i + 1], NumberStyles.Integer | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var count))
                        counters[label + names[i]] = count;
            }
            var match = ExitCode().Match(line);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var code)) { exit = code; completed = true; }
            if (ErrorLine().IsMatch(line)) error = AuditJob.Bound(line);
            if ((match.Success || ErrorLine().IsMatch(line)) && line.Length >= 20 &&
                DateTimeOffset.TryParseExact(line[..20], "yyyy-MM-dd HH:mm:ss'Z'", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal, out var ended)) completedUtc = ended.ToUniversalTime();
            if (trimmed.StartsWith("Ended :", StringComparison.OrdinalIgnoreCase)) completed = true;
        }
        if (!recognized) throw new InvalidDataException("Missing Replicator log header.");
        var profile = profiles.SingleOrDefault(p => slug.EndsWith("-" + p.Id.ToString("N")[..12], StringComparison.OrdinalIgnoreCase));
        var started = headers.TryGetValue("Started", out var start) && DateTimeOffset.TryParse(start, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var parsed) ? parsed.ToUniversalTime() : (DateTimeOffset?)null;
        var failed = error.Length > 0 || exit > 7 || counters.GetValueOrDefault("FilesFailed") > 0 || counters.GetValueOrDefault("DirsFailed") > 0;
        return new AuditJob
        {
            ProfileId = profile?.Id,
            ProfileSnapshot = JsonSerializer.Serialize(new { Name = headers.GetValueOrDefault("Profile", slug),
                Mode = headers.GetValueOrDefault("Mode", "unknown"), SourcePath = headers.GetValueOrDefault("Source", ""),
                DestinationPath = headers.GetValueOrDefault("Destination", ""),
                MirrorDeletes = headers.GetValueOrDefault("Mirror deletes"), Excludes = headers.GetValueOrDefault("Excludes") }),
            SourcePath = headers.GetValueOrDefault("Source", ""), DestinationPath = headers.GetValueOrDefault("Destination", ""),
            Operation = headers.GetValueOrDefault("Mode", "").StartsWith("Dry run", StringComparison.OrdinalIgnoreCase) ? "dry_run" : "backup",
            StartedUtc = started,
            CompletedUtc = completedUtc,
            Status = failed ? "failed" : exit.HasValue ? "succeeded" : completed ? "completed_unverified" : "unknown",
            RobocopyExitCode = exit, Error = error, Counters = JsonSerializer.Serialize(counters),
            Provenance = "historical_log", Confidence = exit.HasValue ? "explicit_exit" : completed ? "summary_only" : "uncertain",
            Artifacts = [new("log", Path.GetFullPath(path))]
        };
    }
}
