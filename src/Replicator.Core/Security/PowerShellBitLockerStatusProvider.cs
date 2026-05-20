using Replicator.Core.Execution;

namespace Replicator.Core.Security;

public sealed class PowerShellBitLockerStatusProvider(IProcessRunner processRunner) : IBitLockerStatusProvider
{
    public async Task<DriveSecurityItem> CheckAsync(
        string label,
        string path,
        string root,
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            return BitLockerQueryFailureClassifier.ToSecurityItem(label, path, root, "BitLocker status is only available on Windows.");
        }

        var mountPoint = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(mountPoint))
        {
            return BitLockerQueryFailureClassifier.ToSecurityItem(label, path, root, "Drive root is unavailable.");
        }

        var escapedMountPoint = mountPoint.Replace("'", "''");
        var command = string.Join(
            Environment.NewLine,
            [
                $"$volume = Get-BitLockerVolume -MountPoint '{escapedMountPoint}' -ErrorAction Stop",
                "[ordered]@{",
                "    MountPoint = $volume.MountPoint",
                "    VolumeStatus = $volume.VolumeStatus.ToString()",
                "    ProtectionStatus = $volume.ProtectionStatus.ToString()",
                "    LockStatus = $volume.LockStatus.ToString()",
                "    EncryptionPercentage = $volume.EncryptionPercentage",
                "} | ConvertTo-Json -Compress"
            ]);

        try
        {
            var result = await processRunner.RunAsync(
                "powershell.exe",
                [
                    "-NoProfile",
                    "-ExecutionPolicy",
                    "Bypass",
                    "-Command",
                    command
                ],
                cancellationToken);

            if (!result.Succeeded)
            {
                var message = FirstNonEmptyLine(result.StandardError) ?? FirstNonEmptyLine(result.StandardOutput) ?? "BitLocker status command failed.";
                return BitLockerQueryFailureClassifier.ToSecurityItem(label, path, root, message);
            }

            return BitLockerStatusParser.TryParseJson(result.StandardOutput, out var status)
                ? BitLockerStatusParser.ToSecurityItem(label, path, root, status)
                : BitLockerQueryFailureClassifier.ToSecurityItem(label, path, root, "BitLocker status output could not be parsed.");
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            return BitLockerQueryFailureClassifier.ToSecurityItem(label, path, root, exception.Message);
        }
    }

    private static string? FirstNonEmptyLine(string text)
    {
        return text
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
    }
}
