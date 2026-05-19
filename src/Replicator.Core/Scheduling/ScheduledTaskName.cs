using System.Text;
using Replicator.Core.Models;

namespace Replicator.Core.Scheduling;

public static class ScheduledTaskName
{
    public static string ForProfile(BackupProfile profile)
    {
        var safeName = MakeSafeSegment(profile.Name);
        return $@"\Replicator\{safeName}-{profile.Id.ToString("N")[..8]}";
    }

    private static string MakeSafeSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "profile";
        }

        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Trim())
        {
            if (char.IsLetterOrDigit(character) || character is '-' or '_')
            {
                builder.Append(character);
            }
            else if (char.IsWhiteSpace(character))
            {
                builder.Append('-');
            }
        }

        var safeName = string.Join('-', builder.ToString().Split('-', StringSplitOptions.RemoveEmptyEntries));
        return string.IsNullOrWhiteSpace(safeName) ? "profile" : safeName;
    }
}
