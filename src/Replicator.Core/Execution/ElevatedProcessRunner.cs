using System.Diagnostics;

namespace Replicator.Core.Execution;

public sealed class ElevatedProcessRunner : IElevatedProcessRunner
{
    public async Task<int> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            UseShellExecute = true,
            Verb = "runas",
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start elevated process: {fileName}");

        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode;
    }
}
