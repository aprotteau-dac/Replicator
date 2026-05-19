namespace Replicator.Core.Shuttle;

public sealed class MachineIdentityProvider(string identityFilePath)
{
    public MachineIdentity GetOrCreate()
    {
        var directory = Path.GetDirectoryName(identityFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string machineId;
        if (File.Exists(identityFilePath))
        {
            machineId = File.ReadAllText(identityFilePath).Trim();
        }
        else
        {
            machineId = Guid.NewGuid().ToString("N");
            File.WriteAllText(identityFilePath, machineId);
        }

        if (!Guid.TryParseExact(machineId, "N", out _))
        {
            machineId = Guid.NewGuid().ToString("N");
            File.WriteAllText(identityFilePath, machineId);
        }

        return new MachineIdentity(machineId, Environment.MachineName);
    }
}
