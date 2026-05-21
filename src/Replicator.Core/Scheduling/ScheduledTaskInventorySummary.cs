namespace Replicator.Core.Scheduling;

public sealed record ScheduledTaskInventorySummary(
    int Total,
    int Ready,
    int NeedsRepair,
    int Orphaned,
    int Running,
    int Unknown)
{
    public bool HasIssues => NeedsRepair > 0 || Orphaned > 0 || Unknown > 0;

    public string ToDisplayString()
    {
        if (Total == 0)
        {
            return "No Replicator scheduled tasks found.";
        }

        if (!HasIssues)
        {
            return $"Scheduled tasks checked: {Ready} ready, {Running} running.";
        }

        var parts = new List<string>();
        if (NeedsRepair > 0)
        {
            parts.Add($"{NeedsRepair} need repair");
        }

        if (Orphaned > 0)
        {
            parts.Add($"{Orphaned} orphaned");
        }

        if (Unknown > 0)
        {
            parts.Add($"{Unknown} unknown");
        }

        if (Running > 0)
        {
            parts.Add($"{Running} running");
        }

        if (Ready > 0)
        {
            parts.Add($"{Ready} ready");
        }

        return $"{Total} scheduled task(s) found: {string.Join(", ", parts)}.";
    }
}
