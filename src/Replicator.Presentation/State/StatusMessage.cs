namespace Replicator.Presentation.State;

public sealed record StatusMessage(string Text, StatusKind Kind)
{
    public static StatusMessage Ready { get; } = new("Ready.", StatusKind.Success);
}
