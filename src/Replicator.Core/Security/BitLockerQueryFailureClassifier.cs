namespace Replicator.Core.Security;

public static class BitLockerQueryFailureClassifier
{
    public static DriveSecurityItem ToSecurityItem(string label, string path, string root, string reason)
    {
        if (IsUnavailableMessage(reason))
        {
            return new DriveSecurityItem(
                label,
                path,
                root,
                DriveSecurityState.Unavailable,
                DriveSecuritySeverity.Error,
                $"Drive security: {label} is unavailable ({root}). {reason}");
        }

        if (IsPermissionDeniedMessage(reason))
        {
            return new DriveSecurityItem(
                label,
                path,
                root,
                DriveSecurityState.PermissionRequired,
                DriveSecuritySeverity.Warning,
                $"Drive security: {label} BitLocker status requires elevated permissions ({root}). Replicator can continue, but encryption state was not confirmed. Use Check as Admin to confirm.");
        }

        return new DriveSecurityItem(
            label,
            path,
            root,
            DriveSecurityState.Unknown,
            DriveSecuritySeverity.Warning,
            $"Drive security: {label} BitLocker status unknown ({root}). {reason}");
    }

    private static bool IsUnavailableMessage(string message)
    {
        return message.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("cannot find", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("does not exist", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPermissionDeniedMessage(string message)
    {
        return message.Contains("access denied", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("unauthorized", StringComparison.OrdinalIgnoreCase);
    }
}
