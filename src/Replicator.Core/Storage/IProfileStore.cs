using Replicator.Core.Models;

namespace Replicator.Core.Storage;

public interface IProfileStore
{
    Task<IReadOnlyList<BackupProfile>> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAllAsync(IEnumerable<BackupProfile> profiles, CancellationToken cancellationToken = default);

    Task UpsertAsync(BackupProfile profile, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid profileId, CancellationToken cancellationToken = default);
}
