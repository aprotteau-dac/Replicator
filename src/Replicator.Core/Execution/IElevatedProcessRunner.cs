namespace Replicator.Core.Execution;

public interface IElevatedProcessRunner
{
    Task<int> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        CancellationToken cancellationToken = default);
}
