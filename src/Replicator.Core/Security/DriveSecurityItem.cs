namespace Replicator.Core.Security;

public sealed record DriveSecurityItem(
    string Label,
    string Path,
    string Root,
    DriveSecurityState State,
    DriveSecuritySeverity Severity,
    string Message);
