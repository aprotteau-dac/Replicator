using System.Text.Json;
using Replicator.Core.Models;

namespace Replicator.Core.Scripting;

public sealed class BackupRunStatusReader(string logsDirectory)
{
    public BackupRunStatus? ReadLatest(BackupProfile profile)
    {
        var statusPath = Path.Combine(logsDirectory, $"{PowerShellScriptGenerator.ProfileSlug(profile)}-latest.json");
        if (!File.Exists(statusPath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(statusPath);
            var status = JsonSerializer.Deserialize<BackupRunStatusDto>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (status is null)
            {
                return null;
            }

            return new BackupRunStatus(
                status.ProfileName ?? string.Empty,
                status.Mode ?? string.Empty,
                status.Source ?? string.Empty,
                status.Destination ?? string.Empty,
                status.LogPath ?? string.Empty,
                status.StartedAt,
                status.UpdatedAt,
                status.ExitCode,
                status.Succeeded,
                status.Message ?? string.Empty);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed class BackupRunStatusDto
    {
        public string? ProfileName { get; init; }

        public string? Mode { get; init; }

        public string? Source { get; init; }

        public string? Destination { get; init; }

        public string? LogPath { get; init; }

        public DateTimeOffset? StartedAt { get; init; }

        public DateTimeOffset? UpdatedAt { get; init; }

        public int? ExitCode { get; init; }

        public bool Succeeded { get; init; }

        public string? Message { get; init; }
    }
}
