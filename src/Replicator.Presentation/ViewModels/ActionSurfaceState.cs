using Replicator.Core.Models;
using Replicator.Core.Scheduling;

namespace Replicator.Presentation.ViewModels;

public sealed record ActionSurfaceState(
    bool HasProfile,
    bool ShowDeleteProfile,
    bool ShowSaveProfile,
    bool ShowGenerateScript,
    bool ShowInstallTask,
    bool ShowRunNow,
    bool ShowStartScheduledTask,
    bool ShowPreviewDryRun,
    bool ShowEnableTask,
    bool ShowDisableTask,
    bool ShowRemoveTask,
    bool ShowRefreshStatus,
    bool ShowCancelOperation,
    bool ShowPrepareShuttle,
    bool ShowDepartShuttle,
    bool ShowDockShuttle,
    bool ShowReceiveShuttle,
    bool ShowElevatedDriveSecurity,
    bool CanMutateProfile,
    bool CanCancelOperation,
    string InstallTaskLabel,
    string TaskSummaryText)
{
    public static ActionSurfaceState Empty { get; } = new(
        HasProfile: false,
        ShowDeleteProfile: false,
        ShowSaveProfile: false,
        ShowGenerateScript: false,
        ShowInstallTask: false,
        ShowRunNow: false,
        ShowStartScheduledTask: false,
        ShowPreviewDryRun: false,
        ShowEnableTask: false,
        ShowDisableTask: false,
        ShowRemoveTask: false,
        ShowRefreshStatus: false,
        ShowCancelOperation: false,
        ShowPrepareShuttle: false,
        ShowDepartShuttle: false,
        ShowDockShuttle: false,
        ShowReceiveShuttle: false,
        ShowElevatedDriveSecurity: false,
        CanMutateProfile: true,
        CanCancelOperation: false,
        InstallTaskLabel: "Install Task",
        TaskSummaryText: "No profile selected.");

    public static ActionSurfaceState From(
        BackupProfile? profile,
        ScheduledTaskSnapshot? snapshot,
        bool isBusy,
        bool hasCancelableOperation,
        bool driveSecurityRequiresElevation)
    {
        if (profile is null)
        {
            return Empty with
            {
                CanMutateProfile = !isBusy,
                ShowCancelOperation = isBusy && hasCancelableOperation,
                CanCancelOperation = isBusy && hasCancelableOperation
            };
        }

        var isBackupProfile = profile.Mode == ProfileMode.Backup;
        var isShuttleProfile = profile.Mode == ProfileMode.Shuttle;
        var supportsScheduledTask = isBackupProfile && profile.Schedule.Cadence != ScheduleCadence.Manual;
        var taskState = snapshot?.State ?? ScheduledTaskState.Missing;
        var taskNeedsRepair = snapshot?.NeedsRepair == true;
        var taskExists = supportsScheduledTask && taskState != ScheduledTaskState.Missing;
        var taskRunning = taskState == ScheduledTaskState.Running;
        var taskDisabled = taskState == ScheduledTaskState.Disabled;
        var taskCanStart = taskExists && !taskNeedsRepair && taskState is ScheduledTaskState.Ready or ScheduledTaskState.Unknown;

        var installLabel = taskNeedsRepair
            ? "Repair Task"
            : taskExists
                ? "Update Task"
                : "Install Task";
        var taskSummary = supportsScheduledTask
            ? snapshot is null
                ? $"{profile.Engine} | {profile.Target.Kind}"
                : taskNeedsRepair
                    ? $"Needs repair | {string.Join(" ", snapshot.RepairReasons)}"
                    : $"{snapshot.State} | {profile.Engine} | {profile.Target.Kind}"
            : isShuttleProfile
                ? $"{profile.Engine} | shuttle mode"
                : $"{profile.Engine} | manual schedule";

        return new ActionSurfaceState(
            HasProfile: true,
            ShowDeleteProfile: true,
            ShowSaveProfile: true,
            ShowGenerateScript: isBackupProfile,
            ShowInstallTask: supportsScheduledTask && !taskRunning,
            ShowRunNow: isBackupProfile,
            ShowStartScheduledTask: taskCanStart,
            ShowPreviewDryRun: isBackupProfile,
            ShowEnableTask: taskExists && taskDisabled,
            ShowDisableTask: taskExists && taskState == ScheduledTaskState.Ready,
            ShowRemoveTask: taskExists && !taskRunning,
            ShowRefreshStatus: supportsScheduledTask,
            ShowCancelOperation: isBusy && hasCancelableOperation,
            ShowPrepareShuttle: isShuttleProfile,
            ShowDepartShuttle: isShuttleProfile,
            ShowDockShuttle: isShuttleProfile,
            ShowReceiveShuttle: isShuttleProfile,
            ShowElevatedDriveSecurity: driveSecurityRequiresElevation,
            CanMutateProfile: !isBusy,
            CanCancelOperation: isBusy && hasCancelableOperation,
            InstallTaskLabel: installLabel,
            TaskSummaryText: taskSummary);
    }
}
