namespace Replicator.Core.Security;

public sealed class ProfileDriveSecurityReport
{
    public ProfileDriveSecurityReport(IEnumerable<DriveSecurityItem> items)
    {
        Items = items.ToList();
    }

    public IReadOnlyList<DriveSecurityItem> Items { get; }

    public bool HasErrors => Items.Any(item => item.Severity == DriveSecuritySeverity.Error);

    public bool HasWarnings => Items.Any(item => item.Severity == DriveSecuritySeverity.Warning);

    public bool RequiresElevation => Items.Any(item => item.State == DriveSecurityState.PermissionRequired);

    public string Summary
    {
        get
        {
            if (Items.Count == 0)
            {
                return "Drive security not checked.";
            }

            var firstProblem = Items
                .OrderByDescending(item => item.Severity)
                .FirstOrDefault(item => item.Severity != DriveSecuritySeverity.Info);

            if (firstProblem is not null)
            {
                return firstProblem.Message;
            }

            return Items.Count == 1
                ? Items[0].Message
                : "Drive security: checked local profile drives.";
        }
    }

    public string ToDisplayString()
    {
        return string.Join(Environment.NewLine, Items.Select(item => $"{item.Label}: {item.Message}"));
    }
}
