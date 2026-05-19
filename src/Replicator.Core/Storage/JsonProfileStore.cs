using System.Text.Json;
using System.Text.Json.Serialization;
using Replicator.Core.Models;

namespace Replicator.Core.Storage;

public sealed class JsonProfileStore(string filePath) : IProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<IReadOnlyList<BackupProfile>> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            return [];
        }

        await using var stream = File.OpenRead(filePath);
        var profiles = await JsonSerializer.DeserializeAsync<List<BackupProfile>>(stream, JsonOptions, cancellationToken);

        return profiles?
            .OrderBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];
    }

    public async Task SaveAllAsync(IEnumerable<BackupProfile> profiles, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var orderedProfiles = profiles
            .OrderBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        await using var stream = File.Create(filePath);
        await JsonSerializer.SerializeAsync(stream, orderedProfiles, JsonOptions, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    public async Task UpsertAsync(BackupProfile profile, CancellationToken cancellationToken = default)
    {
        var profiles = (await LoadAsync(cancellationToken)).ToList();
        var index = profiles.FindIndex(existing => existing.Id == profile.Id);

        profile.UpdatedAt = DateTimeOffset.UtcNow;

        if (index >= 0)
        {
            profiles[index] = profile;
        }
        else
        {
            if (profile.CreatedAt == default)
            {
                profile.CreatedAt = DateTimeOffset.UtcNow;
            }

            profiles.Add(profile);
        }

        await SaveAllAsync(profiles, cancellationToken);
    }

    public async Task DeleteAsync(Guid profileId, CancellationToken cancellationToken = default)
    {
        var profiles = (await LoadAsync(cancellationToken))
            .Where(profile => profile.Id != profileId)
            .ToList();

        await SaveAllAsync(profiles, cancellationToken);
    }
}
