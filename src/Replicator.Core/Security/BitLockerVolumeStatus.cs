namespace Replicator.Core.Security;

public sealed record BitLockerVolumeStatus(
    string MountPoint,
    string VolumeStatus,
    string ProtectionStatus,
    string LockStatus,
    double? EncryptionPercentage);
