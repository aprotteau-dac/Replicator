namespace Replicator.Core.Models;

public enum BackupEngineKind
{
    NativePowerShell = 0,
    Kopia = 1,
    Restic = 2,
    Rclone = 3
}
