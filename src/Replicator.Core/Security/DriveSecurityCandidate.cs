namespace Replicator.Core.Security;

public sealed record DriveSecurityCandidate(
    Guid ProfileId,
    string Label,
    string Path,
    string Root);
