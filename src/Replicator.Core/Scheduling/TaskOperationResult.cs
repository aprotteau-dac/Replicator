namespace Replicator.Core.Scheduling;

public sealed record TaskOperationResult(bool Succeeded, string Message, string Output = "");
