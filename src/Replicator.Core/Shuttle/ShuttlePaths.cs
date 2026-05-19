using Replicator.Core.Models;

namespace Replicator.Core.Shuttle;

public sealed class ShuttlePaths
{
    private ShuttlePaths(string pairRoot)
    {
        PairRoot = pairRoot;
        PayloadDirectory = Path.Combine(pairRoot, "payload");
        ManifestsDirectory = Path.Combine(pairRoot, "manifests");
        StateDirectory = Path.Combine(pairRoot, "state");
        ConflictsDirectory = Path.Combine(pairRoot, "conflicts");
    }

    public string PairRoot { get; }

    public string PayloadDirectory { get; }

    public string ManifestsDirectory { get; }

    public string StateDirectory { get; }

    public string ConflictsDirectory { get; }

    public static ShuttlePaths FromProfile(BackupProfile profile)
    {
        var expandedPath = Environment.ExpandEnvironmentVariables(profile.Target.Path.Trim());
        return new ShuttlePaths(Path.GetFullPath(expandedPath));
    }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(PairRoot);
        Directory.CreateDirectory(PayloadDirectory);
        Directory.CreateDirectory(ManifestsDirectory);
        Directory.CreateDirectory(StateDirectory);
        Directory.CreateDirectory(ConflictsDirectory);
    }
}
