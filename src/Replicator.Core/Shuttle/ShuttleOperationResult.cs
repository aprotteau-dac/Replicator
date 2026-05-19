namespace Replicator.Core.Shuttle;

public sealed record ShuttleOperationResult(
    bool Succeeded,
    string Message,
    ShuttleManifest? Manifest,
    string Details = "")
{
    public string ToDisplayString()
    {
        if (Manifest is null)
        {
            return Message;
        }

        return $"{Message}{Environment.NewLine}" +
               $"Files: total {Manifest.TotalFiles}, copied {Manifest.CopiedFiles}, skipped {Manifest.SkippedFiles}, new {Manifest.NewFiles}, changed {Manifest.ChangedFiles}, conflicts {Manifest.ConflictFiles}.{Environment.NewLine}" +
               Details;
    }
}
