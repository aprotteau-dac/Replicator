namespace Replicator.Core.Availability;

public enum PathAvailabilityState
{
    Available = 0,
    Creatable = 1,
    Missing = 2,
    DriveUnavailable = 3,
    Inaccessible = 4,
    NotConfigured = 5
}
