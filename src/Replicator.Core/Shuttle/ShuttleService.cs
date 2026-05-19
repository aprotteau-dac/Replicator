using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Replicator.Core.Models;

namespace Replicator.Core.Shuttle;

public sealed class ShuttleService(MachineIdentity machineIdentity)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<ShuttleOperationResult> PrepareAsync(BackupProfile profile, CancellationToken cancellationToken = default)
    {
        var validation = ValidateShuttleProfile(profile);
        if (validation is not null)
        {
            return validation;
        }

        var startedAt = DateTimeOffset.UtcNow;
        var paths = ShuttlePaths.FromProfile(profile);
        var availability = CheckShuttleAvailability(paths);
        if (availability is not null)
        {
            return availability;
        }

        paths.EnsureCreated();

        var inboundManifest = await ReadLatestReadyToDockManifestAsync(paths, cancellationToken);
        if (inboundManifest is not null &&
            inboundManifest.FromMachineId != machineIdentity.MachineId &&
            !IsReceived(paths, inboundManifest))
        {
            return new ShuttleOperationResult(
                false,
                $"Inbound shuttle from {inboundManifest.FromMachineName} is waiting. Dock and receive it before preparing outbound changes.",
                inboundManifest);
        }

        if (!Directory.Exists(profile.SourcePath))
        {
            return new ShuttleOperationResult(false, $"Source path is unavailable: {profile.SourcePath}", null);
        }

        var manifest = CreateManifest(profile, paths, ShuttleOperationKind.Prepare, readyToDock: false, startedAt);
        var details = new List<string>();

        foreach (var sourceFile in EnumerateSourceFiles(profile))
        {
            cancellationToken.ThrowIfCancellationRequested();

            manifest.TotalFiles++;
            var relativePath = Path.GetRelativePath(profile.SourcePath, sourceFile);
            var payloadPath = Path.Combine(paths.PayloadDirectory, relativePath);
            var payloadExists = File.Exists(payloadPath);
            var shouldCopy = !payloadExists || !FilesMatch(sourceFile, payloadPath);

            if (!shouldCopy)
            {
                manifest.SkippedFiles++;
                continue;
            }

            manifest.CopiedFiles++;
            if (!payloadExists)
            {
                manifest.NewFiles++;
            }
            else
            {
                manifest.ChangedFiles++;
            }

            if (!profile.DryRun)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(payloadPath)!);
                File.Copy(sourceFile, payloadPath, overwrite: true);
            }

            if (details.Count < 25)
            {
                details.Add($"{(profile.DryRun ? "Would stage" : "Staged")} {relativePath}");
            }
        }

        manifest.CompletedAt = DateTimeOffset.UtcNow;

        if (profile.DryRun)
        {
            return new ShuttleOperationResult(
                true,
                "Prepare Shuttle dry run completed. Uncheck Dry run to write the shuttle payload.",
                manifest,
                string.Join(Environment.NewLine, details));
        }

        await WriteManifestAsync(paths, manifest, cancellationToken);
        await WriteStateManifestAsync(paths, $"latest-prepare-{machineIdentity.MachineId}.json", manifest, cancellationToken);

        return new ShuttleOperationResult(
            true,
            "Shuttle prepared. Use Depart to mark it ready for the next machine.",
            manifest,
            string.Join(Environment.NewLine, details));
    }

    public async Task<ShuttleOperationResult> DepartAsync(BackupProfile profile, CancellationToken cancellationToken = default)
    {
        var validation = ValidateShuttleProfile(profile);
        if (validation is not null)
        {
            return validation;
        }

        var paths = ShuttlePaths.FromProfile(profile);
        var availability = CheckShuttleAvailability(paths);
        if (availability is not null)
        {
            return availability;
        }

        paths.EnsureCreated();

        var preparedManifest = await ReadStateManifestAsync(paths, $"latest-prepare-{machineIdentity.MachineId}.json", cancellationToken);
        if (preparedManifest is null)
        {
            return new ShuttleOperationResult(false, "No prepared shuttle payload was found. Run Prepare Shuttle first.", null);
        }

        var now = DateTimeOffset.UtcNow;
        var departManifest = CloneForOperation(preparedManifest, ShuttleOperationKind.Depart, readyToDock: true, now);

        await WriteManifestAsync(paths, departManifest, cancellationToken);
        await WriteStateManifestAsync(paths, "latest-depart.json", departManifest, cancellationToken);

        return new ShuttleOperationResult(
            true,
            $"Departed from {machineIdentity.MachineName}. The shuttle is ready to dock on another machine.",
            departManifest);
    }

    public async Task<ShuttleOperationResult> DockAsync(BackupProfile profile, CancellationToken cancellationToken = default)
    {
        var validation = ValidateShuttleProfile(profile);
        if (validation is not null)
        {
            return validation;
        }

        var paths = ShuttlePaths.FromProfile(profile);
        var availability = CheckShuttleAvailability(paths);
        if (availability is not null)
        {
            return availability;
        }

        var inboundManifest = await ReadLatestReadyToDockManifestAsync(paths, cancellationToken);
        if (inboundManifest is null || inboundManifest.FromMachineId == machineIdentity.MachineId)
        {
            return new ShuttleOperationResult(true, "No inbound shuttle changes are waiting for this machine.", inboundManifest);
        }

        var analysis = AnalyzeInbound(profile, paths, inboundManifest);
        return new ShuttleOperationResult(
            true,
            $"Docked shuttle from {inboundManifest.FromMachineName}. Review the inbound summary before receiving changes.",
            analysis,
            BuildInboundDetails(analysis));
    }

    public async Task<ShuttleOperationResult> ReceiveAsync(BackupProfile profile, CancellationToken cancellationToken = default)
    {
        var validation = ValidateShuttleProfile(profile);
        if (validation is not null)
        {
            return validation;
        }

        var paths = ShuttlePaths.FromProfile(profile);
        var availability = CheckShuttleAvailability(paths);
        if (availability is not null)
        {
            return availability;
        }

        var inboundManifest = await ReadLatestReadyToDockManifestAsync(paths, cancellationToken);
        if (inboundManifest is null || inboundManifest.FromMachineId == machineIdentity.MachineId)
        {
            return new ShuttleOperationResult(true, "No inbound shuttle changes are waiting for this machine.", inboundManifest);
        }

        paths.EnsureCreated();
        Directory.CreateDirectory(profile.SourcePath);

        var startedAt = DateTimeOffset.UtcNow;
        var receiveManifest = CreateManifest(profile, paths, ShuttleOperationKind.Receive, readyToDock: false, startedAt);
        receiveManifest.FromMachineId = inboundManifest.FromMachineId;
        receiveManifest.FromMachineName = inboundManifest.FromMachineName;
        receiveManifest.ToMachineId = machineIdentity.MachineId;
        receiveManifest.ToMachineName = machineIdentity.MachineName;

        var conflictRoot = Path.Combine(paths.ConflictsDirectory, DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss"));
        var details = new List<string>();

        foreach (var payloadFile in Directory.EnumerateFiles(paths.PayloadDirectory, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            receiveManifest.TotalFiles++;
            var relativePath = Path.GetRelativePath(paths.PayloadDirectory, payloadFile);
            var localPath = Path.Combine(profile.SourcePath, relativePath);

            if (!File.Exists(localPath))
            {
                receiveManifest.NewFiles++;
                receiveManifest.CopiedFiles++;
                CopyFile(payloadFile, localPath);
                AddDetail(details, $"Received new {relativePath}");
                continue;
            }

            if (FilesMatch(payloadFile, localPath))
            {
                receiveManifest.SkippedFiles++;
                continue;
            }

            receiveManifest.ChangedFiles++;
            receiveManifest.ConflictFiles++;
            receiveManifest.CopiedFiles++;

            var conflictPath = Path.Combine(conflictRoot, relativePath);
            CopyFile(localPath, conflictPath);
            CopyFile(payloadFile, localPath);
            AddDetail(details, $"Received changed {relativePath}; preserved local copy under conflicts.");
        }

        receiveManifest.CompletedAt = DateTimeOffset.UtcNow;
        await WriteManifestAsync(paths, receiveManifest, cancellationToken);
        await File.WriteAllTextAsync(
            ReceivedMarkerPath(paths, inboundManifest),
            DateTimeOffset.UtcNow.ToString("O"),
            cancellationToken);

        return new ShuttleOperationResult(
            true,
            "Inbound shuttle changes received. Local conflicting files were preserved before overwrite.",
            receiveManifest,
            string.Join(Environment.NewLine, details));
    }

    private static ShuttleOperationResult? ValidateShuttleProfile(BackupProfile profile)
    {
        if (profile.Mode != ProfileMode.Shuttle)
        {
            return new ShuttleOperationResult(false, "Selected profile is not a shuttle profile.", null);
        }

        if (string.IsNullOrWhiteSpace(profile.SourcePath))
        {
            return new ShuttleOperationResult(false, "Source path is required.", null);
        }

        if (string.IsNullOrWhiteSpace(profile.Target.Path))
        {
            return new ShuttleOperationResult(false, "Shuttle path is required.", null);
        }

        return null;
    }

    private static ShuttleOperationResult? CheckShuttleAvailability(ShuttlePaths paths)
    {
        var root = Path.GetPathRoot(paths.PairRoot);
        if (!string.IsNullOrWhiteSpace(root) && !Directory.Exists(root))
        {
            return new ShuttleOperationResult(false, $"Shuttle drive is unavailable: {root}", null);
        }

        return null;
    }

    private ShuttleManifest CreateManifest(
        BackupProfile profile,
        ShuttlePaths paths,
        ShuttleOperationKind operation,
        bool readyToDock,
        DateTimeOffset startedAt)
    {
        var driveInfo = GetDriveInfo(paths.PairRoot);

        return new ShuttleManifest
        {
            ProfileId = profile.Id,
            ProfileName = profile.Name,
            Operation = operation,
            ReadyToDock = readyToDock,
            FromMachineId = machineIdentity.MachineId,
            FromMachineName = machineIdentity.MachineName,
            SourcePath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(profile.SourcePath)),
            ShuttlePath = paths.PairRoot,
            PayloadPath = paths.PayloadDirectory,
            DriveRoot = driveInfo?.RootDirectory.FullName ?? "",
            DriveLabel = driveInfo?.VolumeLabel ?? "",
            StartedAt = startedAt,
            CompletedAt = startedAt
        };
    }

    private ShuttleManifest AnalyzeInbound(BackupProfile profile, ShuttlePaths paths, ShuttleManifest inboundManifest)
    {
        var manifest = CreateManifest(profile, paths, ShuttleOperationKind.Dock, readyToDock: inboundManifest.ReadyToDock, DateTimeOffset.UtcNow);
        manifest.FromMachineId = inboundManifest.FromMachineId;
        manifest.FromMachineName = inboundManifest.FromMachineName;
        manifest.ToMachineId = machineIdentity.MachineId;
        manifest.ToMachineName = machineIdentity.MachineName;

        if (!Directory.Exists(paths.PayloadDirectory))
        {
            manifest.Warnings.Add("Shuttle payload directory is missing.");
            return manifest;
        }

        foreach (var payloadFile in Directory.EnumerateFiles(paths.PayloadDirectory, "*", SearchOption.AllDirectories))
        {
            manifest.TotalFiles++;
            var relativePath = Path.GetRelativePath(paths.PayloadDirectory, payloadFile);
            var localPath = Path.Combine(profile.SourcePath, relativePath);

            if (!File.Exists(localPath))
            {
                manifest.NewFiles++;
            }
            else if (!FilesMatch(payloadFile, localPath))
            {
                manifest.ChangedFiles++;
                manifest.ConflictFiles++;
            }
            else
            {
                manifest.SkippedFiles++;
            }
        }

        manifest.CompletedAt = DateTimeOffset.UtcNow;
        return manifest;
    }

    private IEnumerable<string> EnumerateSourceFiles(BackupProfile profile)
    {
        var sourceRoot = Path.GetFullPath(Environment.ExpandEnvironmentVariables(profile.SourcePath));
        foreach (var path in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceRoot, path);
            if (!IsExcluded(relativePath, profile.ExcludePatterns))
            {
                yield return path;
            }
        }
    }

    private static bool IsExcluded(string relativePath, IEnumerable<string> excludePatterns)
    {
        var segments = relativePath.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        var fileName = Path.GetFileName(relativePath);

        foreach (var rawPattern in excludePatterns.Append(".replicator-conflicts"))
        {
            var pattern = rawPattern.Trim();
            if (string.IsNullOrWhiteSpace(pattern))
            {
                continue;
            }

            if (fileName.Equals(pattern, StringComparison.OrdinalIgnoreCase) ||
                segments.Any(segment => segment.Equals(pattern, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool FilesMatch(string firstPath, string secondPath)
    {
        var first = new FileInfo(firstPath);
        var second = new FileInfo(secondPath);
        if (!first.Exists || !second.Exists || first.Length != second.Length)
        {
            return false;
        }

        return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(firstPath)))
            .Equals(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(secondPath))), StringComparison.OrdinalIgnoreCase);
    }

    private static void CopyFile(string sourcePath, string destinationPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        File.Copy(sourcePath, destinationPath, overwrite: true);
    }

    private async Task WriteManifestAsync(ShuttlePaths paths, ShuttleManifest manifest, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(paths.ManifestsDirectory);
        var fileName = $"{manifest.CompletedAt:yyyyMMdd-HHmmss}-{SafeSegment(manifest.FromMachineName)}-{manifest.Operation.ToString().ToLowerInvariant()}-{manifest.ManifestId:N}.json";
        var manifestPath = Path.Combine(paths.ManifestsDirectory, fileName);
        await WriteJsonAsync(manifestPath, manifest, cancellationToken);
    }

    private static async Task WriteStateManifestAsync(
        ShuttlePaths paths,
        string fileName,
        ShuttleManifest manifest,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(paths.StateDirectory);
        await WriteJsonAsync(Path.Combine(paths.StateDirectory, fileName), manifest, cancellationToken);
    }

    private static async Task<ShuttleManifest?> ReadStateManifestAsync(
        ShuttlePaths paths,
        string fileName,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(paths.StateDirectory, fileName);
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<ShuttleManifest>(stream, JsonOptions, cancellationToken);
    }

    private static async Task WriteJsonAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken);
    }

    private static async Task<ShuttleManifest?> ReadLatestReadyToDockManifestAsync(
        ShuttlePaths paths,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(paths.ManifestsDirectory))
        {
            return null;
        }

        var manifests = new List<ShuttleManifest>();
        foreach (var path in Directory.EnumerateFiles(paths.ManifestsDirectory, "*.json"))
        {
            await using var stream = File.OpenRead(path);
            var manifest = await JsonSerializer.DeserializeAsync<ShuttleManifest>(stream, JsonOptions, cancellationToken);
            if (manifest?.ReadyToDock == true)
            {
                manifests.Add(manifest);
            }
        }

        return manifests
            .OrderByDescending(manifest => manifest.CompletedAt)
            .FirstOrDefault();
    }

    private static bool IsReceived(ShuttlePaths paths, ShuttleManifest manifest)
    {
        return File.Exists(ReceivedMarkerPath(paths, manifest));
    }

    private static string ReceivedMarkerPath(ShuttlePaths paths, ShuttleManifest manifest)
    {
        return Path.Combine(paths.StateDirectory, $"received-{manifest.ManifestId:N}.txt");
    }

    private static DriveInfo? GetDriveInfo(string path)
    {
        var root = Path.GetPathRoot(path);
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return null;
        }

        return new DriveInfo(root);
    }

    private static ShuttleManifest CloneForOperation(
        ShuttleManifest source,
        ShuttleOperationKind operation,
        bool readyToDock,
        DateTimeOffset timestamp)
    {
        return new ShuttleManifest
        {
            ProfileId = source.ProfileId,
            ProfileName = source.ProfileName,
            Operation = operation,
            ReadyToDock = readyToDock,
            FromMachineId = source.FromMachineId,
            FromMachineName = source.FromMachineName,
            SourcePath = source.SourcePath,
            ShuttlePath = source.ShuttlePath,
            PayloadPath = source.PayloadPath,
            DriveRoot = source.DriveRoot,
            DriveLabel = source.DriveLabel,
            StartedAt = timestamp,
            CompletedAt = timestamp,
            TotalFiles = source.TotalFiles,
            CopiedFiles = source.CopiedFiles,
            SkippedFiles = source.SkippedFiles,
            NewFiles = source.NewFiles,
            ChangedFiles = source.ChangedFiles,
            ConflictFiles = source.ConflictFiles,
            Warnings = [.. source.Warnings]
        };
    }

    private static string BuildInboundDetails(ShuttleManifest manifest)
    {
        var lines = new List<string>
        {
            $"From: {manifest.FromMachineName}",
            $"Payload: {manifest.PayloadPath}",
            $"New files: {manifest.NewFiles}",
            $"Changed files: {manifest.ChangedFiles}",
            $"Potential conflicts preserved on receive: {manifest.ConflictFiles}"
        };

        if (manifest.Warnings.Count > 0)
        {
            lines.AddRange(manifest.Warnings.Select(warning => $"Warning: {warning}"));
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static void AddDetail(List<string> details, string detail)
    {
        if (details.Count < 25)
        {
            details.Add(detail);
        }
    }

    private static string SafeSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var safe = new string(value.Select(character => invalid.Contains(character) ? '-' : character).ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "machine" : safe;
    }
}
