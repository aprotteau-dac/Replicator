namespace Replicator.Core.Execution;

public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Succeeded => ExitCode == 0;
}
