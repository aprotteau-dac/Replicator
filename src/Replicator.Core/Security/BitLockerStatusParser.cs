using System.Text.Json;

namespace Replicator.Core.Security;

public static class BitLockerStatusParser
{
    public static bool TryParseJson(string output, out BitLockerVolumeStatus status)
    {
        status = null!;

        if (string.IsNullOrWhiteSpace(output))
        {
            return false;
        }

        var jsonStart = output.IndexOf('{');
        var jsonEnd = output.LastIndexOf('}');
        if (jsonStart < 0 || jsonEnd <= jsonStart)
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(output[jsonStart..(jsonEnd + 1)]);
            var root = document.RootElement;

            status = new BitLockerVolumeStatus(
                ReadString(root, "MountPoint"),
                ReadString(root, "VolumeStatus"),
                ReadString(root, "ProtectionStatus"),
                ReadString(root, "LockStatus"),
                ReadNullableDouble(root, "EncryptionPercentage"));
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static DriveSecurityItem ToSecurityItem(string label, string path, string root, BitLockerVolumeStatus status)
    {
        if (status.LockStatus.Equals("Locked", StringComparison.OrdinalIgnoreCase))
        {
            return new DriveSecurityItem(
                label,
                path,
                root,
                DriveSecurityState.Locked,
                DriveSecuritySeverity.Error,
                $"Drive security: {label} is BitLocker locked ({root}).");
        }

        if (status.ProtectionStatus.Equals("On", StringComparison.OrdinalIgnoreCase))
        {
            return new DriveSecurityItem(
                label,
                path,
                root,
                DriveSecurityState.Protected,
                DriveSecuritySeverity.Info,
                $"Drive security: {label} is BitLocker protected ({root}).");
        }

        if (status.ProtectionStatus.Equals("Off", StringComparison.OrdinalIgnoreCase) ||
            status.VolumeStatus.Equals("FullyDecrypted", StringComparison.OrdinalIgnoreCase))
        {
            return new DriveSecurityItem(
                label,
                path,
                root,
                DriveSecurityState.Unprotected,
                DriveSecuritySeverity.Warning,
                $"Drive security: {label} is not BitLocker protected ({root}).");
        }

        return new DriveSecurityItem(
            label,
            path,
            root,
            DriveSecurityState.Unknown,
            DriveSecuritySeverity.Warning,
            $"Drive security: {label} BitLocker state is unknown ({root}).");
    }

    private static string ReadString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var property) && property.ValueKind != JsonValueKind.Null
            ? property.ToString()
            : "";
    }

    private static double? ReadNullableDouble(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property) || property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return property.TryGetDouble(out var value) ? value : null;
    }
}
