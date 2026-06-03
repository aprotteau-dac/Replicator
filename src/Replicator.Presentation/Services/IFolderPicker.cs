namespace Replicator.Presentation.Services;

public interface IFolderPicker
{
    Task<string?> PickFolderAsync(string initialPath, CancellationToken cancellationToken = default);
}
