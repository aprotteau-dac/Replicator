using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Replicator.Core.Models;

namespace Replicator.Core.Shuttle;

public sealed class ShuttleService(MachineIdentity machineIdentity)
{
    private static readonly TimeSpan MetadataTimestampTolerance = TimeSpan.FromSeconds(2);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<ShuttleOperationResult> PrepareAsync(
        BackupProfile profile,
        IProgress<ShuttleOperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateShuttleProfile(profile);
        if (validation is not null)
        {
            return validation;
        }

        cancellationToken.ThrowIfCancellationRequested();

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

        var previousPrepareManifest = await ReadStateManifestAsync(paths, $"latest-prepare-{machineIdentity.MachineId}.json", cancellationToken);
        var previousEntries = CreateEntryLookup(previousPrepareManifest?.Entries ?? []);

        ReportProgress(progress, ShuttleOperationKind.Prepare, 0, 0, "Scanning source files...");
        var sourceFiles = EnumerateSourceFiles(profile, cancellationToken).ToList();
        var manifest = CreateManifest(profile, paths, ShuttleOperationKind.Prepare, readyToDock: false, startedAt);
        var details = new List<string>();
        var processedFiles = 0;

        ReportProgress(progress, ShuttleOperationKind.Prepare, 0, sourceFiles.Count, "Preparing shuttle payload...");

        foreach (var sourceFile in sourceFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            processedFiles++;
            manifest.TotalFiles++;
            var relativePath = NormalizeRelativePath(Path.GetRelativePath(profile.SourcePath, sourceFile));
            previousEntries.TryGetValue(relativePath, out var previousEntry);
            var sourceInfo = new FileInfo(sourceFile);
            var sourceEntry = TryReuseEntry(relativePath, sourceInfo, previousEntry);

            var payloadPath = CombineUnderRoot(paths.PayloadDirectory, relativePath);
            var payloadExists = File.Exists(payloadPath);
            var shouldCopy = !payloadExists;

            ReportFileProgress(
                progress,
                ShuttleOperationKind.Prepare,
                processedFiles,
                sourceFiles.Count,
                $"Indexing {processedFiles} of {sourceFiles.Count} files.");

            if (payloadExists)
            {
                sourceEntry ??= CreateFileEntry(relativePath, sourceFile, previousEntry: null);
                shouldCopy = !FileMatchesEntry(sourceEntry, payloadPath);
            }

            if (!shouldCopy)
            {
                manifest.Entries.Add(sourceEntry!);
                manifest.SkippedFiles++;
                ReportFileProgress(
                    progress,
                    ShuttleOperationKind.Prepare,
                    processedFiles,
                    sourceFiles.Count,
                    $"Checked {processedFiles} of {sourceFiles.Count} files.");
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
                sourceEntry = sourceEntry is null
                    ? CopyFileAndCreateEntry(relativePath, sourceFile, payloadPath)
                    : CopyFile(sourceEntry, sourceFile, payloadPath);
            }
            else
            {
                sourceEntry ??= CreateFileEntry(relativePath, sourceFile, previousEntry: null);
            }

            manifest.Entries.Add(sourceEntry);

            if (details.Count < 25)
            {
                details.Add($"{(profile.DryRun ? "Would stage" : "Staged")} {relativePath}");
            }

            ReportFileProgress(
                progress,
                ShuttleOperationKind.Prepare,
                processedFiles,
                sourceFiles.Count,
                $"{(profile.DryRun ? "Previewed" : "Staged")} {processedFiles} of {sourceFiles.Count} files.");
        }

        manifest.CompletedAt = DateTimeOffset.UtcNow;
        ReportProgress(progress, ShuttleOperationKind.Prepare, sourceFiles.Count, sourceFiles.Count, "Prepare Shuttle completed.");

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

    public async Task<ShuttleOperationResult> DepartAsync(
        BackupProfile profile,
        IProgress<ShuttleOperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateShuttleProfile(profile);
        if (validation is not null)
        {
            return validation;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var paths = ShuttlePaths.FromProfile(profile);
        var availability = CheckShuttleAvailability(paths);
        if (availability is not null)
        {
            return availability;
        }

        paths.EnsureCreated();
        ReportProgress(progress, ShuttleOperationKind.Depart, 0, 0, "Reading prepared shuttle payload...");

        var preparedManifest = await ReadStateManifestAsync(paths, $"latest-prepare-{machineIdentity.MachineId}.json", cancellationToken);
        if (preparedManifest is null)
        {
            return new ShuttleOperationResult(false, "No prepared shuttle payload was found. Run Prepare Shuttle first.", null);
        }

        var now = DateTimeOffset.UtcNow;
        var departManifest = CloneForOperation(preparedManifest, ShuttleOperationKind.Depart, readyToDock: true, now);

        await WriteManifestAsync(paths, departManifest, cancellationToken);
        await WriteStateManifestAsync(paths, "latest-depart.json", departManifest, cancellationToken);
        ReportProgress(progress, ShuttleOperationKind.Depart, 1, 1, "Depart completed.");

        return new ShuttleOperationResult(
            true,
            $"Departed from {machineIdentity.MachineName}. The shuttle is ready to dock on another machine.",
            departManifest);
    }

    public async Task<ShuttleOperationResult> DockAsync(
        BackupProfile profile,
        IProgress<ShuttleOperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateShuttleProfile(profile);
        if (validation is not null)
        {
            return validation;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var paths = ShuttlePaths.FromProfile(profile);
        var availability = CheckShuttleAvailability(paths);
        if (availability is not null)
        {
            return availability;
        }

        var inboundManifest = await ReadLatestReadyToDockManifestAsync(paths, cancellationToken);
        if (inboundManifest is null ||
            inboundManifest.FromMachineId == machineIdentity.MachineId ||
            IsReceived(paths, inboundManifest))
        {
            return new ShuttleOperationResult(true, "No inbound shuttle changes are waiting for this machine.", inboundManifest);
        }

        var analysis = AnalyzeInbound(profile, paths, inboundManifest, progress, cancellationToken);
        return new ShuttleOperationResult(
            true,
            $"Docked shuttle from {inboundManifest.FromMachineName}. Review the inbound summary before receiving changes.",
            analysis,
            BuildInboundDetails(analysis));
    }

    public async Task<ShuttleOperationResult> ReceiveAsync(
        BackupProfile profile,
        IProgress<ShuttleOperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateShuttleProfile(profile);
        if (validation is not null)
        {
            return validation;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var paths = ShuttlePaths.FromProfile(profile);
        var availability = CheckShuttleAvailability(paths);
        if (availability is not null)
        {
            return availability;
        }

        var inboundManifest = await ReadLatestReadyToDockManifestAsync(paths, cancellationToken);
        if (inboundManifest is null ||
            inboundManifest.FromMachineId == machineIdentity.MachineId ||
            IsReceived(paths, inboundManifest))
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
        var payloadEntries = GetPayloadEntries(paths, inboundManifest, cancellationToken);
        if (inboundManifest.Entries.Count == 0 && payloadEntries.Count > 0)
        {
            receiveManifest.Warnings.Add("Inbound manifest has no file index; fell back to payload scan.");
        }

        var processedFiles = 0;

        ReportProgress(progress, ShuttleOperationKind.Receive, 0, payloadEntries.Count, "Receiving shuttle payload...");

        foreach (var entry in payloadEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            processedFiles++;
            receiveManifest.TotalFiles++;
            receiveManifest.Entries.Add(entry);

            var relativePath = entry.RelativePath;
            var payloadFile = CombineUnderRoot(paths.PayloadDirectory, relativePath);
            if (!File.Exists(payloadFile))
            {
                receiveManifest.Warnings.Add($"Payload file is missing: {relativePath}");
                ReportFileProgress(progress, ShuttleOperationKind.Receive, processedFiles, payloadEntries.Count, $"Checked {processedFiles} of {payloadEntries.Count} files.");
                continue;
            }

            var localPath = CombineUnderRoot(profile.SourcePath, relativePath);

            if (!File.Exists(localPath))
            {
                receiveManifest.NewFiles++;
                receiveManifest.CopiedFiles++;
                CopyFile(payloadFile, localPath);
                AddDetail(details, $"Received new {relativePath}");
                ReportFileProgress(progress, ShuttleOperationKind.Receive, processedFiles, payloadEntries.Count, $"Received {processedFiles} of {payloadEntries.Count} files.");
                continue;
            }

            if (FileMatchesEntry(entry, localPath))
            {
                receiveManifest.SkippedFiles++;
                ReportFileProgress(progress, ShuttleOperationKind.Receive, processedFiles, payloadEntries.Count, $"Checked {processedFiles} of {payloadEntries.Count} files.");
                continue;
            }

            receiveManifest.ChangedFiles++;
            receiveManifest.ConflictFiles++;
            receiveManifest.CopiedFiles++;

            var conflictPath = Path.Combine(conflictRoot, relativePath);
            CopyFile(localPath, conflictPath);
            CopyFile(payloadFile, localPath);
            AddDetail(details, $"Received changed {relativePath}; preserved local copy under conflicts.");
            ReportFileProgress(progress, ShuttleOperationKind.Receive, processedFiles, payloadEntries.Count, $"Received {processedFiles} of {payloadEntries.Count} files.");
        }

        receiveManifest.CompletedAt = DateTimeOffset.UtcNow;
        await WriteManifestAsync(paths, receiveManifest, cancellationToken);
        await File.WriteAllTextAsync(
            ReceivedMarkerPath(paths, inboundManifest),
            DateTimeOffset.UtcNow.ToString("O"),
            cancellationToken);
        ReportProgress(progress, ShuttleOperationKind.Receive, payloadEntries.Count, payloadEntries.Count, "Receive completed.");

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

    private ShuttleManifest AnalyzeInbound(
        BackupProfile profile,
        ShuttlePaths paths,
        ShuttleManifest inboundManifest,
        IProgress<ShuttleOperationProgress>? progress,
        CancellationToken cancellationToken)
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

        var payloadEntries = GetPayloadEntries(paths, inboundManifest, cancellationToken);
        if (inboundManifest.Entries.Count == 0 && payloadEntries.Count > 0)
        {
            manifest.Warnings.Add("Inbound manifest has no file index; fell back to payload scan.");
        }

        var processedFiles = 0;

        ReportProgress(progress, ShuttleOperationKind.Dock, 0, payloadEntries.Count, "Analyzing inbound shuttle payload...");

        foreach (var entry in payloadEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            processedFiles++;
            manifest.TotalFiles++;
            manifest.Entries.Add(entry);

            var relativePath = entry.RelativePath;
            var payloadPath = CombineUnderRoot(paths.PayloadDirectory, relativePath);
            if (!File.Exists(payloadPath))
            {
                manifest.Warnings.Add($"Payload file is missing: {relativePath}");
                ReportFileProgress(progress, ShuttleOperationKind.Dock, processedFiles, payloadEntries.Count, $"Analyzed {processedFiles} of {payloadEntries.Count} files.");
                continue;
            }

            var localPath = CombineUnderRoot(profile.SourcePath, relativePath);

            if (!File.Exists(localPath))
            {
                manifest.NewFiles++;
            }
            else if (!FileMatchesEntry(entry, localPath))
            {
                manifest.ChangedFiles++;
                manifest.ConflictFiles++;
            }
            else
            {
                manifest.SkippedFiles++;
            }

            ReportFileProgress(progress, ShuttleOperationKind.Dock, processedFiles, payloadEntries.Count, $"Analyzed {processedFiles} of {payloadEntries.Count} files.");
        }

        manifest.CompletedAt = DateTimeOffset.UtcNow;
        ReportProgress(progress, ShuttleOperationKind.Dock, payloadEntries.Count, payloadEntries.Count, "Dock analysis completed.");
        return manifest;
    }

    private IEnumerable<string> EnumerateSourceFiles(BackupProfile profile, CancellationToken cancellationToken)
    {
        var sourceRoot = Path.GetFullPath(Environment.ExpandEnvironmentVariables(profile.SourcePath));
        foreach (var path in EnumerateFiles(sourceRoot, cancellationToken))
        {
            var relativePath = Path.GetRelativePath(sourceRoot, path);
            if (!IsExcluded(relativePath, profile.ExcludePatterns))
            {
                yield return path;
            }
        }
    }

    private static IEnumerable<string> EnumerateFiles(string root, CancellationToken cancellationToken)
    {
        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return path;
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

    private static IReadOnlyList<ShuttleManifestEntry> GetPayloadEntries(
        ShuttlePaths paths,
        ShuttleManifest inboundManifest,
        CancellationToken cancellationToken)
    {
        if (inboundManifest.Entries.Count > 0)
        {
            return inboundManifest.Entries;
        }

        if (!Directory.Exists(paths.PayloadDirectory))
        {
            return [];
        }

        return EnumerateFiles(paths.PayloadDirectory, cancellationToken)
            .Select(payloadFile =>
            {
                var relativePath = NormalizeRelativePath(Path.GetRelativePath(paths.PayloadDirectory, payloadFile));
                return CreateFileEntry(relativePath, payloadFile, previousEntry: null);
            })
            .ToList();
    }

    private static Dictionary<string, ShuttleManifestEntry> CreateEntryLookup(IEnumerable<ShuttleManifestEntry> entries)
    {
        var lookup = new Dictionary<string, ShuttleManifestEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            if (!string.IsNullOrWhiteSpace(entry.RelativePath))
            {
                lookup[NormalizeRelativePath(entry.RelativePath)] = entry;
            }
        }

        return lookup;
    }

    private static ShuttleManifestEntry CreateFileEntry(
        string relativePath,
        string filePath,
        ShuttleManifestEntry? previousEntry)
    {
        var info = new FileInfo(filePath);
        if (!info.Exists)
        {
            throw new FileNotFoundException("Cannot create a shuttle manifest entry for a missing file.", filePath);
        }

        var normalizedRelativePath = NormalizeRelativePath(relativePath);
        var sha256 = previousEntry is not null &&
                     EntryMetadataMatchesFile(previousEntry, info) &&
                     !string.IsNullOrWhiteSpace(previousEntry.Sha256)
            ? previousEntry.Sha256
            : HashFile(filePath);

        return new ShuttleManifestEntry
        {
            RelativePath = normalizedRelativePath,
            Length = info.Length,
            LastWriteTimeUtc = info.LastWriteTimeUtc,
            Sha256 = sha256
        };
    }

    private static ShuttleManifestEntry? TryReuseEntry(
        string relativePath,
        FileInfo file,
        ShuttleManifestEntry? previousEntry)
    {
        if (previousEntry is null ||
            string.IsNullOrWhiteSpace(previousEntry.Sha256) ||
            !EntryMetadataMatchesFile(previousEntry, file))
        {
            return null;
        }

        return new ShuttleManifestEntry
        {
            RelativePath = NormalizeRelativePath(relativePath),
            Length = file.Length,
            LastWriteTimeUtc = file.LastWriteTimeUtc,
            Sha256 = previousEntry.Sha256
        };
    }

    private static bool EntryMetadataMatchesFile(ShuttleManifestEntry entry, FileInfo file)
    {
        return file.Exists &&
               file.Length == entry.Length &&
               TimestampsMatch(file.LastWriteTimeUtc, entry.LastWriteTimeUtc);
    }

    private static bool FileMatchesEntry(ShuttleManifestEntry entry, string filePath)
    {
        var file = new FileInfo(filePath);
        if (!file.Exists || file.Length != entry.Length)
        {
            return false;
        }

        if (TimestampsMatch(file.LastWriteTimeUtc, entry.LastWriteTimeUtc))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(entry.Sha256) &&
               HashFile(filePath).Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase);
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static bool TimestampsMatch(DateTime first, DateTime second)
    {
        return (first - second).Duration() <= MetadataTimestampTolerance;
    }

    private static string NormalizeRelativePath(string relativePath)
    {
        return relativePath
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');
    }

    private static string CombineUnderRoot(string root, string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
        {
            throw new InvalidOperationException($"Shuttle manifest path must be relative: {relativePath}");
        }

        var fullRoot = Path.GetFullPath(root);
        var localRelativePath = relativePath.Replace('/', Path.DirectorySeparatorChar);
        var candidate = Path.GetFullPath(Path.Combine(fullRoot, localRelativePath));
        var rootWithSeparator = Path.EndsInDirectorySeparator(fullRoot)
            ? fullRoot
            : fullRoot + Path.DirectorySeparatorChar;

        if (!candidate.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Shuttle manifest path escapes the managed root: {relativePath}");
        }

        return candidate;
    }

    private static ShuttleManifestEntry CopyFile(
        ShuttleManifestEntry entry,
        string sourcePath,
        string destinationPath)
    {
        CopyFile(sourcePath, destinationPath);
        return entry;
    }

    private static void CopyFile(string sourcePath, string destinationPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        File.Copy(sourcePath, destinationPath, overwrite: true);
        File.SetLastWriteTimeUtc(destinationPath, File.GetLastWriteTimeUtc(sourcePath));
    }

    private static ShuttleManifestEntry CopyFileAndCreateEntry(
        string relativePath,
        string sourcePath,
        string destinationPath)
    {
        var sourceInfo = new FileInfo(sourcePath);
        if (!sourceInfo.Exists)
        {
            throw new FileNotFoundException("Cannot stage a missing shuttle source file.", sourcePath);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using (var source = File.OpenRead(sourcePath))
        using (var destination = File.Create(destinationPath))
        {
            var buffer = new byte[1024 * 128];
            int read;
            while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                destination.Write(buffer.AsSpan(0, read));
                hash.AppendData(buffer, 0, read);
            }
        }

        File.SetLastWriteTimeUtc(destinationPath, sourceInfo.LastWriteTimeUtc);

        return new ShuttleManifestEntry
        {
            RelativePath = NormalizeRelativePath(relativePath),
            Length = sourceInfo.Length,
            LastWriteTimeUtc = sourceInfo.LastWriteTimeUtc,
            Sha256 = Convert.ToHexString(hash.GetHashAndReset())
        };
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
        var latestDepart = await ReadStateManifestAsync(paths, "latest-depart.json", cancellationToken);
        if (latestDepart?.ReadyToDock == true)
        {
            return latestDepart;
        }

        if (!Directory.Exists(paths.ManifestsDirectory))
        {
            return null;
        }

        var manifests = new List<ShuttleManifest>();
        foreach (var path in Directory.EnumerateFiles(paths.ManifestsDirectory, "*.json"))
        {
            cancellationToken.ThrowIfCancellationRequested();

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
            SchemaVersion = source.SchemaVersion,
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
            Entries = source.Entries
                .Select(entry => new ShuttleManifestEntry
                {
                    RelativePath = entry.RelativePath,
                    Length = entry.Length,
                    LastWriteTimeUtc = entry.LastWriteTimeUtc,
                    Sha256 = entry.Sha256
                })
                .ToList(),
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

    private static void ReportProgress(
        IProgress<ShuttleOperationProgress>? progress,
        ShuttleOperationKind operation,
        int processedFiles,
        int totalFiles,
        string message)
    {
        progress?.Report(new ShuttleOperationProgress(operation, processedFiles, totalFiles, message));
    }

    private static void ReportFileProgress(
        IProgress<ShuttleOperationProgress>? progress,
        ShuttleOperationKind operation,
        int processedFiles,
        int totalFiles,
        string message)
    {
        if (totalFiles <= 50 || processedFiles == 1 || processedFiles == totalFiles || processedFiles % 25 == 0)
        {
            ReportProgress(progress, operation, processedFiles, totalFiles, message);
        }
    }
}
