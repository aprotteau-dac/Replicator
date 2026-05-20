namespace Replicator.Core.Security;

public enum DriveSecurityState
{
    Unknown = 0,
    Protected = 1,
    Unprotected = 2,
    Locked = 3,
    Unavailable = 4,
    NotApplicable = 5,
    PermissionRequired = 6
}
