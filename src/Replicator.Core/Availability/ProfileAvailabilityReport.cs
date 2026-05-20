namespace Replicator.Core.Availability;

public sealed class ProfileAvailabilityReport
{
    public ProfileAvailabilityReport(IEnumerable<PathAvailabilityItem> items)
    {
        Items = items.ToList();
    }

    public IReadOnlyList<PathAvailabilityItem> Items { get; }

    public bool HasErrors => Items.Any(item => item.Severity == AvailabilitySeverity.Error);

    public bool HasWarnings => Items.Any(item => item.Severity == AvailabilitySeverity.Warning);

    public string Summary
    {
        get
        {
            if (Items.Count == 0)
            {
                return "Availability not checked.";
            }

            var firstProblem = Items
                .OrderByDescending(item => item.Severity)
                .FirstOrDefault(item => item.Severity != AvailabilitySeverity.Info);

            return firstProblem?.Message ?? "Source and target are available.";
        }
    }

    public string ToDisplayString()
    {
        return string.Join(Environment.NewLine, Items.Select(item => $"{item.Label}: {item.Message}"));
    }
}
