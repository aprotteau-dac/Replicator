using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Replicator.Core.Execution;

namespace Replicator.Core.Security;

public sealed class ElevatedPowerShellBitLockerStatusProvider(
    IElevatedProcessRunner processRunner,
    string? workingDirectory = null,
    TimeSpan? processTimeout = null) : IBitLockerStatusProvider, IBitLockerBatchStatusProvider
{
    private static readonly TimeSpan DefaultProcessTimeout = TimeSpan.FromMinutes(2);

    private readonly string _workingDirectory = workingDirectory ?? Path.Combine(Path.GetTempPath(), "Replicator", "security");
    private readonly TimeSpan _processTimeout = processTimeout ?? DefaultProcessTimeout;

    public async Task<DriveSecurityItem> CheckAsync(
        string label,
        string path,
        string root,
        CancellationToken cancellationToken = default)
    {
        var results = await CheckAsync([new DriveSecurityCandidate(Guid.Empty, label, path, root)], cancellationToken);
        return results.TryGetValue(root, out var item)
            ? item
            : BitLockerQueryFailureClassifier.ToSecurityItem(label, path, root, "Elevated BitLocker status check did not return a result.");
    }

    public async Task<IReadOnlyDictionary<string, DriveSecurityItem>> CheckAsync(
        IReadOnlyList<DriveSecurityCandidate> candidates,
        CancellationToken cancellationToken = default)
    {
        var uniqueCandidates = candidates
            .GroupBy(candidate => candidate.Root, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        if (!OperatingSystem.IsWindows())
        {
            return uniqueCandidates.ToDictionary(
                candidate => candidate.Root,
                candidate => BitLockerQueryFailureClassifier.ToSecurityItem(candidate.Label, candidate.Path, candidate.Root, "BitLocker status is only available on Windows."),
                StringComparer.OrdinalIgnoreCase);
        }

        var invalidMountPoints = uniqueCandidates
            .Where(candidate => string.IsNullOrWhiteSpace(candidate.Root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)))
            .ToList();
        if (invalidMountPoints.Count > 0)
        {
            return invalidMountPoints.ToDictionary(
                candidate => candidate.Root,
                candidate => BitLockerQueryFailureClassifier.ToSecurityItem(candidate.Label, candidate.Path, candidate.Root, "Drive root is unavailable."),
                StringComparer.OrdinalIgnoreCase);
        }

        Directory.CreateDirectory(_workingDirectory);
        var checkId = Guid.NewGuid().ToString("N");
        var requestsPath = Path.Combine(_workingDirectory, $"bitlocker-check-{checkId}-requests.json");
        var outputPath = Path.Combine(_workingDirectory, $"bitlocker-check-{checkId}.json");

        try
        {
            await File.WriteAllTextAsync(requestsPath, BuildRequestsJson(uniqueCandidates), Encoding.UTF8, cancellationToken);

            int exitCode;
            using (var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeoutSource.CancelAfter(_processTimeout);
                try
                {
                    exitCode = await processRunner.RunAsync(
                        WindowsPowerShellPath(),
                        [
                            "-NoProfile",
                            "-NonInteractive",
                            "-WindowStyle",
                            "Hidden",
                            "-ExecutionPolicy",
                            "Bypass",
                            "-EncodedCommand",
                            BuildEncodedCommand(requestsPath, outputPath)
                        ],
                        timeoutSource.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeoutSource.IsCancellationRequested)
                {
                    return TimeoutResults(uniqueCandidates);
                }
            }

            if (!File.Exists(outputPath))
            {
                return uniqueCandidates.ToDictionary(
                    candidate => candidate.Root,
                    candidate => BitLockerQueryFailureClassifier.ToSecurityItem(
                        candidate.Label,
                        candidate.Path,
                        candidate.Root,
                        $"Elevated BitLocker status check failed with exit code {exitCode}."),
                    StringComparer.OrdinalIgnoreCase);
            }

            var output = await File.ReadAllTextAsync(outputPath, cancellationToken);
            return ParseOutput(uniqueCandidates, output);
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            return uniqueCandidates.ToDictionary(
                candidate => candidate.Root,
                candidate => new DriveSecurityItem(
                    candidate.Label,
                    candidate.Path,
                    candidate.Root,
                    DriveSecurityState.PermissionRequired,
                    DriveSecuritySeverity.Warning,
                    $"Drive security: {candidate.Label} administrator check was canceled ({candidate.Root}). Replicator can continue, but encryption state was not confirmed."),
                StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or IOException or JsonException)
        {
            return uniqueCandidates.ToDictionary(
                candidate => candidate.Root,
                candidate => BitLockerQueryFailureClassifier.ToSecurityItem(candidate.Label, candidate.Path, candidate.Root, exception.Message),
                StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(requestsPath);
            TryDelete(outputPath);
        }
    }

    private IReadOnlyDictionary<string, DriveSecurityItem> TimeoutResults(IReadOnlyList<DriveSecurityCandidate> candidates)
    {
        var seconds = Math.Max(1, (int)Math.Ceiling(_processTimeout.TotalSeconds));
        return candidates.ToDictionary(
            candidate => candidate.Root,
            candidate => BitLockerQueryFailureClassifier.ToSecurityItem(
                candidate.Label,
                candidate.Path,
                candidate.Root,
                $"Elevated BitLocker status check timed out after {seconds} second(s)."),
            StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyDictionary<string, DriveSecurityItem> ParseOutput(
        IReadOnlyList<DriveSecurityCandidate> candidates,
        string output)
    {
        using var document = JsonDocument.Parse(output);
        var payloads = document.RootElement.ValueKind == JsonValueKind.Array
            ? document.RootElement.EnumerateArray().ToList()
            : [document.RootElement.Clone()];
        var candidateByRoot = candidates.ToDictionary(candidate => candidate.Root, StringComparer.OrdinalIgnoreCase);
        var results = new Dictionary<string, DriveSecurityItem>(StringComparer.OrdinalIgnoreCase);

        foreach (var payload in payloads)
        {
            var root = ReadString(payload, "Root") ?? candidates.FirstOrDefault()?.Root ?? "";
            if (!candidateByRoot.TryGetValue(root, out var candidate))
            {
                continue;
            }

            results[root] = ParseOutput(candidate.Label, candidate.Path, candidate.Root, payload);
        }

        foreach (var candidate in candidates)
        {
            if (!results.ContainsKey(candidate.Root))
            {
                results[candidate.Root] = BitLockerQueryFailureClassifier.ToSecurityItem(
                    candidate.Label,
                    candidate.Path,
                    candidate.Root,
                    "Elevated BitLocker status check did not return a result.");
            }
        }

        return results;
    }

    private static DriveSecurityItem ParseOutput(string label, string path, string root, JsonElement payload)
    {
        if (!ReadBool(payload, "Succeeded"))
        {
            var error = ReadString(payload, "Error") ?? "Elevated BitLocker status check failed.";
            return BitLockerQueryFailureClassifier.ToSecurityItem(label, path, root, error);
        }

        var mountPoint = ReadString(payload, "MountPoint");
        var volumeStatus = ReadString(payload, "VolumeStatus");
        var protectionStatus = ReadString(payload, "ProtectionStatus");
        var lockStatus = ReadString(payload, "LockStatus");

        if (string.IsNullOrWhiteSpace(mountPoint) ||
            string.IsNullOrWhiteSpace(volumeStatus) ||
            string.IsNullOrWhiteSpace(protectionStatus) ||
            string.IsNullOrWhiteSpace(lockStatus))
        {
            return BitLockerQueryFailureClassifier.ToSecurityItem(label, path, root, "Elevated BitLocker status output could not be parsed.");
        }

        var status = new BitLockerVolumeStatus(
            mountPoint,
            volumeStatus,
            protectionStatus,
            lockStatus,
            ReadNullableDouble(payload, "EncryptionPercentage"));

        return BitLockerStatusParser.ToSecurityItem(label, path, root, status);
    }

    private static string BuildScript()
    {
        return """
            param(
                [Parameter(Mandatory = $true)]
                [string]$RequestsPath,

                [Parameter(Mandatory = $true)]
                [string]$OutputPath
            )

            $ErrorActionPreference = 'Stop'

            function Write-FailureResult([string]$ErrorMessage) {
                [ordered]@{
                    Succeeded = $false
                    Error = $ErrorMessage
                } | ConvertTo-Json -Compress -Depth 4 | Set-Content -LiteralPath $OutputPath -Encoding UTF8
                exit 1
            }

            try {
                $RequestsJson = Get-Content -LiteralPath $RequestsPath -Raw -ErrorAction Stop
                $requests = @($RequestsJson | ConvertFrom-Json -ErrorAction Stop | ForEach-Object { $_ })
                if ($requests.Count -eq 0) {
                    throw 'Elevated BitLocker status check could not start because no drive requests were supplied.'
                }

                $results = foreach ($request in $requests) {
                    try {
                        $mountPoint = $request.Root.TrimEnd('\', '/')
                        $volume = Get-BitLockerVolume -MountPoint $mountPoint -ErrorAction Stop
                        [ordered]@{
                            Root = $request.Root
                            Succeeded = $true
                            MountPoint = $volume.MountPoint
                            VolumeStatus = $volume.VolumeStatus.ToString()
                            ProtectionStatus = $volume.ProtectionStatus.ToString()
                            LockStatus = $volume.LockStatus.ToString()
                            EncryptionPercentage = $volume.EncryptionPercentage
                        }
                    }
                    catch {
                        [ordered]@{
                            Root = $request.Root
                            Succeeded = $false
                            Error = $_.Exception.Message
                        }
                    }
                }

                $results | ConvertTo-Json -Compress -Depth 4 | Set-Content -LiteralPath $OutputPath -Encoding UTF8
                if ($results | Where-Object { -not $_.Succeeded }) {
                    exit 1
                }
                exit 0
            }
            catch {
                Write-FailureResult "Elevated BitLocker status check could not start. $($_.Exception.Message)"
            }
            """;
    }

    private static string BuildEncodedCommand(string requestsPath, string outputPath)
    {
        var command = string.Join(
            Environment.NewLine,
            [
                "$helper = @'",
                BuildScript(),
                "'@",
                $"& ([scriptblock]::Create($helper)) -RequestsPath {PowerShellSingleQuoted(requestsPath)} -OutputPath {PowerShellSingleQuoted(outputPath)}",
                "exit $LASTEXITCODE"
            ]);
        return Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
    }

    private static string BuildRequestsJson(IEnumerable<DriveSecurityCandidate> candidates)
    {
        var requests = candidates.Select(candidate => new
        {
            candidate.Root
        });

        return JsonSerializer.Serialize(requests);
    }

    private static string PowerShellSingleQuoted(string value)
    {
        return $"'{value.Replace("'", "''")}'";
    }

    private static string WindowsPowerShellPath()
    {
        var windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        return string.IsNullOrWhiteSpace(windowsDirectory)
            ? "powershell.exe"
            : Path.Combine(windowsDirectory, "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
    }

    private static bool ReadBool(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.True;
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : property.ToString();
    }

    private static double? ReadNullableDouble(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return property.TryGetDouble(out var value) ? value : null;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
