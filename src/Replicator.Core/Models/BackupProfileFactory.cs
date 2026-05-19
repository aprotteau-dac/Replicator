namespace Replicator.Core.Models;

public static class BackupProfileFactory
{
    public static BackupProfile CreateDefault()
    {
        return new BackupProfile
        {
            ExcludePatterns =
            [
                "node_modules",
                "bin",
                "obj",
                ".vs",
                ".idea",
                ".cache",
                "__pycache__",
                ".replicator-conflicts"
            ]
        };
    }
}
