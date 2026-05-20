namespace Replicator.Core.Availability;

public sealed record PathAvailabilityItem(
    string Label,
    string Path,
    PathAvailabilityState State,
    AvailabilitySeverity Severity,
    string Message);
