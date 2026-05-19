namespace Replicator.Core;

public sealed class ReplicatorPaths
{
    public ReplicatorPaths(string rootDirectory)
    {
        RootDirectory = rootDirectory;
        ProfilesFile = Path.Combine(rootDirectory, "profiles.json");
        MachineIdentityFile = Path.Combine(rootDirectory, "machine-id.txt");
        ScriptsDirectory = Path.Combine(rootDirectory, "scripts");
        LogsDirectory = Path.Combine(rootDirectory, "logs");
    }

    public string RootDirectory { get; }

    public string ProfilesFile { get; }

    public string MachineIdentityFile { get; }

    public string ScriptsDirectory { get; }

    public string LogsDirectory { get; }

    public static ReplicatorPaths CreateDefault()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new ReplicatorPaths(Path.Combine(localAppData, "Replicator"));
    }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(ScriptsDirectory);
        Directory.CreateDirectory(LogsDirectory);
    }
}
