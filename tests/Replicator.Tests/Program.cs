using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Xml.Linq;
using Replicator.Core;
using Replicator.Core.Execution;
using Replicator.Core.Availability;
using Replicator.Core.Models;
using Replicator.Core.Scheduling;
using Replicator.Core.Scripting;
using Replicator.Core.Security;
using Replicator.Core.Shuttle;
using Replicator.Core.Storage;
using Replicator.Presentation.Commands;
using Replicator.Presentation.State;

var tests = new List<(string Name, Func<Task> Test)>
{
    ("validator rejects destinations under the source tree", ValidatorRejectsNestedDestination),
    ("script generator emits robocopy dry-run script", ScriptGeneratorEmitsDryRunScript),
    ("script generator emits target preflight status", ScriptGeneratorEmitsTargetPreflightStatus),
    ("script generator writes hidden scheduled task launcher", ScriptGeneratorWritesHiddenScheduledTaskLauncher),
    ("log reader summarizes latest robocopy log", LogReaderSummarizesLatestRobocopyLog),
    ("log reader skips locked latest robocopy log", LogReaderSkipsLockedLatestRobocopyLog),
    ("status reader parses latest backup status", StatusReaderParsesLatestBackupStatus),
    ("status reader ignores malformed latest backup status", StatusReaderIgnoresMalformedLatestBackupStatus),
    ("relay command executes when allowed", RelayCommandExecutesWhenAllowed),
    ("async command disables while executing", AsyncCommandDisablesWhileExecuting),
    ("status message ready is success", StatusMessageReadyIsSuccess),
    ("profile form loads and applies backup profile edits", ProfileFormLoadsAndAppliesBackupProfileEdits),
    ("profile form rejects invalid start time", ProfileFormRejectsInvalidStartTime),
    ("profile form rejects invalid interval", ProfileFormRejectsInvalidInterval),
    ("action surface hides scheduled task commands for manual backup profiles", ActionSurfaceHidesScheduledTaskCommandsForManualBackupProfiles),
    ("action surface exposes scheduled task commands for ready daily backup profiles", ActionSurfaceExposesScheduledTaskCommandsForReadyDailyBackupProfiles),
    ("action surface exposes shuttle commands and hides backup run commands", ActionSurfaceExposesShuttleCommandsAndHidesBackupRunCommands),
    ("action surface disables profile mutation while busy", ActionSurfaceDisablesProfileMutationWhileBusy),
    ("task inventory distinguishes action center from review visibility", TaskInventoryDistinguishesActionCenterFromReviewVisibility),
    ("task inventory shows action center for unselected issues", TaskInventoryShowsActionCenterForUnselectedIssues),
    ("main window load command loads saved profiles", MainWindowLoadCommandLoadsSavedProfiles),
    ("main window view model loads saved profiles and selects first profile", MainWindowViewModelLoadsSavedProfilesAndSelectsFirstProfile),
    ("main window view model creates and selects default profile", MainWindowViewModelCreatesAndSelectsDefaultProfile),
    ("delete profile cancel leaves profiles unchanged", DeleteProfileCancelLeavesProfilesUnchanged),
    ("delete profile confirm deletes profile and scheduled task", DeleteProfileConfirmDeletesProfileAndScheduledTask),
    ("browse source cancel leaves source path unchanged", BrowseSourceCancelLeavesSourcePathUnchanged),
    ("browse destination applies selected folder", BrowseDestinationAppliesSelectedFolder),
    ("save profile applies form and persists selected profile", SaveProfileAppliesFormAndPersistsSelectedProfile),
    ("generate script applies form persists profile and appends generated script path", GenerateScriptAppliesFormPersistsProfileAndAppendsGeneratedScriptPath),
    ("install task writes script installs task and refreshes inventory", InstallTaskWritesScriptInstallsTaskAndRefreshesInventory),
    ("preview dry run applies form and invokes powershell with dry run switch", PreviewDryRunAppliesFormAndInvokesPowerShellWithDryRunSwitch),
    ("run now applies form and invokes powershell without dry run switch", RunNowAppliesFormAndInvokesPowerShellWithoutDryRunSwitch),
    ("enable task calls scheduled service and refreshes inventory", EnableTaskCallsScheduledServiceAndRefreshesInventory),
    ("disable task calls scheduled service and refreshes inventory", DisableTaskCallsScheduledServiceAndRefreshesInventory),
    ("remove task calls scheduled service and refreshes inventory", RemoveTaskCallsScheduledServiceAndRefreshesInventory),
    ("start scheduled task reports missing task without running", StartScheduledTaskReportsMissingTaskWithoutRunning),
    ("start scheduled task reports repair needed without running", StartScheduledTaskReportsRepairNeededWithoutRunning),
    ("refresh status updates task fields and action surface", RefreshStatusUpdatesTaskFieldsAndActionSurface),
    ("review task inventory opens rows or reports none", ReviewTaskInventoryOpensRowsOrReportsNone),
    ("repair selected inventory task installs profile task", RepairSelectedInventoryTaskInstallsProfileTask),
    ("main window view model marks drive security permission required for elevation", MainWindowViewModelMarksDriveSecurityPermissionRequiredForElevation),
    ("check drive security as admin refreshes selected profile elevation state", CheckDriveSecurityAsAdminRefreshesSelectedProfileElevationState),
    ("prepare shuttle command runs dry run and reports result", PrepareShuttleCommandRunsDryRunAndReportsResult),
    ("depart shuttle command marks prepared payload ready", DepartShuttleCommandMarksPreparedPayloadReady),
    ("dock shuttle command reports no inbound changes", DockShuttleCommandReportsNoInboundChanges),
    ("receive shuttle command reports no inbound changes", ReceiveShuttleCommandReportsNoInboundChanges),
    ("busy operation disables mutation and restores action surface", BusyOperationDisablesMutationAndRestoresActionSurface),
    ("shuttle operation is busy cancellable and blocks profile mutation", ShuttleOperationIsBusyCancellableAndBlocksProfileMutation),
    ("cancel operation command is disabled when idle", CancelOperationCommandIsDisabledWhenIdle),
    ("profile store round-trips JSON", ProfileStoreRoundTripsJson),
    ("shuttle prepare depart dock receive preserves conflicts", ShuttlePrepareDepartDockReceivePreservesConflicts),
    ("shuttle source enumeration prunes excluded directories", ShuttleSourceEnumerationPrunesExcludedDirectories),
    ("shuttle prepare preserves timestamps for fast skip analysis", ShuttlePreparePreservesTimestampsForFastSkipAnalysis),
    ("shuttle dock compares drifted local files against manifest hash", ShuttleDockComparesDriftedLocalFilesAgainstManifestHash),
    ("shuttle prepare reports file progress", ShuttlePrepareReportsFileProgress),
    ("shuttle prepare reports progress for skipped files", ShuttlePrepareReportsProgressForSkippedFiles),
    ("shuttle operations honor cancellation", ShuttleOperationsHonorCancellation),
    ("shuttle prepare blocks missing source before creating shuttle directories", ShuttlePrepareBlocksMissingSourceBeforeCreatingShuttleDirectories),
    ("shuttle receive blocks missing source without creating it", ShuttleReceiveBlocksMissingSourceWithoutCreatingIt),
    ("shuttle prepare expands environment variable source path", ShuttlePrepareExpandsEnvironmentVariableSourcePath),
    ("shuttle metadata operations block missing source", ShuttleMetadataOperationsBlockMissingSource),
    ("scheduled task names are deterministic and scoped", ScheduledTaskNamesAreScoped),
    ("scheduled task action uses hidden windowless launcher", ScheduledTaskActionUsesHiddenWindowlessLauncher),
    ("scheduled task action inspector flags visible powershell actions", ScheduledTaskActionInspectorFlagsVisiblePowerShellActions),
    ("scheduled task action inspector flags console powershell actions even when hidden", ScheduledTaskActionInspectorFlagsConsolePowerShellActionsEvenWhenHidden),
    ("scheduled task action inspector flags mismatched script paths", ScheduledTaskActionInspectorFlagsMismatchedScriptPaths),
    ("scheduled task query reports repair reasons", ScheduledTaskQueryReportsRepairReasons),
    ("scheduled task inventory classifies matched repair and orphaned tasks", ScheduledTaskInventoryClassifiesMatchedRepairAndOrphanedTasks),
    ("scheduled task inventory disables repair for running tasks", ScheduledTaskInventoryDisablesRepairForRunningTasks),
    ("scheduled task issue selector ignores other profile repairs", ScheduledTaskIssueSelectorIgnoresOtherProfileRepairs),
    ("scheduled task inventory reports query failure as unknown", ScheduledTaskInventoryReportsQueryFailureAsUnknown),
    ("main window status text is layout bounded", MainWindowStatusTextIsLayoutBounded),
    ("minute schedules emit schtasks minute cadence", MinuteSchedulesEmitSchtasksMinuteCadence),
    ("default profile carries local development excludes", DefaultProfileHasDevelopmentExcludes),
    ("validator rejects invalid minute interval", ValidatorRejectsInvalidMinuteInterval),
    ("availability checker reports missing source and creatable target", AvailabilityCheckerReportsMissingSourceAndCreatableTarget),
    ("availability checker reports unavailable drive", AvailabilityCheckerReportsUnavailableDrive),
    ("bitlocker parser classifies protected unprotected and locked drives", BitLockerParserClassifiesProtectedUnprotectedAndLockedDrives),
    ("bitlocker access denied is classified as permission required", BitLockerAccessDeniedIsClassifiedAsPermissionRequired),
    ("powershell bitlocker provider maps access denied to permission required", PowerShellBitLockerProviderMapsAccessDeniedToPermissionRequired),
    ("drive security report treats permission required as a warning", DriveSecurityReportTreatsPermissionRequiredAsWarning),
    ("drive security report marks permission required checks as elevation ready", DriveSecurityReportMarksPermissionRequiredChecksAsElevationReady),
    ("drive security cache warms unique roots across profiles", DriveSecurityCacheWarmsUniqueRootsAcrossProfiles),
    ("drive security cache refreshes selected profile roots only", DriveSecurityCacheRefreshesSelectedProfileRootsOnly),
    ("drive security cache preserves unknown check failure reason", DriveSecurityCachePreservesUnknownCheckFailureReason),
    ("elevated bitlocker provider launches encoded admin helper and parses result file", ElevatedBitLockerProviderLaunchesEncodedAdminHelperAndParsesResultFile),
    ("elevated bitlocker encoded command runs helper script", ElevatedBitLockerEncodedCommandRunsHelperScript),
    ("elevated bitlocker provider batches multiple roots into one admin launch", ElevatedBitLockerProviderBatchesMultipleRootsIntoOneAdminLaunch),
    ("elevated bitlocker helper script enumerates Windows PowerShell JSON requests", ElevatedBitLockerHelperScriptEnumeratesWindowsPowerShellJsonRequests),
    ("elevated bitlocker helper script writes output for startup failures", ElevatedBitLockerHelperScriptWritesOutputForStartupFailures),
    ("elevated bitlocker provider times out hung admin helper", ElevatedBitLockerProviderTimesOutHungAdminHelper),
    ("elevated bitlocker provider treats canceled admin prompt as permission required", ElevatedBitLockerProviderTreatsCanceledAdminPromptAsPermissionRequired),
    ("profile drive security checker summarizes bitlocker posture", ProfileDriveSecurityCheckerSummarizesBitLockerPosture)
};

if (Environment.GetEnvironmentVariable("REPLICATOR_LONG_SHUTTLE_SMOKE") == "1")
{
    tests.Add(("long shuttle manifest smoke handles 6500 skipped files", ShuttleLongManifestSmokeHandles6500SkippedFiles));
}

var failures = 0;

foreach (var (name, test) in tests)
{
    try
    {
        await test();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception exception)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {name}");
        Console.Error.WriteLine(exception);
    }
}

if (failures > 0)
{
    Console.Error.WriteLine($"{failures} test(s) failed.");
    return 1;
}

Console.WriteLine($"{tests.Count} test(s) passed.");
return 0;

static Task RelayCommandExecutesWhenAllowed()
{
    var executed = false;
    var command = new RelayCommand(() => executed = true, () => true);

    Assert(command.CanExecute(null), "Expected command to be executable.");
    command.Execute(null);

    Assert(executed, "Expected relay command to execute action.");
    return Task.CompletedTask;
}

static async Task AsyncCommandDisablesWhileExecuting()
{
    var release = new TaskCompletionSource();
    var command = new AsyncCommand(async () => await release.Task);

    var execution = command.ExecuteAsync();

    Assert(!command.CanExecute(null), "Expected async command to disable while executing.");
    release.SetResult();
    await execution;
    Assert(command.CanExecute(null), "Expected async command to re-enable after execution.");
}

static Task StatusMessageReadyIsSuccess()
{
    Assert(StatusMessage.Ready.Text == "Ready.", $"Unexpected ready text: {StatusMessage.Ready.Text}");
    Assert(StatusMessage.Ready.Kind == StatusKind.Success, $"Unexpected ready kind: {StatusMessage.Ready.Kind}");
    return Task.CompletedTask;
}

static Task ProfileFormLoadsAndAppliesBackupProfileEdits()
{
    var profile = ValidProfile();
    var form = new Replicator.Presentation.ViewModels.ProfileFormViewModel();

    form.Load(profile);
    form.Name = "Daily Scratch";
    form.SourcePath = @"C:\work\daily";
    form.DestinationPath = @"D:\backups\daily";
    form.TimeText = "06:30";
    form.IntervalText = "15";
    form.Cadence = ScheduleCadence.Minutes;
    form.DryRun = false;

    var result = form.TryApply(profile);

    Assert(result.Succeeded, result.Message);
    Assert(profile.Name == "Daily Scratch", $"Unexpected name: {profile.Name}");
    Assert(profile.SourcePath == @"C:\work\daily", $"Unexpected source: {profile.SourcePath}");
    Assert(profile.Target.Path == @"D:\backups\daily", $"Unexpected target: {profile.Target.Path}");
    Assert(profile.Schedule.TimeOfDay == new TimeOnly(6, 30), $"Unexpected time: {profile.Schedule.TimeOfDay}");
    Assert(profile.Schedule.IntervalMinutes == 15, $"Unexpected minute interval: {profile.Schedule.IntervalMinutes}");
    Assert(!profile.DryRun, "Expected dry run to be disabled.");
    return Task.CompletedTask;
}

static Task ProfileFormRejectsInvalidStartTime()
{
    var profile = ValidProfile();
    var form = new Replicator.Presentation.ViewModels.ProfileFormViewModel();
    form.Load(profile);
    form.TimeText = "25:99";

    var result = form.TryApply(profile);

    Assert(!result.Succeeded, "Expected invalid time to fail.");
    Assert(result.Message == "Start time must be a valid time.", $"Unexpected message: {result.Message}");
    return Task.CompletedTask;
}

static Task ProfileFormRejectsInvalidInterval()
{
    var profile = ValidProfile();
    var form = new Replicator.Presentation.ViewModels.ProfileFormViewModel();
    form.Load(profile);
    form.IntervalText = "every hour";

    var result = form.TryApply(profile);

    Assert(!result.Succeeded, "Expected invalid interval to fail.");
    Assert(result.Message == "Interval must be a number.", $"Unexpected message: {result.Message}");
    return Task.CompletedTask;
}

static Task ActionSurfaceHidesScheduledTaskCommandsForManualBackupProfiles()
{
    var profile = ValidProfile();
    profile.Schedule.Cadence = ScheduleCadence.Manual;

    var state = Replicator.Presentation.ViewModels.ActionSurfaceState.From(
        profile,
        snapshot: null,
        isBusy: false,
        hasCancelableOperation: false,
        driveSecurityRequiresElevation: false);

    Assert(state.ShowRunNow, "Manual backup profiles should still allow Run Now.");
    Assert(!state.ShowInstallTask, "Manual backup profiles should not show Install Task.");
    Assert(!state.ShowRefreshStatus, "Manual backup profiles should not show Refresh Status.");
    Assert(state.TaskSummaryText.Contains("manual schedule", StringComparison.OrdinalIgnoreCase), $"Unexpected summary: {state.TaskSummaryText}");
    return Task.CompletedTask;
}

static Task ActionSurfaceExposesScheduledTaskCommandsForReadyDailyBackupProfiles()
{
    var profile = ValidProfile();
    profile.Schedule.Cadence = ScheduleCadence.Daily;
    var snapshot = new ScheduledTaskSnapshot(
        ScheduledTaskName.ForProfile(profile),
        ScheduledTaskState.Ready,
        "6/3/2026 6:00:00 PM",
        "6/2/2026 6:00:00 PM",
        0,
        "");

    var state = Replicator.Presentation.ViewModels.ActionSurfaceState.From(
        profile,
        snapshot,
        isBusy: false,
        hasCancelableOperation: false,
        driveSecurityRequiresElevation: false);

    Assert(state.ShowInstallTask, "Daily backup profile should show update/install task command.");
    Assert(state.InstallTaskLabel == "Update Task", $"Unexpected install label: {state.InstallTaskLabel}");
    Assert(state.ShowStartScheduledTask, "Ready scheduled task should be startable.");
    Assert(state.ShowDisableTask, "Ready scheduled task should be disableable.");
    Assert(state.ShowRefreshStatus, "Scheduled profile should expose refresh status.");
    return Task.CompletedTask;
}

static Task ActionSurfaceExposesShuttleCommandsAndHidesBackupRunCommands()
{
    var profile = ValidProfile();
    profile.Mode = ProfileMode.Shuttle;

    var state = Replicator.Presentation.ViewModels.ActionSurfaceState.From(
        profile,
        snapshot: null,
        isBusy: false,
        hasCancelableOperation: false,
        driveSecurityRequiresElevation: false);

    Assert(!state.ShowRunNow, "Shuttle profiles should hide Run Now.");
    Assert(!state.ShowGenerateScript, "Shuttle profiles should hide Generate Script.");
    Assert(state.ShowPrepareShuttle, "Shuttle profiles should expose Prepare Shuttle.");
    Assert(state.ShowDepartShuttle, "Shuttle profiles should expose Depart.");
    Assert(state.ShowDockShuttle, "Shuttle profiles should expose Dock Shuttle.");
    Assert(state.ShowReceiveShuttle, "Shuttle profiles should expose Receive Changes.");
    return Task.CompletedTask;
}

static Task ActionSurfaceDisablesProfileMutationWhileBusy()
{
    var profile = ValidProfile();

    var state = Replicator.Presentation.ViewModels.ActionSurfaceState.From(
        profile,
        snapshot: null,
        isBusy: true,
        hasCancelableOperation: true,
        driveSecurityRequiresElevation: true);

    Assert(!state.CanMutateProfile, "Busy state should disable profile mutation.");
    Assert(state.ShowCancelOperation, "Cancelable busy operation should show Cancel.");
    Assert(state.CanCancelOperation, "Cancelable busy operation should enable Cancel.");
    Assert(state.ShowElevatedDriveSecurity, "Drive security elevation prompt should remain visible while required.");
    return Task.CompletedTask;
}

static Task TaskInventoryDistinguishesActionCenterFromReviewVisibility()
{
    var profile = ValidProfile();
    var item = new ScheduledTaskInventoryItem(
        ScheduledTaskName.ForProfile(profile),
        profile.Id,
        profile.Name,
        ScheduledTaskInventoryState.NeedsRepair,
        ScheduledTaskState.Ready,
        "6/3/2026 6:00:00 PM",
        "6/2/2026 6:00:00 PM",
        0,
        "powershell.exe",
        @"C:\Replicator\profile.ps1",
        true,
        ["Visible PowerShell action"],
        "Visible PowerShell action",
        "");
    var inventory = new ScheduledTaskInventoryResult(
        [item],
        new ScheduledTaskInventorySummary(Total: 1, Ready: 0, NeedsRepair: 1, Orphaned: 0, Running: 0, Unknown: 0),
        "");
    var viewModel = new Replicator.Presentation.ViewModels.TaskInventoryViewModel();

    viewModel.Apply(inventory, selectedIssue: item);

    Assert(viewModel.IsActionCenterVisible, "Selected repair issue should show the Action Center.");
    Assert(!viewModel.IsReviewOpen, "Applying inventory should not automatically open the review surface.");

    viewModel.OpenReview();

    Assert(viewModel.IsReviewOpen, "Review surface should open when inventory rows exist.");
    return Task.CompletedTask;
}

static Task TaskInventoryShowsActionCenterForUnselectedIssues()
{
    var orphan = new ScheduledTaskInventoryItem(
        @"\Replicator\Old-Profile",
        ProfileId: null,
        ProfileName: "",
        ScheduledTaskInventoryState.Orphaned,
        ScheduledTaskState.Ready,
        "6/3/2026 6:00:00 PM",
        "6/2/2026 6:00:00 PM",
        0,
        "wscript.exe",
        @"C:\Replicator\orphan.vbs",
        ScriptExists: false,
        RepairReasons: [],
        "Orphaned Replicator scheduled task.",
        "");
    var inventory = new ScheduledTaskInventoryResult(
        [orphan],
        new ScheduledTaskInventorySummary(Total: 1, Ready: 0, NeedsRepair: 0, Orphaned: 1, Running: 0, Unknown: 0),
        "");
    var viewModel = new Replicator.Presentation.ViewModels.TaskInventoryViewModel();

    viewModel.Apply(inventory, selectedIssue: null);

    Assert(viewModel.HasIssues, "Expected orphaned scheduled task inventory to count as an issue.");
    Assert(viewModel.IsActionCenterVisible, "Expected Action Center to stay visible for orphaned or unknown issues without a selected-profile repair.");
    Assert(!viewModel.IsReviewOpen, "Applying inventory should not automatically open the review surface.");
    return Task.CompletedTask;
}

static async Task MainWindowViewModelLoadsSavedProfilesAndSelectsFirstProfile()
{
    var first = ValidProfile();
    first.Name = "First profile";
    first.SourcePath = @"C:\work\first";
    first.Target.Path = @"D:\backups\first";
    var second = ValidProfile();
    second.Name = "Second profile";

    var viewModel = CreateMainWindowViewModel([first, second]);

    await viewModel.LoadAsync();

    Assert(viewModel.Profiles.Count == 2, $"Expected two loaded profiles, got {viewModel.Profiles.Count}.");
    Assert(ReferenceEquals(viewModel.SelectedProfile, viewModel.Profiles[0]), "Expected the first loaded profile to be selected.");
    Assert(viewModel.HeaderText == "First profile", $"Unexpected header: {viewModel.HeaderText}");
    Assert(viewModel.Form.Name == "First profile", $"Unexpected form name: {viewModel.Form.Name}");
    Assert(viewModel.Form.SourcePath == first.SourcePath, $"Unexpected source path: {viewModel.Form.SourcePath}");
    Assert(viewModel.Form.DestinationPath == first.Target.Path, $"Unexpected destination path: {viewModel.Form.DestinationPath}");
    Assert(viewModel.Status.Text == "Ready.", $"Unexpected status: {viewModel.Status.Text}");
    Assert(viewModel.TaskInventory.Summary == "No Replicator scheduled tasks found.", $"Unexpected inventory summary: {viewModel.TaskInventory.Summary}");
}

static async Task MainWindowLoadCommandLoadsSavedProfiles()
{
    var profile = ValidProfile();
    profile.Name = "Load command profile";
    var viewModel = CreateMainWindowViewModel([profile]);

    await viewModel.LoadCommand.ExecuteAsync();

    Assert(viewModel.Profiles.Count == 1, $"Expected one profile, got {viewModel.Profiles.Count}.");
    Assert(viewModel.HeaderText == "Load command profile", $"Unexpected header: {viewModel.HeaderText}");
}

static async Task MainWindowViewModelCreatesAndSelectsDefaultProfile()
{
    var existing = ValidProfile();
    existing.Name = "Existing profile";
    var viewModel = CreateMainWindowViewModel([existing]);
    await viewModel.LoadAsync();

    viewModel.NewProfileCommand.Execute(null);

    Assert(viewModel.Profiles.Count == 2, $"Expected one new profile, got {viewModel.Profiles.Count} profiles.");
    Assert(!ReferenceEquals(viewModel.SelectedProfile, existing), "Expected the new profile to be selected.");
    Assert(ReferenceEquals(viewModel.SelectedProfile, viewModel.Profiles[1]), "Expected the selected profile to be the new collection item.");
    Assert(viewModel.Form.Name == "New backup profile", $"Unexpected form name: {viewModel.Form.Name}");
    Assert(viewModel.HeaderText == "New backup profile", $"Unexpected header: {viewModel.HeaderText}");
    Assert(viewModel.Status.Text == "New profile created.", $"Unexpected status: {viewModel.Status.Text}");
}

static async Task DeleteProfileCancelLeavesProfilesUnchanged()
{
    var profile = ValidProfile();
    var store = new FakeProfileStore([profile]);
    var scheduledTasks = new FakeScheduledTaskService();
    var confirmation = new FakeUserConfirmation();
    confirmation.Results.Enqueue(false);
    var viewModel = CreateMainWindowViewModel(
        profileStore: store,
        scheduledTasks: scheduledTasks,
        confirmation: confirmation);
    await viewModel.LoadAsync();

    await viewModel.DeleteProfileCommand.ExecuteAsync();

    Assert(viewModel.Profiles.Count == 1, $"Expected profile to remain, got {viewModel.Profiles.Count}.");
    Assert(ReferenceEquals(viewModel.SelectedProfile, profile), "Expected selected profile to remain unchanged.");
    Assert(store.Deletes.Count == 0, "Cancel should not delete from the profile store.");
    Assert(scheduledTasks.Deletes.Count == 0, "Cancel should not delete the scheduled task.");
    Assert(viewModel.Status.Text == "Ready.", $"Unexpected status after cancel: {viewModel.Status.Text}");
}

static async Task DeleteProfileConfirmDeletesProfileAndScheduledTask()
{
    var profile = ValidProfile();
    profile.Name = "Remove me";
    var store = new FakeProfileStore([profile]);
    var scheduledTasks = new FakeScheduledTaskService();
    var inventory = new FakeScheduledTaskInventoryService();
    var confirmation = new FakeUserConfirmation();
    confirmation.Results.Enqueue(true);
    var viewModel = CreateMainWindowViewModel(
        profileStore: store,
        scheduledTasks: scheduledTasks,
        taskInventoryService: inventory,
        confirmation: confirmation);
    await viewModel.LoadAsync();
    var initialScanCount = inventory.ScanCount;

    await viewModel.DeleteProfileCommand.ExecuteAsync();

    Assert(scheduledTasks.Deletes.Count == 1, $"Expected one scheduled task delete, got {scheduledTasks.Deletes.Count}.");
    Assert(ReferenceEquals(scheduledTasks.Deletes[0], profile), "Expected scheduled task delete to receive the selected profile.");
    Assert(store.Deletes.Count == 1 && store.Deletes[0] == profile.Id, "Expected selected profile to be deleted from the store.");
    Assert(viewModel.Profiles.Count == 1, $"Expected fallback default profile after deleting the last profile, got {viewModel.Profiles.Count}.");
    Assert(!ReferenceEquals(viewModel.SelectedProfile, profile), "Expected deleted profile to no longer be selected.");
    Assert(viewModel.HeaderText == "New backup profile", $"Unexpected fallback header: {viewModel.HeaderText}");
    Assert(viewModel.OutputText.Contains("delete output", StringComparison.Ordinal), $"Expected task output to be appended, got: {viewModel.OutputText}");
    Assert(viewModel.Status.Text == "Profile removed.", $"Unexpected status: {viewModel.Status.Text}");
    Assert(inventory.ScanCount == initialScanCount + 1, "Expected deletion to refresh task inventory.");
}

static async Task BrowseSourceCancelLeavesSourcePathUnchanged()
{
    var profile = ValidProfile();
    profile.SourcePath = @"C:\work\original";
    var folderPicker = new FakeFolderPicker();
    var viewModel = CreateMainWindowViewModel([profile], folderPicker: folderPicker);
    await viewModel.LoadAsync();

    await viewModel.BrowseSourceCommand.ExecuteAsync();

    Assert(viewModel.Form.SourcePath == profile.SourcePath, $"Expected source path to remain unchanged, got {viewModel.Form.SourcePath}.");
    Assert(folderPicker.InitialPaths.Count == 1 && folderPicker.InitialPaths[0] == profile.SourcePath, "Expected picker to receive the current source path.");
}

static async Task BrowseDestinationAppliesSelectedFolder()
{
    var profile = ValidProfile();
    profile.Target.Path = @"D:\backups\original";
    var folderPicker = new FakeFolderPicker();
    folderPicker.Results.Enqueue(@"E:\backups\selected");
    var viewModel = CreateMainWindowViewModel([profile], folderPicker: folderPicker);
    await viewModel.LoadAsync();

    await viewModel.BrowseDestinationCommand.ExecuteAsync();

    Assert(viewModel.Form.DestinationPath == @"E:\backups\selected", $"Expected selected destination, got {viewModel.Form.DestinationPath}.");
    Assert(folderPicker.InitialPaths.Count == 1 && folderPicker.InitialPaths[0] == profile.Target.Path, "Expected picker to receive the current destination path.");
}

static async Task SaveProfileAppliesFormAndPersistsSelectedProfile()
{
    var profile = ValidProfile();
    var store = new FakeProfileStore([profile]);
    var inventory = new FakeScheduledTaskInventoryService();
    var viewModel = CreateMainWindowViewModel(
        profileStore: store,
        taskInventoryService: inventory);
    await viewModel.LoadAsync();
    var initialScanCount = inventory.ScanCount;

    viewModel.Form.Name = "Saved profile";
    viewModel.Form.SourcePath = @"C:\work\saved";
    viewModel.Form.DestinationPath = @"D:\backups\saved";
    viewModel.Form.TimeText = "07:15";

    await viewModel.SaveProfileCommand.ExecuteAsync();

    Assert(profile.Name == "Saved profile", $"Expected profile name to be applied, got {profile.Name}.");
    Assert(profile.SourcePath == @"C:\work\saved", $"Expected source path to be applied, got {profile.SourcePath}.");
    Assert(profile.Target.Path == @"D:\backups\saved", $"Expected destination path to be applied, got {profile.Target.Path}.");
    Assert(profile.Schedule.TimeOfDay == new TimeOnly(7, 15), $"Expected start time to be applied, got {profile.Schedule.TimeOfDay}.");
    Assert(store.Upserts.Count == 1 && ReferenceEquals(store.Upserts[0], profile), "Expected selected profile to be upserted.");
    Assert(viewModel.HeaderText == "Saved profile", $"Unexpected header: {viewModel.HeaderText}");
    Assert(viewModel.Status.Text == "Profile saved.", $"Unexpected status: {viewModel.Status.Text}");
    Assert(inventory.ScanCount == initialScanCount + 1, "Expected save to refresh task inventory.");
}

static async Task GenerateScriptAppliesFormPersistsProfileAndAppendsGeneratedScriptPath()
{
    var profile = ValidProfile();
    var store = new FakeProfileStore([profile]);
    var viewModel = CreateMainWindowViewModel(profileStore: store);
    await viewModel.LoadAsync();
    viewModel.Form.Name = "Generated profile";

    await viewModel.GenerateScriptCommand.ExecuteAsync();

    Assert(profile.Name == "Generated profile", $"Expected form edits to be applied, got {profile.Name}.");
    Assert(store.Upserts.Count == 1 && ReferenceEquals(store.Upserts[0], profile), "Expected generated profile to be persisted.");
    Assert(viewModel.OutputText.Contains("Generated script:", StringComparison.Ordinal), $"Expected generated script output, got: {viewModel.OutputText}");
    var generatedPath = viewModel.OutputText.Split("Generated script:", StringSplitOptions.None)[1].Trim();
    Assert(File.Exists(generatedPath), $"Expected generated script to exist at {generatedPath}.");
    Assert(viewModel.Status.Text == "Script generated.", $"Unexpected status: {viewModel.Status.Text}");
}

static async Task InstallTaskWritesScriptInstallsTaskAndRefreshesInventory()
{
    var profile = ValidProfile();
    var store = new FakeProfileStore([profile]);
    var scheduledTasks = new FakeScheduledTaskService
    {
        QueryResult = new ScheduledTaskSnapshot(
            string.Empty,
            ScheduledTaskState.Ready,
            "6/3/2026 6:00:00 PM",
            "6/2/2026 6:00:00 PM",
            0,
            string.Empty)
    };
    var inventory = new FakeScheduledTaskInventoryService();
    var viewModel = CreateMainWindowViewModel(
        profileStore: store,
        scheduledTasks: scheduledTasks,
        taskInventoryService: inventory);
    await viewModel.LoadAsync();
    var initialScanCount = inventory.ScanCount;

    await viewModel.InstallTaskCommand.ExecuteAsync();

    Assert(store.Upserts.Count == 1 && ReferenceEquals(store.Upserts[0], profile), "Expected install to persist the selected profile.");
    Assert(scheduledTasks.Installs.Count == 1 && ReferenceEquals(scheduledTasks.Installs[0], profile), "Expected scheduled task install for the selected profile.");
    Assert(File.Exists(scheduledTasks.LastInstallScriptPath), $"Expected install script to be written at {scheduledTasks.LastInstallScriptPath}.");
    Assert(viewModel.OutputText.Contains("install output", StringComparison.Ordinal), $"Expected install output to be appended, got: {viewModel.OutputText}");
    Assert(viewModel.Status.Text == "Task installed.", $"Unexpected status: {viewModel.Status.Text}");
    Assert(viewModel.TaskNameText == ScheduledTaskName.ForProfile(profile), $"Unexpected task name: {viewModel.TaskNameText}");
    Assert(viewModel.ActionSurface.InstallTaskLabel == "Update Task", $"Unexpected install label: {viewModel.ActionSurface.InstallTaskLabel}");
    Assert(inventory.ScanCount == initialScanCount + 1, "Expected install to refresh task inventory.");
}

static async Task PreviewDryRunAppliesFormAndInvokesPowerShellWithDryRunSwitch()
{
    var root = Path.Combine(Environment.CurrentDirectory, "test-artifacts", Guid.NewGuid().ToString("N"));
    var source = Path.Combine(root, "source");
    var target = Path.Combine(root, "target");
    Directory.CreateDirectory(source);
    Directory.CreateDirectory(target);

    var profile = ValidProfile();
    profile.SourcePath = source;
    profile.Target.Path = target;
    profile.DryRun = false;
    var store = new FakeProfileStore([profile]);
    var processRunner = new FakeProcessRunner(new ProcessResult(0, "preview output", string.Empty));
    var viewModel = CreateMainWindowViewModel(
        profileStore: store,
        processRunner: processRunner);
    await viewModel.LoadAsync();

    await viewModel.PreviewDryRunCommand.ExecuteAsync();

    Assert(store.Upserts.Count == 1 && ReferenceEquals(store.Upserts[0], profile), "Expected dry-run preview to persist the selected profile.");
    Assert(processRunner.LastFileName.Equals("powershell.exe", StringComparison.OrdinalIgnoreCase), $"Unexpected process: {processRunner.LastFileName}");
    Assert(processRunner.LastArguments.Contains("-DryRun"), "Expected preview dry run to pass the -DryRun switch.");
    Assert(processRunner.LastArguments.Contains("-File"), "Expected generated script to be invoked by file path.");
    Assert(viewModel.OutputText.Contains("preview output", StringComparison.Ordinal), $"Expected process output, got: {viewModel.OutputText}");
    Assert(viewModel.Status.Text == "Dry run completed. Latest log is shown below.", $"Unexpected status: {viewModel.Status.Text}");
}

static async Task RunNowAppliesFormAndInvokesPowerShellWithoutDryRunSwitch()
{
    var root = Path.Combine(Environment.CurrentDirectory, "test-artifacts", Guid.NewGuid().ToString("N"));
    var source = Path.Combine(root, "source");
    var target = Path.Combine(root, "target");
    Directory.CreateDirectory(source);
    Directory.CreateDirectory(target);

    var profile = ValidProfile();
    profile.SourcePath = source;
    profile.Target.Path = target;
    profile.DryRun = false;
    var store = new FakeProfileStore([profile]);
    var processRunner = new FakeProcessRunner(new ProcessResult(0, "run output", string.Empty));
    var viewModel = CreateMainWindowViewModel(
        profileStore: store,
        processRunner: processRunner);
    await viewModel.LoadAsync();

    await viewModel.RunNowCommand.ExecuteAsync();

    Assert(store.Upserts.Count == 1 && ReferenceEquals(store.Upserts[0], profile), "Expected Run Now to persist the selected profile.");
    Assert(processRunner.LastFileName.Equals("powershell.exe", StringComparison.OrdinalIgnoreCase), $"Unexpected process: {processRunner.LastFileName}");
    Assert(!processRunner.LastArguments.Contains("-DryRun"), "Run Now should not force the -DryRun switch.");
    Assert(viewModel.OutputText.Contains("run output", StringComparison.Ordinal), $"Expected process output, got: {viewModel.OutputText}");
    Assert(viewModel.Status.Text == "Run completed. Latest log is shown below.", $"Unexpected status: {viewModel.Status.Text}");
}

static async Task EnableTaskCallsScheduledServiceAndRefreshesInventory()
{
    var profile = ValidProfile();
    var scheduledTasks = new FakeScheduledTaskService
    {
        QueryResult = new ScheduledTaskSnapshot(
            string.Empty,
            ScheduledTaskState.Ready,
            "6/3/2026 6:00:00 PM",
            string.Empty,
            0,
            string.Empty)
    };
    var inventory = new FakeScheduledTaskInventoryService();
    var viewModel = CreateMainWindowViewModel(
        [profile],
        scheduledTasks: scheduledTasks,
        taskInventoryService: inventory);
    await viewModel.LoadAsync();
    var initialScanCount = inventory.ScanCount;

    await viewModel.EnableTaskCommand.ExecuteAsync();

    Assert(scheduledTasks.Enables.Count == 1 && ReferenceEquals(scheduledTasks.Enables[0], profile), "Expected enable to target the selected profile.");
    Assert(viewModel.OutputText.Contains("enable output", StringComparison.Ordinal), $"Expected enable output, got: {viewModel.OutputText}");
    Assert(viewModel.Status.Text == "Task enabled.", $"Unexpected status: {viewModel.Status.Text}");
    Assert(viewModel.NextRunText == "6/3/2026 6:00:00 PM", $"Expected refreshed next run, got: {viewModel.NextRunText}");
    Assert(inventory.ScanCount == initialScanCount + 1, "Expected enable to refresh task inventory.");
}

static async Task DisableTaskCallsScheduledServiceAndRefreshesInventory()
{
    var profile = ValidProfile();
    var scheduledTasks = new FakeScheduledTaskService
    {
        QueryResult = new ScheduledTaskSnapshot(
            string.Empty,
            ScheduledTaskState.Disabled,
            string.Empty,
            string.Empty,
            0,
            string.Empty)
    };
    var inventory = new FakeScheduledTaskInventoryService();
    var viewModel = CreateMainWindowViewModel(
        [profile],
        scheduledTasks: scheduledTasks,
        taskInventoryService: inventory);
    await viewModel.LoadAsync();
    var initialScanCount = inventory.ScanCount;

    await viewModel.DisableTaskCommand.ExecuteAsync();

    Assert(scheduledTasks.Disables.Count == 1 && ReferenceEquals(scheduledTasks.Disables[0], profile), "Expected disable to target the selected profile.");
    Assert(viewModel.OutputText.Contains("disable output", StringComparison.Ordinal), $"Expected disable output, got: {viewModel.OutputText}");
    Assert(viewModel.Status.Text == "Task disabled.", $"Unexpected status: {viewModel.Status.Text}");
    Assert(viewModel.NextRunText == ScheduledTaskState.Disabled.ToString(), $"Expected disabled next-run text, got: {viewModel.NextRunText}");
    Assert(inventory.ScanCount == initialScanCount + 1, "Expected disable to refresh task inventory.");
}

static async Task RemoveTaskCallsScheduledServiceAndRefreshesInventory()
{
    var profile = ValidProfile();
    var scheduledTasks = new FakeScheduledTaskService
    {
        QueryResult = new ScheduledTaskSnapshot(
            string.Empty,
            ScheduledTaskState.Missing,
            string.Empty,
            string.Empty,
            0,
            string.Empty)
    };
    var inventory = new FakeScheduledTaskInventoryService();
    var viewModel = CreateMainWindowViewModel(
        [profile],
        scheduledTasks: scheduledTasks,
        taskInventoryService: inventory);
    await viewModel.LoadAsync();
    var initialScanCount = inventory.ScanCount;

    await viewModel.RemoveTaskCommand.ExecuteAsync();

    Assert(scheduledTasks.Deletes.Count == 1 && ReferenceEquals(scheduledTasks.Deletes[0], profile), "Expected remove task to target the selected profile.");
    Assert(viewModel.OutputText.Contains("delete output", StringComparison.Ordinal), $"Expected delete output, got: {viewModel.OutputText}");
    Assert(viewModel.Status.Text == "Task deleted.", $"Unexpected status: {viewModel.Status.Text}");
    Assert(viewModel.NextRunText == ScheduledTaskState.Missing.ToString(), $"Expected missing next-run text, got: {viewModel.NextRunText}");
    Assert(inventory.ScanCount == initialScanCount + 1, "Expected remove to refresh task inventory.");
}

static async Task StartScheduledTaskReportsMissingTaskWithoutRunning()
{
    var profile = ValidProfile();
    var scheduledTasks = new FakeScheduledTaskService
    {
        QueryResult = new ScheduledTaskSnapshot(
            string.Empty,
            ScheduledTaskState.Missing,
            string.Empty,
            string.Empty,
            0,
            "missing query output")
    };
    var viewModel = CreateMainWindowViewModel([profile], scheduledTasks: scheduledTasks);
    await viewModel.LoadAsync();

    await viewModel.StartScheduledTaskCommand.ExecuteAsync();

    Assert(scheduledTasks.Runs.Count == 0, "Missing scheduled task should not be started.");
    Assert(viewModel.OutputText.Contains("missing query output", StringComparison.Ordinal), $"Expected query output, got: {viewModel.OutputText}");
    Assert(viewModel.Status.Text == "No scheduled task is installed for this profile. Use Install Task or Run Now.", $"Unexpected status: {viewModel.Status.Text}");
}

static async Task StartScheduledTaskReportsRepairNeededWithoutRunning()
{
    var profile = ValidProfile();
    var scheduledTasks = new FakeScheduledTaskService
    {
        QueryResult = new ScheduledTaskSnapshot(
            string.Empty,
            ScheduledTaskState.Ready,
            string.Empty,
            string.Empty,
            0,
            string.Empty)
        {
            NeedsRepair = true,
            RepairReasons = ["Visible PowerShell action"]
        }
    };
    var viewModel = CreateMainWindowViewModel([profile], scheduledTasks: scheduledTasks);
    await viewModel.LoadAsync();

    await viewModel.StartScheduledTaskCommand.ExecuteAsync();

    Assert(scheduledTasks.Runs.Count == 0, "Repair-needed scheduled task should not be started.");
    Assert(viewModel.OutputText.Contains("Visible PowerShell action", StringComparison.Ordinal), $"Expected repair reason output, got: {viewModel.OutputText}");
    Assert(viewModel.Status.Text == "Scheduled task needs repair before it can be started. Use Repair Task.", $"Unexpected status: {viewModel.Status.Text}");
}

static async Task RefreshStatusUpdatesTaskFieldsAndActionSurface()
{
    var profile = ValidProfile();
    var scheduledTasks = new FakeScheduledTaskService
    {
        QueryResult = new ScheduledTaskSnapshot(
            string.Empty,
            ScheduledTaskState.Ready,
            "6/3/2026 6:00:00 PM",
            "6/2/2026 6:00:00 PM",
            0,
            string.Empty)
    };
    var inventory = new FakeScheduledTaskInventoryService();
    var viewModel = CreateMainWindowViewModel(
        [profile],
        scheduledTasks: scheduledTasks,
        taskInventoryService: inventory);
    await viewModel.LoadAsync();
    var initialScanCount = inventory.ScanCount;

    await viewModel.RefreshStatusCommand.ExecuteAsync();

    Assert(viewModel.TaskNameText == ScheduledTaskName.ForProfile(profile), $"Unexpected task name: {viewModel.TaskNameText}");
    Assert(viewModel.NextRunText == "6/3/2026 6:00:00 PM", $"Unexpected next run: {viewModel.NextRunText}");
    Assert(viewModel.LastRunText == "6/2/2026 6:00:00 PM; result 0", $"Unexpected last run: {viewModel.LastRunText}");
    Assert(viewModel.ActionSurface.ShowStartScheduledTask, "Expected ready task to be startable.");
    Assert(viewModel.ActionSurface.ShowDisableTask, "Expected ready task to expose disable.");
    Assert(viewModel.ActionSurface.InstallTaskLabel == "Update Task", $"Unexpected install label: {viewModel.ActionSurface.InstallTaskLabel}");
    Assert(inventory.ScanCount == initialScanCount + 1, "Expected refresh status to refresh task inventory.");
}

static async Task ReviewTaskInventoryOpensRowsOrReportsNone()
{
    var profile = ValidProfile();
    var item = new ScheduledTaskInventoryItem(
        ScheduledTaskName.ForProfile(profile),
        profile.Id,
        profile.Name,
        ScheduledTaskInventoryState.Ready,
        ScheduledTaskState.Ready,
        string.Empty,
        string.Empty,
        0,
        "powershell.exe",
        @"C:\Replicator\profile.ps1",
        false,
        [],
        "Ready",
        string.Empty);
    var inventoryWithRows = new FakeScheduledTaskInventoryService
    {
        Result = new ScheduledTaskInventoryResult(
            [item],
            new ScheduledTaskInventorySummary(1, 1, 0, 0, 0, 0),
            string.Empty)
    };
    var viewModelWithRows = CreateMainWindowViewModel(
        [profile],
        taskInventoryService: inventoryWithRows);
    await viewModelWithRows.LoadAsync();

    await viewModelWithRows.ReviewTaskInventoryCommand.ExecuteAsync();

    Assert(viewModelWithRows.TaskInventory.IsReviewOpen, "Expected review surface to open when inventory rows exist.");

    var emptyViewModel = CreateMainWindowViewModel([ValidProfile()]);
    await emptyViewModel.LoadAsync();

    await emptyViewModel.ReviewTaskInventoryCommand.ExecuteAsync();

    Assert(!emptyViewModel.TaskInventory.IsReviewOpen, "Empty inventory should not open review.");
    Assert(emptyViewModel.Status.Text == "No Replicator scheduled tasks were found.", $"Unexpected empty-inventory status: {emptyViewModel.Status.Text}");
}

static async Task RepairSelectedInventoryTaskInstallsProfileTask()
{
    var profile = ValidProfile();
    var item = new ScheduledTaskInventoryItem(
        ScheduledTaskName.ForProfile(profile),
        profile.Id,
        profile.Name,
        ScheduledTaskInventoryState.NeedsRepair,
        ScheduledTaskState.Ready,
        string.Empty,
        string.Empty,
        0,
        "powershell.exe",
        @"C:\Replicator\profile.ps1",
        true,
        ["Visible PowerShell action"],
        "Visible PowerShell action",
        string.Empty);
    var inventory = new FakeScheduledTaskInventoryService
    {
        Result = new ScheduledTaskInventoryResult(
            [item],
            new ScheduledTaskInventorySummary(1, 0, 1, 0, 0, 0),
            string.Empty)
    };
    var scheduledTasks = new FakeScheduledTaskService
    {
        QueryResult = new ScheduledTaskSnapshot(
            string.Empty,
            ScheduledTaskState.Ready,
            string.Empty,
            string.Empty,
            0,
            string.Empty)
    };
    var viewModel = CreateMainWindowViewModel(
        [profile],
        scheduledTasks: scheduledTasks,
        taskInventoryService: inventory);
    await viewModel.LoadAsync();
    var initialScanCount = inventory.ScanCount;

    await viewModel.RepairSelectedInventoryTaskCommand.ExecuteAsync();

    Assert(scheduledTasks.Installs.Count == 1 && ReferenceEquals(scheduledTasks.Installs[0], profile), "Expected selected repair item to reinstall the profile task.");
    Assert(viewModel.OutputText.Contains("install output", StringComparison.Ordinal), $"Expected install output, got: {viewModel.OutputText}");
    Assert(viewModel.Status.Text == "Task installed.", $"Unexpected status: {viewModel.Status.Text}");
    Assert(inventory.ScanCount == initialScanCount + 1, "Expected repair to refresh task inventory.");
}

static async Task MainWindowViewModelMarksDriveSecurityPermissionRequiredForElevation()
{
    var profile = ValidProfile();
    profile.SourcePath = @"C:\work\scratch";
    profile.Target.Path = @"D:\backups\scratch";
    var provider = new FakeBitLockerStatusProvider();
    provider.Items[@"C:\"] = new DriveSecurityItem(
        "Source drive",
        profile.SourcePath,
        @"C:\",
        DriveSecurityState.Protected,
        DriveSecuritySeverity.Info,
        @"Drive security: Source drive is BitLocker protected (C:\).");
    provider.Items[@"D:\"] = new DriveSecurityItem(
        "Target drive",
        profile.Target.Path,
        @"D:\",
        DriveSecurityState.PermissionRequired,
        DriveSecuritySeverity.Warning,
        @"Drive security: Target drive needs an administrator check (D:\).");
    var viewModel = CreateMainWindowViewModel(
        [profile],
        driveSecurityProvider: provider);

    await viewModel.LoadAsync();

    Assert(viewModel.DriveSecurityRequiresElevation, "Expected permission-required drive security to request elevation.");
    Assert(viewModel.ActionSurface.ShowElevatedDriveSecurity, "Expected action surface to show elevated drive security command.");
    Assert(viewModel.DriveSecurityText.Contains("Check as Admin", StringComparison.OrdinalIgnoreCase), $"Unexpected drive security text: {viewModel.DriveSecurityText}");
}

static async Task CheckDriveSecurityAsAdminRefreshesSelectedProfileElevationState()
{
    var profile = ValidProfile();
    profile.SourcePath = @"C:\work\scratch";
    profile.Target.Path = @"D:\backups\scratch";
    var provider = new FakeBitLockerStatusProvider();
    provider.Items[@"C:\"] = new DriveSecurityItem(
        "Source drive",
        profile.SourcePath,
        @"C:\",
        DriveSecurityState.Protected,
        DriveSecuritySeverity.Info,
        @"Drive security: Source drive is BitLocker protected (C:\).");
    provider.Items[@"D:\"] = new DriveSecurityItem(
        "Target drive",
        profile.Target.Path,
        @"D:\",
        DriveSecurityState.PermissionRequired,
        DriveSecuritySeverity.Warning,
        @"Drive security: Target drive needs elevated permissions (D:\).");
    var elevatedProvider = new FakeBitLockerStatusProvider();
    elevatedProvider.Items[@"C:\"] = provider.Items[@"C:\"];
    elevatedProvider.Items[@"D:\"] = new DriveSecurityItem(
        "Target drive",
        profile.Target.Path,
        @"D:\",
        DriveSecurityState.Protected,
        DriveSecuritySeverity.Info,
        @"Drive security: Target drive is BitLocker protected (D:\).");
    var viewModel = CreateMainWindowViewModel(
        [profile],
        driveSecurityProvider: provider,
        elevatedDriveSecurityProvider: elevatedProvider);
    await viewModel.LoadAsync();

    await viewModel.CheckDriveSecurityAsAdminCommand.ExecuteAsync();

    Assert(!viewModel.DriveSecurityRequiresElevation, "Expected elevated refresh to clear elevation requirement.");
    Assert(!viewModel.ActionSurface.ShowElevatedDriveSecurity, "Expected action surface to hide elevated drive security command.");
    Assert(viewModel.DriveSecurityText == "Drive security: checked local profile drives.", $"Unexpected drive security text: {viewModel.DriveSecurityText}");
}

static async Task PrepareShuttleCommandRunsDryRunAndReportsResult()
{
    var root = Path.Combine(Environment.CurrentDirectory, "test-artifacts", Guid.NewGuid().ToString("N"));
    var source = Path.Combine(root, "source");
    var shuttleRoot = Path.Combine(root, "external", "Replicator", "shuttle", "repo");
    Directory.CreateDirectory(source);
    await File.WriteAllTextAsync(Path.Combine(source, "note.md"), "hello");

    var profile = ValidShuttleProfile(Guid.NewGuid(), source, shuttleRoot);
    profile.DryRun = true;
    var store = new FakeProfileStore([profile]);
    var viewModel = CreateMainWindowViewModel(profileStore: store);
    await viewModel.LoadAsync();

    await viewModel.PrepareShuttleCommand.ExecuteAsync();

    Assert(store.Upserts.Count == 1 && ReferenceEquals(store.Upserts[0], profile), "Expected prepare shuttle to persist the selected profile.");
    Assert(viewModel.Status.Text == "Prepare Shuttle dry run completed. Uncheck Dry run to write the shuttle payload.", $"Unexpected status: {viewModel.Status.Text}");
    Assert(viewModel.OutputText.Contains("Would stage note.md", StringComparison.Ordinal), $"Expected dry-run details, got: {viewModel.OutputText}");
    Assert(viewModel.ActionSurface.ShowPrepareShuttle, "Expected shuttle action surface to remain visible.");
}

static async Task DepartShuttleCommandMarksPreparedPayloadReady()
{
    var root = Path.Combine(Environment.CurrentDirectory, "test-artifacts", Guid.NewGuid().ToString("N"));
    var source = Path.Combine(root, "source");
    var shuttleRoot = Path.Combine(root, "external", "Replicator", "shuttle", "repo");
    Directory.CreateDirectory(source);
    await File.WriteAllTextAsync(Path.Combine(source, "note.md"), "hello");

    var profile = ValidShuttleProfile(Guid.NewGuid(), source, shuttleRoot);
    profile.DryRun = false;
    var viewModel = CreateMainWindowViewModel([profile]);
    await viewModel.LoadAsync();
    await viewModel.PrepareShuttleCommand.ExecuteAsync();

    await viewModel.DepartShuttleCommand.ExecuteAsync();

    Assert(viewModel.Status.Text.Contains("Departed from", StringComparison.Ordinal), $"Unexpected status: {viewModel.Status.Text}");
    Assert(viewModel.OutputText.Contains("ready to dock", StringComparison.OrdinalIgnoreCase), $"Expected departed output, got: {viewModel.OutputText}");
}

static async Task DockShuttleCommandReportsNoInboundChanges()
{
    var root = Path.Combine(Environment.CurrentDirectory, "test-artifacts", Guid.NewGuid().ToString("N"));
    var source = Path.Combine(root, "source");
    var shuttleRoot = Path.Combine(root, "external", "Replicator", "shuttle", "repo");
    Directory.CreateDirectory(source);

    var profile = ValidShuttleProfile(Guid.NewGuid(), source, shuttleRoot);
    var viewModel = CreateMainWindowViewModel([profile]);
    await viewModel.LoadAsync();

    await viewModel.DockShuttleCommand.ExecuteAsync();

    Assert(viewModel.Status.Text == "No inbound shuttle changes are waiting for this machine.", $"Unexpected status: {viewModel.Status.Text}");
    Assert(viewModel.OutputText.Contains("No inbound shuttle changes", StringComparison.Ordinal), $"Expected no-inbound output, got: {viewModel.OutputText}");
}

static async Task ReceiveShuttleCommandReportsNoInboundChanges()
{
    var root = Path.Combine(Environment.CurrentDirectory, "test-artifacts", Guid.NewGuid().ToString("N"));
    var source = Path.Combine(root, "source");
    var shuttleRoot = Path.Combine(root, "external", "Replicator", "shuttle", "repo");
    Directory.CreateDirectory(source);

    var profile = ValidShuttleProfile(Guid.NewGuid(), source, shuttleRoot);
    var viewModel = CreateMainWindowViewModel([profile]);
    await viewModel.LoadAsync();

    await viewModel.ReceiveShuttleCommand.ExecuteAsync();

    Assert(viewModel.Status.Text == "No inbound shuttle changes are waiting for this machine.", $"Unexpected status: {viewModel.Status.Text}");
    Assert(viewModel.OutputText.Contains("No inbound shuttle changes", StringComparison.Ordinal), $"Expected no-inbound output, got: {viewModel.OutputText}");
}

static async Task BusyOperationDisablesMutationAndRestoresActionSurface()
{
    var root = Path.Combine(Environment.CurrentDirectory, "test-artifacts", Guid.NewGuid().ToString("N"));
    var source = Path.Combine(root, "source");
    var target = Path.Combine(root, "target");
    Directory.CreateDirectory(source);
    Directory.CreateDirectory(target);

    var profile = ValidProfile();
    profile.SourcePath = source;
    profile.Target.Path = target;
    profile.DryRun = false;
    var processRunner = new BlockingProcessRunner();
    var viewModel = CreateMainWindowViewModel(
        [profile],
        processRunner: processRunner);
    await viewModel.LoadAsync();

    var runTask = viewModel.RunNowCommand.ExecuteAsync();
    await processRunner.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

    Assert(viewModel.IsBusy, "Expected view model to be busy while Run Now is in flight.");
    Assert(!viewModel.ActionSurface.CanMutateProfile, "Busy operation should disable profile mutation.");
    Assert(viewModel.Status.Text == "Running profile now...", $"Unexpected busy status: {viewModel.Status.Text}");

    processRunner.Complete(new ProcessResult(0, "busy run output", string.Empty));
    await runTask;

    Assert(!viewModel.IsBusy, "Expected busy state to clear after operation completes.");
    Assert(viewModel.ActionSurface.CanMutateProfile, "Expected profile mutation to be restored after operation completes.");
    Assert(viewModel.OutputText.Contains("busy run output", StringComparison.Ordinal), $"Expected process output, got: {viewModel.OutputText}");
}

static async Task ShuttleOperationIsBusyCancellableAndBlocksProfileMutation()
{
    var profile = ValidProfile();
    profile.Mode = ProfileMode.Shuttle;
    profile.DryRun = false;
    profile.Target.Path = Path.Combine(Environment.CurrentDirectory, "test-artifacts", Guid.NewGuid().ToString("N"), "shuttle");
    var shuttleService = new BlockingShuttleService();
    var viewModel = CreateMainWindowViewModel([profile], shuttleService: shuttleService);
    await viewModel.LoadAsync();
    var originalProfileCount = viewModel.Profiles.Count;
    var canExecuteChangedCount = 0;
    viewModel.NewProfileCommand.CanExecuteChanged += (_, _) => canExecuteChangedCount++;

    var operation = viewModel.PrepareShuttleCommand.ExecuteAsync();
    await shuttleService.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

    Assert(viewModel.IsBusy, "Expected shuttle operation to mark the view model busy.");
    Assert(viewModel.CanCancel, "Expected shuttle operation to expose cancellation.");
    Assert(!viewModel.NewProfileCommand.CanExecute(null), "Expected New Profile command to be disabled while shuttle operation is busy.");
    Assert(!viewModel.ActionSurface.CanMutateProfile, "Expected shuttle busy state to disable profile mutation.");
    Assert(canExecuteChangedCount > 0, "Expected busy state to notify command bindings that CanExecute changed.");

    viewModel.NewProfileCommand.Execute(null);
    Assert(viewModel.Profiles.Count == originalProfileCount, "New Profile command should not mutate profiles when CanExecute is false.");

    viewModel.CancelOperationCommand.Execute(null);
    var completed = await Task.WhenAny(operation, Task.Delay(TimeSpan.FromMilliseconds(250))) == operation;
    if (!completed)
    {
        shuttleService.Complete(new ShuttleOperationResult(true, "Released blocked shuttle operation.", null));
        await operation;
    }

    Assert(completed, "Expected shuttle operation to observe cancellation instead of staying blocked.");
    Assert(shuttleService.ObservedCancellation, "Expected shuttle service to receive the cancellation token.");
    Assert(!viewModel.IsBusy, "Expected busy state to clear after cancellation.");
    Assert(viewModel.Status.Text == "Operation canceled.", $"Unexpected cancellation status: {viewModel.Status.Text}");
}

static async Task CancelOperationCommandIsDisabledWhenIdle()
{
    var viewModel = CreateMainWindowViewModel([ValidProfile()]);
    await viewModel.LoadAsync();

    Assert(!viewModel.CanCancel, "Expected idle view model to report no cancellable operation.");
    Assert(!viewModel.CancelOperationCommand.CanExecute(null), "Expected cancel command to be disabled while idle.");
}

static Task ValidatorRejectsNestedDestination()
{
    var profile = ValidProfile();
    profile.SourcePath = @"C:\work\scratch";
    profile.Target.Path = @"C:\work\scratch\backup";

    var issues = BackupProfileValidator.Validate(profile);

    Assert(issues.Any(issue => issue.Field == "Path"), "Expected nested destination validation issue.");
    return Task.CompletedTask;
}

static Task ScriptGeneratorEmitsDryRunScript()
{
    var root = Path.Combine(Environment.CurrentDirectory, "test-artifacts", Guid.NewGuid().ToString("N"));
    var generator = new PowerShellScriptGenerator(Path.Combine(root, "scripts"), Path.Combine(root, "logs"));
    var profile = ValidProfile();
    profile.DryRun = true;
    profile.ExcludePatterns = ["node_modules", "bin"];

    var script = generator.Generate(profile);

    Assert(script.Path.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase), "Expected PowerShell script path.");
    Assert(script.Content.Contains("robocopy", StringComparison.OrdinalIgnoreCase), "Expected robocopy invocation.");
    Assert(script.Content.Contains("$DryRunFromProfile = $true", StringComparison.Ordinal), "Expected profile dry-run flag.");
    Assert(script.Content.Contains("'node_modules'", StringComparison.Ordinal), "Expected node_modules exclude.");
    Assert(script.Content.Contains("'bin'", StringComparison.Ordinal), "Expected bin exclude.");

    return Task.CompletedTask;
}

static Task ScriptGeneratorEmitsTargetPreflightStatus()
{
    var root = Path.Combine(Environment.CurrentDirectory, "test-artifacts", Guid.NewGuid().ToString("N"));
    var generator = new PowerShellScriptGenerator(Path.Combine(root, "scripts"), Path.Combine(root, "logs"));

    var script = generator.Generate(ValidProfile());

    Assert(script.Content.Contains("Target path does not exist; dry run would create it during a real run:", StringComparison.Ordinal), "Expected dry-run target-path message.");
    Assert(script.Content.Contains("Write-Status -Message $message -ExitCode 0 -Succeeded $true", StringComparison.Ordinal), "Expected dry-run target status write.");
    Assert(
        script.Content.IndexOf("Target path does not exist; dry run would create it during a real run:", StringComparison.Ordinal)
            < script.Content.IndexOf("& robocopy", StringComparison.OrdinalIgnoreCase),
        "Expected target preflight before robocopy.");

    return Task.CompletedTask;
}

static async Task ScriptGeneratorWritesHiddenScheduledTaskLauncher()
{
    var root = Path.Combine(Environment.CurrentDirectory, "test-artifacts", Guid.NewGuid().ToString("N"));
    var generator = new PowerShellScriptGenerator(Path.Combine(root, "scripts"), Path.Combine(root, "logs"));

    try
    {
        var script = await generator.WriteAsync(ValidProfile());
        var launcherPath = Path.ChangeExtension(script.Path, ".vbs");

        Assert(File.Exists(launcherPath), $"Expected hidden scheduled task launcher at {launcherPath}.");

        var launcher = await File.ReadAllTextAsync(launcherPath);
        Assert(launcher.Contains("WScript.Shell", StringComparison.Ordinal), "Expected launcher to use the windowless WScript host.");
        Assert(launcher.Contains("-WindowStyle Hidden", StringComparison.OrdinalIgnoreCase), "Expected launcher to hide PowerShell.");
        Assert(launcher.Contains(script.Path, StringComparison.OrdinalIgnoreCase), "Expected launcher to invoke the generated PowerShell script.");
        Assert(launcher.Contains("shell.Run(command, 0, True)", StringComparison.Ordinal), "Expected launcher to wait for the hidden PowerShell process.");
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

static Task LogReaderSummarizesLatestRobocopyLog()
{
    var root = Path.Combine(Environment.CurrentDirectory, "test-artifacts", Guid.NewGuid().ToString("N"));
    var logsDirectory = Path.Combine(root, "logs");
    Directory.CreateDirectory(logsDirectory);

    try
    {
        var profile = ValidProfile();
        var slug = PowerShellScriptGenerator.ProfileSlug(profile);
        var logPath = Path.Combine(logsDirectory, $"{slug}-20260518-220000.log");

        File.WriteAllLines(
            logPath,
            [
                "Replicator backup run",
                "Mode: Dry run - no files will be copied",
                "Source: C:\\work\\scratch",
                "Destination: D:\\backups\\scratch",
                "",
                "------------------------------------------------------------------------------",
                "               Total    Copied   Skipped  Mismatch    FAILED    Extras",
                "    Dirs :        42        41         1         0         0         0",
                "   Files :        73        73         0         0         0         0",
                "   Bytes :    1.57 m    1.57 m         0         0         0         0"
            ]);

        var summary = new BackupLogReader(logsDirectory).ReadLatest(profile);

        if (summary is null)
        {
            throw new InvalidOperationException("Expected latest log summary.");
        }

        Assert(summary.Mode == "Dry run - no files will be copied", "Expected mode from header.");
        Assert(summary.TotalFiles == 73, "Expected total file count.");
        Assert(summary.CopiedFiles == 73, "Expected copied/listed file count.");
        Assert(summary.FailedFiles == 0, "Expected failed file count.");
        Assert(summary.TotalDirectories == 42, "Expected total directory count.");
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    return Task.CompletedTask;
}

static Task LogReaderSkipsLockedLatestRobocopyLog()
{
    var root = Path.Combine(Environment.CurrentDirectory, "test-artifacts", Guid.NewGuid().ToString("N"));
    var logsDirectory = Path.Combine(root, "logs");
    Directory.CreateDirectory(logsDirectory);

    try
    {
        var profile = ValidProfile();
        var slug = PowerShellScriptGenerator.ProfileSlug(profile);
        var lockedLogPath = Path.Combine(logsDirectory, $"{slug}-20260522-100250.log");
        var readableLogPath = Path.Combine(logsDirectory, $"{slug}-20260522-100001.log");

        File.WriteAllLines(lockedLogPath, ["locked latest"]);
        File.WriteAllLines(
            readableLogPath,
            [
                "Replicator backup run",
                "Mode: Copy - files may be copied",
                "Source: C:\\work\\scratch",
                "Destination: D:\\backups\\scratch",
                "    Dirs :         2         2         0         0         0         0",
                "   Files :         3         3         0         0         0         0"
            ]);
        File.SetLastWriteTimeUtc(readableLogPath, new DateTime(2026, 5, 22, 10, 0, 1, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(lockedLogPath, new DateTime(2026, 5, 22, 10, 2, 50, DateTimeKind.Utc));

        using var lockedStream = new FileStream(lockedLogPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var summary = new BackupLogReader(logsDirectory).ReadLatest(profile);

        if (summary is null)
        {
            throw new InvalidOperationException("Expected readable previous log summary.");
        }

        Assert(summary.LogPath == readableLogPath, $"Expected locked latest log to be skipped, got {summary.LogPath}.");
        Assert(summary.TotalFiles == 3, "Expected previous readable log metrics.");
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    return Task.CompletedTask;
}

static Task StatusReaderParsesLatestBackupStatus()
{
    var root = Path.Combine(Environment.CurrentDirectory, "test-artifacts", Guid.NewGuid().ToString("N"));
    var logsDirectory = Path.Combine(root, "logs");
    Directory.CreateDirectory(logsDirectory);

    try
    {
        var profile = ValidProfile();
        var slug = PowerShellScriptGenerator.ProfileSlug(profile);
        var statusPath = Path.Combine(logsDirectory, $"{slug}-latest.json");
        File.WriteAllText(statusPath, """
{
  "ProfileName": "Scratch",
  "Mode": "DryRun",
  "Source": "D:\\repos\\work",
  "Destination": "H:\\dev\\work",
  "LogPath": "C:\\Users\\aprotteau\\AppData\\Local\\Replicator\\logs\\scratch.log",
  "StartedAt": "2026-05-21T20:00:00.0000000Z",
  "UpdatedAt": "2026-05-21T20:00:01.0000000Z",
  "ExitCode": 0,
  "Succeeded": true,
  "Message": "Target path does not exist; dry run would create it during a real run: H:\\dev\\work"
}
""");
        var reader = new BackupRunStatusReader(logsDirectory);

        var status = reader.ReadLatest(profile);

        if (status is null)
        {
            throw new InvalidOperationException("Expected latest status to be parsed.");
        }

        Assert(status.ProfileName == "Scratch", $"Unexpected profile name: {status.ProfileName}");
        Assert(status.Mode == "DryRun", $"Unexpected mode: {status.Mode}");
        Assert(status.ExitCode == 0, $"Unexpected exit code: {status.ExitCode}");
        Assert(status.Succeeded, "Expected status success.");
        Assert(status.Message.Contains("dry run would create", StringComparison.OrdinalIgnoreCase), $"Unexpected message: {status.Message}");
        Assert(status.ToDisplayString().Contains("DryRun", StringComparison.Ordinal), "Expected display string to include mode.");
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    return Task.CompletedTask;
}

static Task StatusReaderIgnoresMalformedLatestBackupStatus()
{
    var root = Path.Combine(Environment.CurrentDirectory, "test-artifacts", Guid.NewGuid().ToString("N"));
    var logsDirectory = Path.Combine(root, "logs");
    Directory.CreateDirectory(logsDirectory);

    try
    {
        var profile = ValidProfile();
        var slug = PowerShellScriptGenerator.ProfileSlug(profile);
        File.WriteAllText(Path.Combine(logsDirectory, $"{slug}-latest.json"), "{ not json");
        var reader = new BackupRunStatusReader(logsDirectory);

        var status = reader.ReadLatest(profile);

        Assert(status is null, "Expected malformed status to be ignored.");
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    return Task.CompletedTask;
}

static async Task ProfileStoreRoundTripsJson()
{
    var root = Path.Combine(Environment.CurrentDirectory, "test-artifacts", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);

    try
    {
        var store = new JsonProfileStore(Path.Combine(root, "profiles.json"));
        var profile = ValidProfile();
        profile.Name = "Round trip";
        profile.Schedule.Cadence = ScheduleCadence.Weekly;
        profile.Schedule.DayOfWeek = DayOfWeek.Friday;

        await store.UpsertAsync(profile);
        var loaded = await store.LoadAsync();

        Assert(loaded.Count == 1, "Expected one profile.");
        Assert(loaded[0].Name == "Round trip", "Expected saved name.");
        Assert(loaded[0].Schedule.Cadence == ScheduleCadence.Weekly, "Expected saved cadence.");
        Assert(loaded[0].Schedule.DayOfWeek == DayOfWeek.Friday, "Expected saved day.");
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

static async Task ShuttlePrepareDepartDockReceivePreservesConflicts()
{
    var root = Path.Combine(Environment.CurrentDirectory, "test-artifacts", Guid.NewGuid().ToString("N"));
    var homeSource = Path.Combine(root, "home", "repo");
    var workSource = Path.Combine(root, "work", "repo");
    var shuttleRoot = Path.Combine(root, "external", "Replicator", "shuttle", "repo");

    Directory.CreateDirectory(homeSource);
    Directory.CreateDirectory(workSource);

    try
    {
        File.WriteAllText(Path.Combine(homeSource, "note.md"), "from home");
        Directory.CreateDirectory(Path.Combine(homeSource, "node_modules"));
        File.WriteAllText(Path.Combine(homeSource, "node_modules", "ignored.txt"), "ignored");
        File.WriteAllText(Path.Combine(workSource, "note.md"), "local work edit");

        var profileId = Guid.NewGuid();
        var homeProfile = ValidShuttleProfile(profileId, homeSource, shuttleRoot);
        var workProfile = ValidShuttleProfile(profileId, workSource, shuttleRoot);

        var home = new ShuttleService(new MachineIdentity("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "HOME"));
        var work = new ShuttleService(new MachineIdentity("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", "WORK"));

        var prepare = await home.PrepareAsync(homeProfile);
        Assert(prepare.Succeeded, prepare.Message);
        Assert(File.Exists(Path.Combine(shuttleRoot, "payload", "note.md")), "Expected shuttle payload file.");
        Assert(!File.Exists(Path.Combine(shuttleRoot, "payload", "node_modules", "ignored.txt")), "Expected excluded payload file to be skipped.");

        var depart = await home.DepartAsync(homeProfile);
        Assert(depart.Succeeded, depart.Message);
        Assert(depart.Manifest?.ReadyToDock == true, "Expected depart manifest to be ready to dock.");

        var dock = await work.DockAsync(workProfile);
        Assert(dock.Succeeded, dock.Message);
        Assert(dock.Manifest?.ConflictFiles == 1, "Expected one potential conflict.");

        var receive = await work.ReceiveAsync(workProfile);
        Assert(receive.Succeeded, receive.Message);
        Assert(File.ReadAllText(Path.Combine(workSource, "note.md")) == "from home", "Expected inbound shuttle file to overwrite local file.");

        var conflictFiles = Directory.EnumerateFiles(Path.Combine(shuttleRoot, "conflicts"), "note.md", SearchOption.AllDirectories).ToList();
        Assert(conflictFiles.Count == 1, "Expected preserved local conflict copy.");
        Assert(File.ReadAllText(conflictFiles[0]) == "local work edit", "Expected conflict copy to preserve local content.");
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

static Task ShuttleSourceEnumerationPrunesExcludedDirectories()
{
    var root = Path.GetFullPath(Path.Combine("source-root"));
    var src = Path.Combine(root, "src");
    var excluded = Path.Combine(root, "node_modules");
    var visited = new List<string>();

    var enumerator = new ShuttleSourceFileEnumerator(
        path =>
        {
            visited.Add($"files:{path}");
            if (path == excluded)
            {
                throw new InvalidOperationException("Excluded directory should not be scanned for files.");
            }

            if (path == root)
            {
                return [Path.Combine(root, "README.md")];
            }

            if (path == src)
            {
                return [Path.Combine(src, "app.cs")];
            }

            return [];
        },
        path =>
        {
            visited.Add($"dirs:{path}");
            if (path == excluded)
            {
                throw new InvalidOperationException("Excluded directory should not be scanned for child directories.");
            }

            return path == root
                ? [src, excluded]
                : [];
        });

    var files = enumerator.EnumerateFiles(root, ["node_modules"], CancellationToken.None).ToList();

    Assert(files.Count == 2, $"Expected only included files, got {files.Count}.");
    Assert(files.Any(path => path.EndsWith("README.md", StringComparison.OrdinalIgnoreCase)), "Expected root file.");
    Assert(files.Any(path => path.EndsWith("app.cs", StringComparison.OrdinalIgnoreCase)), "Expected src file.");
    Assert(!visited.Contains($"files:{excluded}"), "Excluded directory should not be enumerated for files.");
    Assert(!visited.Contains($"dirs:{excluded}"), "Excluded directory should not be enumerated for directories.");
    return Task.CompletedTask;
}

static async Task ShuttlePreparePreservesTimestampsForFastSkipAnalysis()
{
    var root = Path.Combine(Environment.CurrentDirectory, "test-artifacts", Guid.NewGuid().ToString("N"));
    var homeSource = Path.Combine(root, "home", "repo");
    var workSource = Path.Combine(root, "work", "repo");
    var shuttleRoot = Path.Combine(root, "external", "Replicator", "shuttle", "repo");

    Directory.CreateDirectory(homeSource);
    Directory.CreateDirectory(workSource);

    try
    {
        var sourceFile = Path.Combine(homeSource, "note.md");
        var matchingWorkFile = Path.Combine(workSource, "note.md");
        var expectedTimestamp = new DateTime(2026, 1, 2, 3, 4, 6, DateTimeKind.Utc);

        File.WriteAllText(sourceFile, "same content");
        File.WriteAllText(matchingWorkFile, "same content");
        File.SetLastWriteTimeUtc(sourceFile, expectedTimestamp);
        File.SetLastWriteTimeUtc(matchingWorkFile, expectedTimestamp);

        var profileId = Guid.NewGuid();
        var homeProfile = ValidShuttleProfile(profileId, homeSource, shuttleRoot);
        var workProfile = ValidShuttleProfile(profileId, workSource, shuttleRoot);
        var payloadFile = Path.Combine(shuttleRoot, "payload", "note.md");

        var home = new ShuttleService(new MachineIdentity("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "HOME"));
        var work = new ShuttleService(new MachineIdentity("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", "WORK"));

        var prepare = await home.PrepareAsync(homeProfile);
        Assert(prepare.Succeeded, prepare.Message);
        Assert(File.Exists(payloadFile), "Expected shuttle payload file.");
        Assert(TimestampsWithinTolerance(File.GetLastWriteTimeUtc(payloadFile), expectedTimestamp), "Expected prepared payload to preserve source timestamp.");
        Assert(prepare.Manifest?.Entries.Count == 1, "Expected prepare manifest to include one file entry.");
        Assert(prepare.Manifest?.Entries[0].RelativePath == "note.md", "Expected normalized manifest relative path.");
        Assert(prepare.Manifest?.Entries[0].Sha256 == Sha256(sourceFile), "Expected manifest entry hash to match source file.");

        var depart = await home.DepartAsync(homeProfile);
        Assert(depart.Succeeded, depart.Message);
        Assert(depart.Manifest?.Entries.Count == 1, "Expected depart manifest to preserve file entries.");

        var dock = await work.DockAsync(workProfile);
        Assert(dock.Succeeded, dock.Message);
        Assert(dock.Manifest?.SkippedFiles == 1, "Expected matching inbound file to be skipped.");
        Assert(dock.Manifest?.ChangedFiles == 0, "Expected no changed files for matching inbound payload.");
        Assert(dock.Manifest?.ConflictFiles == 0, "Expected no conflicts for matching inbound payload.");
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

static async Task ShuttleDockComparesDriftedLocalFilesAgainstManifestHash()
{
    var root = Path.Combine(Environment.CurrentDirectory, "test-artifacts", Guid.NewGuid().ToString("N"));
    var homeSource = Path.Combine(root, "home", "repo");
    var workSource = Path.Combine(root, "work", "repo");
    var shuttleRoot = Path.Combine(root, "external", "Replicator", "shuttle", "repo");

    Directory.CreateDirectory(homeSource);
    Directory.CreateDirectory(workSource);

    try
    {
        var sourceFile = Path.Combine(homeSource, "note.md");
        var workFile = Path.Combine(workSource, "note.md");

        File.WriteAllText(sourceFile, "same content");
        File.WriteAllText(workFile, "same content");
        File.SetLastWriteTimeUtc(sourceFile, new DateTime(2026, 1, 2, 3, 4, 6, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(workFile, new DateTime(2026, 1, 2, 4, 4, 6, DateTimeKind.Utc));

        var profileId = Guid.NewGuid();
        var homeProfile = ValidShuttleProfile(profileId, homeSource, shuttleRoot);
        var workProfile = ValidShuttleProfile(profileId, workSource, shuttleRoot);

        var home = new ShuttleService(new MachineIdentity("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "HOME"));
        var work = new ShuttleService(new MachineIdentity("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", "WORK"));

        var prepare = await home.PrepareAsync(homeProfile);
        Assert(prepare.Succeeded, prepare.Message);

        var depart = await home.DepartAsync(homeProfile);
        Assert(depart.Succeeded, depart.Message);

        var dock = await work.DockAsync(workProfile);
        Assert(dock.Succeeded, dock.Message);
        Assert(dock.Manifest?.SkippedFiles == 1, "Expected same-content file with drifted timestamp to be skipped by manifest hash.");
        Assert(dock.Manifest?.ChangedFiles == 0, "Expected manifest hash comparison to avoid a false change.");
        Assert(dock.Manifest?.ConflictFiles == 0, "Expected manifest hash comparison to avoid a false conflict.");
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

static async Task ShuttleLongManifestSmokeHandles6500SkippedFiles()
{
    const int fileCount = 6500;

    var root = Path.Combine(Environment.CurrentDirectory, "test-artifacts", Guid.NewGuid().ToString("N"));
    var homeSource = Path.Combine(root, "home", "repo");
    var workSource = Path.Combine(root, "work", "repo");
    var shuttleRoot = Path.Combine(root, "external", "Replicator", "shuttle", "repo");

    Directory.CreateDirectory(homeSource);
    Directory.CreateDirectory(workSource);

    try
    {
        var homeTimestamp = new DateTime(2026, 1, 2, 3, 4, 6, DateTimeKind.Utc);
        var workTimestamp = homeTimestamp.AddHours(1);

        for (var index = 0; index < fileCount; index++)
        {
            var relativeDirectory = Path.Combine($"bucket-{index % 100:000}", $"slice-{index % 10:00}");
            var homeDirectory = Path.Combine(homeSource, relativeDirectory);
            var workDirectory = Path.Combine(workSource, relativeDirectory);
            Directory.CreateDirectory(homeDirectory);
            Directory.CreateDirectory(workDirectory);

            var fileName = $"file-{index:00000}.md";
            var homeFile = Path.Combine(homeDirectory, fileName);
            var workFile = Path.Combine(workDirectory, fileName);
            var content = $"same content {index:00000}{Environment.NewLine}";

            File.WriteAllText(homeFile, content);
            File.WriteAllText(workFile, content);
            File.SetLastWriteTimeUtc(homeFile, homeTimestamp);
            File.SetLastWriteTimeUtc(workFile, workTimestamp);
        }

        var profileId = Guid.NewGuid();
        var homeProfile = ValidShuttleProfile(profileId, homeSource, shuttleRoot);
        var workProfile = ValidShuttleProfile(profileId, workSource, shuttleRoot);

        var home = new ShuttleService(new MachineIdentity("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "HOME"));
        var work = new ShuttleService(new MachineIdentity("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", "WORK"));

        var firstPrepareStopwatch = Stopwatch.StartNew();
        var prepare = await home.PrepareAsync(homeProfile);
        firstPrepareStopwatch.Stop();
        Assert(prepare.Succeeded, prepare.Message);
        Assert(prepare.Manifest?.Entries.Count == fileCount, $"Expected {fileCount} manifest entries.");

        var secondPrepareStopwatch = Stopwatch.StartNew();
        var secondPrepare = await home.PrepareAsync(homeProfile);
        secondPrepareStopwatch.Stop();
        Assert(secondPrepare.Succeeded, secondPrepare.Message);
        Assert(secondPrepare.Manifest?.SkippedFiles == fileCount, $"Expected {fileCount} skipped files on second prepare.");

        var depart = await home.DepartAsync(homeProfile);
        Assert(depart.Succeeded, depart.Message);

        var stopwatch = Stopwatch.StartNew();
        var dock = await work.DockAsync(workProfile);
        stopwatch.Stop();

        Assert(dock.Succeeded, dock.Message);
        Assert(dock.Manifest?.SkippedFiles == fileCount, $"Expected {fileCount} skipped files.");
        Assert(dock.Manifest?.ChangedFiles == 0, "Expected no changed files.");
        Assert(dock.Manifest?.ConflictFiles == 0, "Expected no conflicts.");
        Console.WriteLine($"INFO long shuttle first prepare staged {fileCount} files in {firstPrepareStopwatch.Elapsed.TotalSeconds:0.00}s.");
        Console.WriteLine($"INFO long shuttle second prepare skipped {fileCount} files in {secondPrepareStopwatch.Elapsed.TotalSeconds:0.00}s.");
        Console.WriteLine($"INFO long shuttle dock analyzed {fileCount} skipped files in {stopwatch.Elapsed.TotalSeconds:0.00}s.");
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

static async Task ShuttlePrepareReportsFileProgress()
{
    var root = Path.Combine(Environment.CurrentDirectory, "test-artifacts", Guid.NewGuid().ToString("N"));
    var source = Path.Combine(root, "source");
    var shuttleRoot = Path.Combine(root, "external", "Replicator", "shuttle", "repo");

    Directory.CreateDirectory(source);

    try
    {
        File.WriteAllText(Path.Combine(source, "one.md"), "one");
        Directory.CreateDirectory(Path.Combine(source, "notes"));
        File.WriteAllText(Path.Combine(source, "notes", "two.md"), "two");

        var profile = ValidShuttleProfile(Guid.NewGuid(), source, shuttleRoot);
        var service = new ShuttleService(new MachineIdentity("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "HOME"));
        var progressEvents = new System.Collections.Concurrent.ConcurrentQueue<ShuttleOperationProgress>();
        var progress = new Progress<ShuttleOperationProgress>(progressEvents.Enqueue);

        var result = await service.PrepareAsync(profile, progress);
        for (var attempt = 0; attempt < 20 && !progressEvents.Any(item => item.Operation == ShuttleOperationKind.Prepare && item.TotalFiles == 2 && item.ProcessedFiles == 2); attempt++)
        {
            await Task.Delay(10);
        }

        Assert(result.Succeeded, result.Message);
        Assert(progressEvents.Any(item => item.Operation == ShuttleOperationKind.Prepare && item.TotalFiles == 2), "Expected prepare progress to include total file count.");

        var maxProcessed = progressEvents
            .Where(item => item.Operation == ShuttleOperationKind.Prepare && item.TotalFiles == 2)
            .Max(item => item.ProcessedFiles);

        Assert(maxProcessed == 2, $"Expected max processed file count to be 2, got {maxProcessed}.");
        Assert(progressEvents.Any(item => item.Operation == ShuttleOperationKind.Prepare && item.PercentComplete == 100), "Expected a 100% progress event.");
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

static async Task ShuttlePrepareReportsProgressForSkippedFiles()
{
    var root = Path.Combine(Environment.CurrentDirectory, "test-artifacts", Guid.NewGuid().ToString("N"));
    var source = Path.Combine(root, "source");
    var shuttleRoot = Path.Combine(root, "external", "Replicator", "shuttle", "repo");

    Directory.CreateDirectory(source);

    try
    {
        File.WriteAllText(Path.Combine(source, "one.md"), "one");
        File.WriteAllText(Path.Combine(source, "two.md"), "two");

        var profile = ValidShuttleProfile(Guid.NewGuid(), source, shuttleRoot);
        var service = new ShuttleService(new MachineIdentity("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "HOME"));
        var firstPrepare = await service.PrepareAsync(profile);
        Assert(firstPrepare.Succeeded, firstPrepare.Message);

        var progressEvents = new System.Collections.Concurrent.ConcurrentQueue<ShuttleOperationProgress>();
        var progress = new Progress<ShuttleOperationProgress>(progressEvents.Enqueue);

        var secondPrepare = await service.PrepareAsync(profile, progress);
        for (var attempt = 0; attempt < 20 && !progressEvents.Any(item => item.Operation == ShuttleOperationKind.Prepare && item.TotalFiles == 2 && item.ProcessedFiles == 2); attempt++)
        {
            await Task.Delay(10);
        }

        Assert(secondPrepare.Succeeded, secondPrepare.Message);
        Assert(secondPrepare.Manifest?.SkippedFiles == 2, "Expected second prepare to skip unchanged files.");
        Assert(progressEvents.Any(item => item.Operation == ShuttleOperationKind.Prepare && item.TotalFiles == 2 && item.ProcessedFiles == 1), "Expected prepare progress after the first skipped file.");
        Assert(progressEvents.Any(item => item.Operation == ShuttleOperationKind.Prepare && item.TotalFiles == 2 && item.ProcessedFiles == 2), "Expected prepare progress to complete skipped files.");
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

static async Task ShuttleOperationsHonorCancellation()
{
    var root = Path.Combine(Environment.CurrentDirectory, "test-artifacts", Guid.NewGuid().ToString("N"));
    var source = Path.Combine(root, "source");
    var shuttleRoot = Path.Combine(root, "external", "Replicator", "shuttle", "repo");

    Directory.CreateDirectory(source);

    try
    {
        File.WriteAllText(Path.Combine(source, "one.md"), "one");

        var profile = ValidShuttleProfile(Guid.NewGuid(), source, shuttleRoot);
        var service = new ShuttleService(new MachineIdentity("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "HOME"));

        await AssertCanceled(
            token => service.PrepareAsync(profile, progress: null, cancellationToken: token),
            "Expected Prepare Shuttle to honor cancellation.");
        await AssertCanceled(
            token => service.DepartAsync(profile, progress: null, cancellationToken: token),
            "Expected Depart to honor cancellation.");
        await AssertCanceled(
            token => service.DockAsync(profile, progress: null, cancellationToken: token),
            "Expected Dock Shuttle to honor cancellation.");
        await AssertCanceled(
            token => service.ReceiveAsync(profile, progress: null, cancellationToken: token),
            "Expected Receive Changes to honor cancellation.");

        var manifestsDirectory = Path.Combine(shuttleRoot, "manifests");
        var manifestCount = Directory.Exists(manifestsDirectory)
            ? Directory.EnumerateFiles(manifestsDirectory, "*.json").Count()
            : 0;

        Assert(manifestCount == 0, "Expected canceled prepare to avoid writing manifests.");
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

static async Task ShuttlePrepareBlocksMissingSourceBeforeCreatingShuttleDirectories()
{
    var root = Path.Combine(Environment.CurrentDirectory, "test-artifacts", Guid.NewGuid().ToString("N"));
    var source = Path.Combine(root, "missing-source");
    var shuttleRoot = Path.Combine(root, "external", "Replicator", "shuttle", "repo");
    Directory.CreateDirectory(root);

    try
    {
        var profile = ValidShuttleProfile(Guid.NewGuid(), source, shuttleRoot);
        var service = new ShuttleService(new MachineIdentity("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "HOME"));

        var prepare = await service.PrepareAsync(profile);

        Assert(!prepare.Succeeded, "Expected Prepare Shuttle to block a missing source.");
        Assert(prepare.Message.Contains("Source path is unavailable", StringComparison.OrdinalIgnoreCase), $"Unexpected prepare message: {prepare.Message}");
        Assert(!Directory.Exists(shuttleRoot), "Prepare Shuttle should not create shuttle directories after an availability error.");
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

static async Task ShuttleReceiveBlocksMissingSourceWithoutCreatingIt()
{
    var root = Path.Combine(Environment.CurrentDirectory, "test-artifacts", Guid.NewGuid().ToString("N"));
    var homeSource = Path.Combine(root, "home-source");
    var workSource = Path.Combine(root, "missing-work-source");
    var shuttleRoot = Path.Combine(root, "external", "Replicator", "shuttle", "repo");
    Directory.CreateDirectory(homeSource);

    try
    {
        var profileId = Guid.NewGuid();
        File.WriteAllText(Path.Combine(homeSource, "one.md"), "one");

        var homeProfile = ValidShuttleProfile(profileId, homeSource, shuttleRoot);
        var workProfile = ValidShuttleProfile(profileId, workSource, shuttleRoot);
        var home = new ShuttleService(new MachineIdentity("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "HOME"));
        var work = new ShuttleService(new MachineIdentity("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", "WORK"));

        var prepare = await home.PrepareAsync(homeProfile);
        Assert(prepare.Succeeded, $"Expected prepare to seed inbound shuttle payload: {prepare.Message}");
        var depart = await home.DepartAsync(homeProfile);
        Assert(depart.Succeeded, $"Expected depart to mark inbound shuttle payload ready: {depart.Message}");

        var receive = await work.ReceiveAsync(workProfile);

        Assert(!receive.Succeeded, "Expected Receive Changes to block a missing source.");
        Assert(receive.Message.Contains("Source path is unavailable", StringComparison.OrdinalIgnoreCase), $"Unexpected receive message: {receive.Message}");
        Assert(!Directory.Exists(workSource), "Receive Changes should not create a missing source path after an availability error.");
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

static async Task ShuttlePrepareExpandsEnvironmentVariableSourcePath()
{
    var root = Path.Combine(Environment.CurrentDirectory, "test-artifacts", Guid.NewGuid().ToString("N"));
    var source = Path.Combine(root, "source");
    var shuttleRoot = Path.Combine(root, "external", "Replicator", "shuttle", "repo");
    const string variableName = "REPLICATOR_TEST_SOURCE";
    var previousValue = Environment.GetEnvironmentVariable(variableName);

    Directory.CreateDirectory(source);

    try
    {
        File.WriteAllText(Path.Combine(source, "one.md"), "one");
        Environment.SetEnvironmentVariable(variableName, source);

        var profile = ValidShuttleProfile(Guid.NewGuid(), $"%{variableName}%", shuttleRoot);
        var service = new ShuttleService(new MachineIdentity("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "HOME"));

        var prepare = await service.PrepareAsync(profile);

        Assert(prepare.Succeeded, $"Expected Prepare Shuttle to expand environment variable source paths: {prepare.Message}");
        Assert(prepare.Manifest?.SourcePath == Path.GetFullPath(source), "Expected manifest source path to be expanded.");
        Assert(File.Exists(Path.Combine(shuttleRoot, "payload", "one.md")), "Expected staged payload file from expanded source path.");
    }
    finally
    {
        Environment.SetEnvironmentVariable(variableName, previousValue);
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

static async Task ShuttleMetadataOperationsBlockMissingSource()
{
    var root = Path.Combine(Environment.CurrentDirectory, "test-artifacts", Guid.NewGuid().ToString("N"));
    var source = Path.Combine(root, "missing-source");
    var shuttleRoot = Path.Combine(root, "external", "Replicator", "shuttle", "repo");
    Directory.CreateDirectory(root);

    try
    {
        var profile = ValidShuttleProfile(Guid.NewGuid(), source, shuttleRoot);
        var service = new ShuttleService(new MachineIdentity("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "HOME"));

        var depart = await service.DepartAsync(profile);
        var dock = await service.DockAsync(profile);

        Assert(!depart.Succeeded, "Expected Depart to block a missing source.");
        Assert(depart.Message.Contains("Source path is unavailable", StringComparison.OrdinalIgnoreCase), $"Unexpected depart message: {depart.Message}");
        Assert(!dock.Succeeded, "Expected Dock Shuttle to block a missing source.");
        Assert(dock.Message.Contains("Source path is unavailable", StringComparison.OrdinalIgnoreCase), $"Unexpected dock message: {dock.Message}");
        Assert(!Directory.Exists(shuttleRoot), "Metadata shuttle operations should not create shuttle directories after an availability error.");
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

static async Task AssertCanceled(Func<CancellationToken, Task> operation, string message)
{
    using var cancellation = new CancellationTokenSource();
    cancellation.Cancel();

    try
    {
        await operation(cancellation.Token);
        throw new InvalidOperationException(message);
    }
    catch (OperationCanceledException)
    {
        // Expected path.
    }
}

static Task ScheduledTaskNamesAreScoped()
{
    var profile = ValidProfile();
    profile.Id = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
    profile.Name = "AI Scratch Repo Backup!";

    var taskName = ScheduledTaskName.ForProfile(profile);

    Assert(taskName == @"\Replicator\AI-Scratch-Repo-Backup-00112233", $"Unexpected task name: {taskName}");
    return Task.CompletedTask;
}

static Task ScheduledTaskActionUsesHiddenWindowlessLauncher()
{
    var profile = ValidProfile();
    var arguments = BuildScheduledTaskArguments(profile);
    var taskRunIndex = arguments.ToList().IndexOf("/TR");
    Assert(taskRunIndex >= 0 && taskRunIndex + 1 < arguments.Count, "Expected scheduled task run command.");

    var taskRunCommand = arguments[taskRunIndex + 1];
    var expectedLauncher = Path.ChangeExtension(@"C:\Replicator\profile.ps1", ".vbs");

    Assert(taskRunCommand.StartsWith("wscript.exe ", StringComparison.OrdinalIgnoreCase), $"Expected scheduled task to use windowless WScript host: {taskRunCommand}");
    Assert(taskRunCommand.Contains("//B", StringComparison.OrdinalIgnoreCase), $"Expected WScript batch mode: {taskRunCommand}");
    Assert(taskRunCommand.Contains("//Nologo", StringComparison.OrdinalIgnoreCase), $"Expected WScript nologo mode: {taskRunCommand}");
    Assert(taskRunCommand.Contains($"\"{expectedLauncher}\"", StringComparison.OrdinalIgnoreCase), $"Expected launcher path in task action: {taskRunCommand}");
    Assert(!taskRunCommand.Contains("powershell.exe", StringComparison.OrdinalIgnoreCase), $"Expected scheduled task action not to start console PowerShell directly: {taskRunCommand}");
    return Task.CompletedTask;
}

static Task ScheduledTaskActionInspectorFlagsVisiblePowerShellActions()
{
    var scriptPath = Path.Combine(Path.GetTempPath(), $"replicator-{Guid.NewGuid():N}.ps1");
    File.WriteAllText(scriptPath, "# test");

    try
    {
        var action = $"powershell.exe -NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"";

        var health = ScheduledTaskActionInspector.Inspect(action, scriptPath);

        Assert(health.NeedsRepair, "Expected visible/noninteractive-missing action to need repair.");
        Assert(health.ScriptPath == scriptPath, $"Unexpected script path: {health.ScriptPath}");
        Assert(health.ScriptExists, "Expected script to exist.");
        Assert(health.RepairReasons.Any(reason => reason.Contains("-WindowStyle Hidden", StringComparison.OrdinalIgnoreCase)), "Expected hidden-window repair reason.");
        Assert(health.RepairReasons.Any(reason => reason.Contains("-NonInteractive", StringComparison.OrdinalIgnoreCase)), "Expected noninteractive repair reason.");
    }
    finally
    {
        if (File.Exists(scriptPath))
        {
            File.Delete(scriptPath);
        }
    }

    return Task.CompletedTask;
}

static Task ScheduledTaskActionInspectorFlagsConsolePowerShellActionsEvenWhenHidden()
{
    var scriptPath = Path.Combine(Path.GetTempPath(), $"replicator-{Guid.NewGuid():N}.ps1");
    File.WriteAllText(scriptPath, "# test");

    try
    {
        var action = $"powershell.exe -NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File \"{scriptPath}\"";

        var health = ScheduledTaskActionInspector.Inspect(action, scriptPath);

        Assert(health.NeedsRepair, "Expected console PowerShell action to need repair even when it asks for a hidden window.");
        Assert(health.ScriptPath == scriptPath, $"Unexpected script path: {health.ScriptPath}");
        Assert(health.ScriptExists, "Expected script to exist.");
        Assert(health.RepairReasons.Any(reason => reason.Contains("windowless launcher", StringComparison.OrdinalIgnoreCase)), "Expected windowless-launcher repair reason.");
    }
    finally
    {
        if (File.Exists(scriptPath))
        {
            File.Delete(scriptPath);
        }
    }

    return Task.CompletedTask;
}

static Task ScheduledTaskActionInspectorFlagsMismatchedScriptPaths()
{
    var expected = Path.Combine(Path.GetTempPath(), $"expected-{Guid.NewGuid():N}.ps1");
    var actual = Path.Combine(Path.GetTempPath(), $"actual-{Guid.NewGuid():N}.ps1");
    File.WriteAllText(actual, "# test");

    try
    {
        var action = $"powershell.exe -NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File \"{actual}\"";

        var health = ScheduledTaskActionInspector.Inspect(action, expected);

        Assert(health.NeedsRepair, "Expected mismatched script path to need repair.");
        Assert(health.ScriptPath == actual, $"Unexpected parsed script path: {health.ScriptPath}");
        Assert(health.ScriptExists, "Expected actual script to exist.");
        Assert(health.RepairReasons.Any(reason => reason.Contains("does not match", StringComparison.OrdinalIgnoreCase)), "Expected path mismatch repair reason.");
    }
    finally
    {
        if (File.Exists(actual))
        {
            File.Delete(actual);
        }
    }

    return Task.CompletedTask;
}

static async Task ScheduledTaskQueryReportsRepairReasons()
{
    var expectedScript = Path.Combine(Path.GetTempPath(), $"expected-{Guid.NewGuid():N}.ps1");
    var actualScript = Path.Combine(Path.GetTempPath(), $"actual-{Guid.NewGuid():N}.ps1");
    File.WriteAllText(actualScript, "# test");

    try
    {
        var output = $"""

Folder: \Replicator
HostName:                             DEVBOX
TaskName:                             \Replicator\Back-up-Personal-Dev-58baefdb
Next Run Time:                        5/21/2026 2:00:00 PM
Status:                               Ready
Logon Mode:                           Interactive/Background
Last Run Time:                        5/21/2026 1:00:00 PM
Last Result:                          0
Task To Run:                          powershell.exe -NoProfile -ExecutionPolicy Bypass -File "{actualScript}"

""";
        var service = new WindowsScheduledTaskService(new FakeProcessRunner(new ProcessResult(0, output, string.Empty)));

        var snapshot = await service.QueryAsync(ValidProfile(), expectedScript);

        Assert(snapshot.NeedsRepair, "Expected visible or mismatched action to need repair.");
        Assert(snapshot.TaskToRun.Contains(actualScript, StringComparison.OrdinalIgnoreCase), "Expected raw task action to be captured.");
        Assert(snapshot.ScriptPath == actualScript, $"Unexpected script path: {snapshot.ScriptPath}");
        Assert(snapshot.ScriptExists, "Expected parsed task script to exist.");
        Assert(snapshot.RepairReasons.Any(reason => reason.Contains("-WindowStyle Hidden", StringComparison.OrdinalIgnoreCase)), "Expected missing hidden-window repair reason.");
        Assert(snapshot.RepairReasons.Any(reason => reason.Contains("-NonInteractive", StringComparison.OrdinalIgnoreCase)), "Expected missing noninteractive repair reason.");
        Assert(snapshot.RepairReasons.Any(reason => reason.Contains("does not match", StringComparison.OrdinalIgnoreCase)), "Expected script path mismatch reason.");
    }
    finally
    {
        if (File.Exists(actualScript))
        {
            File.Delete(actualScript);
        }
    }
}

static async Task ScheduledTaskInventoryClassifiesMatchedRepairAndOrphanedTasks()
{
    var profile = ValidProfile();
    var expectedScript = Path.Combine(Path.GetTempPath(), $"expected-{Guid.NewGuid():N}.ps1");
    var orphanScript = Path.Combine(Path.GetTempPath(), $"orphan-{Guid.NewGuid():N}.ps1");
    File.WriteAllText(orphanScript, "# orphan");

    try
    {
        var output = $"""
TaskName:                             {ScheduledTaskName.ForProfile(profile)}
Next Run Time:                        5/21/2026 2:00:00 PM
Status:                               Ready
Last Run Time:                        5/21/2026 1:00:00 PM
Last Result:                          0
Task To Run:                          powershell.exe -NoProfile -ExecutionPolicy Bypass -File "{orphanScript}"

TaskName:                             \Replicator\Legacy-Replicator-Task
Next Run Time:                        5/21/2026 3:00:00 PM
Status:                               Ready
Last Run Time:                        5/21/2026 12:00:00 PM
Last Result:                          0
Task To Run:                          powershell.exe -NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File "{orphanScript}"
""";
        var service = new WindowsScheduledTaskInventoryService(new FakeProcessRunner(new ProcessResult(0, output, string.Empty)));

        var result = await service.ScanAsync([profile], new Dictionary<Guid, string> { [profile.Id] = expectedScript });

        Assert(result.Summary.Total == 2, $"Expected two inventory rows, got {result.Summary.Total}.");
        Assert(result.Summary.NeedsRepair == 1, $"Expected one repair row, got {result.Summary.NeedsRepair}.");
        Assert(result.Summary.Orphaned == 1, $"Expected one orphan row, got {result.Summary.Orphaned}.");
        Assert(result.Summary.HasIssues, "Expected summary to show issues.");

        var matched = result.Items.Single(item => item.ProfileId == profile.Id);
        Assert(matched.InventoryState == ScheduledTaskInventoryState.NeedsRepair, $"Unexpected matched state: {matched.InventoryState}.");
        Assert(matched.CanRepair, "Expected matched unhealthy task to be repairable.");
        Assert(matched.Reason.Contains("-WindowStyle Hidden", StringComparison.OrdinalIgnoreCase), $"Unexpected repair reason: {matched.Reason}");

        var orphan = result.Items.Single(item => item.InventoryState == ScheduledTaskInventoryState.Orphaned);
        Assert(orphan.ProfileId is null, "Expected orphan row to have no profile id.");
        Assert(!orphan.CanRepair, "Orphaned tasks should not be repairable in this slice.");
    }
    finally
    {
        if (File.Exists(orphanScript))
        {
            File.Delete(orphanScript);
        }
    }
}

static async Task ScheduledTaskInventoryDisablesRepairForRunningTasks()
{
    var profile = ValidProfile();
    var script = Path.Combine(Path.GetTempPath(), $"running-{Guid.NewGuid():N}.ps1");
    File.WriteAllText(script, "# running");

    try
    {
        var output = $"""
TaskName:                             {ScheduledTaskName.ForProfile(profile)}
Next Run Time:                        N/A
Status:                               Running
Last Run Time:                        5/21/2026 1:00:00 PM
Last Result:                          267009
Task To Run:                          powershell.exe -NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File "{script}"
""";
        var service = new WindowsScheduledTaskInventoryService(new FakeProcessRunner(new ProcessResult(0, output, string.Empty)));

        var result = await service.ScanAsync([profile], new Dictionary<Guid, string> { [profile.Id] = script });

        var item = result.Items.Single();
        Assert(item.InventoryState == ScheduledTaskInventoryState.Running, $"Unexpected state: {item.InventoryState}.");
        Assert(!item.CanRepair, "Running tasks should not be repairable.");
        Assert(result.Summary.Running == 1, $"Expected one running task, got {result.Summary.Running}.");
    }
    finally
    {
        if (File.Exists(script))
        {
            File.Delete(script);
        }
    }
}

static Task ScheduledTaskIssueSelectorIgnoresOtherProfileRepairs()
{
    var selectedProfileId = Guid.NewGuid();
    var repairProfileId = Guid.NewGuid();
    var selectedReady = new ScheduledTaskInventoryItem(
        @"\Replicator\Selected-Profile",
        selectedProfileId,
        "Selected Profile",
        ScheduledTaskInventoryState.Ready,
        ScheduledTaskState.Ready,
        "6/1/2026 2:00:00 PM",
        "6/1/2026 1:00:00 PM",
        0,
        "wscript.exe //B //Nologo \"C:\\Replicator\\selected.vbs\"",
        @"C:\Replicator\selected.ps1",
        true,
        [],
        "Task action is current.",
        string.Empty);
    var otherRepair = new ScheduledTaskInventoryItem(
        @"\Replicator\Other-Profile",
        repairProfileId,
        "Other Profile",
        ScheduledTaskInventoryState.NeedsRepair,
        ScheduledTaskState.Ready,
        "6/1/2026 2:00:00 PM",
        "6/1/2026 1:00:00 PM",
        0,
        "powershell.exe -NoProfile -ExecutionPolicy Bypass -File \"C:\\Replicator\\other.ps1\"",
        @"C:\Replicator\other.ps1",
        true,
        ["Task action uses the console PowerShell host; repair to the windowless launcher."],
        "Task action uses the console PowerShell host; repair to the windowless launcher.",
        string.Empty);
    var inventory = new ScheduledTaskInventoryResult(
        [selectedReady, otherRepair],
        new ScheduledTaskInventorySummary(2, 1, 1, 0, 0, 0),
        string.Empty);

    var selectedIssue = ScheduledTaskInventoryIssueSelector.ForProfile(inventory, selectedProfileId);
    var repairIssue = ScheduledTaskInventoryIssueSelector.ForProfile(inventory, repairProfileId);

    Assert(selectedIssue is null, "Selected ready profile should not show a repair flag because another profile needs repair.");
    Assert(repairIssue == otherRepair, "Expected repair issue only when the repairable profile is selected.");
    return Task.CompletedTask;
}

static async Task ScheduledTaskInventoryReportsQueryFailureAsUnknown()
{
    var service = new WindowsScheduledTaskInventoryService(new FakeProcessRunner(new ProcessResult(1, string.Empty, "Access is denied.")));

    var result = await service.ScanAsync([ValidProfile()], new Dictionary<Guid, string>());

    Assert(result.Summary.Total == 1, $"Expected one unknown inventory row, got {result.Summary.Total}.");
    Assert(result.Summary.Unknown == 1, $"Expected one unknown row, got {result.Summary.Unknown}.");
    Assert(result.Summary.HasIssues, "Expected query failure to show an Action Center issue.");
    Assert(result.Items[0].InventoryState == ScheduledTaskInventoryState.Unknown, $"Unexpected state: {result.Items[0].InventoryState}.");
    Assert(result.Items[0].Reason.Contains("Access is denied", StringComparison.OrdinalIgnoreCase), $"Unexpected reason: {result.Items[0].Reason}");
}

static Task MainWindowStatusTextIsLayoutBounded()
{
    var xamlPath = Path.Combine(Environment.CurrentDirectory, "src", "Replicator.App", "MainWindow.xaml");
    var document = XDocument.Load(xamlPath);
    XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    var statusInfoBar = document
        .Descendants(presentation + "InfoBar")
        .Single(element => ((string?)element.Attribute("Title"))?.Contains("Status.Text", StringComparison.Ordinal) == true);

    Assert(statusInfoBar.Attribute("MaxWidth") is not null, "Status InfoBar must have MaxWidth so long errors cannot consume the header grid.");
    Assert((string?)statusInfoBar.Attribute("IsClosable") == "False", "Status InfoBar must remain a stable status surface.");
    return Task.CompletedTask;
}

static Task MinuteSchedulesEmitSchtasksMinuteCadence()
{
    var profile = ValidProfile();
    profile.Schedule.Cadence = ScheduleCadence.Minutes;
    profile.Schedule.IntervalMinutes = 15;

    var arguments = BuildScheduledTaskArguments(profile);

    Assert(arguments.Contains("/SC"), "Expected schtasks schedule switch.");
    Assert(arguments.Contains("MINUTE"), "Expected minute schedule cadence.");
    Assert(arguments.Contains("/MO"), "Expected schedule modifier switch.");
    Assert(arguments.Contains("15"), "Expected 15-minute schedule modifier.");
    return Task.CompletedTask;
}

static Task DefaultProfileHasDevelopmentExcludes()
{
    var profile = BackupProfileFactory.CreateDefault();

    Assert(profile.DryRun, "Expected new profiles to default to dry-run.");
    Assert(profile.ExcludePatterns.Contains("node_modules"), "Expected node_modules exclude.");
    Assert(profile.ExcludePatterns.Contains("bin"), "Expected bin exclude.");
    Assert(profile.ExcludePatterns.Contains("obj"), "Expected obj exclude.");
    Assert(profile.ExcludePatterns.Contains(".replicator-conflicts"), "Expected conflict folder exclude.");

    return Task.CompletedTask;
}

static Task ValidatorRejectsInvalidMinuteInterval()
{
    var profile = ValidProfile();
    profile.Schedule.Cadence = ScheduleCadence.Minutes;
    profile.Schedule.IntervalMinutes = 0;

    var issues = BackupProfileValidator.Validate(profile);

    Assert(issues.Any(issue => issue.Field == "IntervalMinutes"), "Expected invalid minute interval validation issue.");
    return Task.CompletedTask;
}

static Task AvailabilityCheckerReportsMissingSourceAndCreatableTarget()
{
    var root = Path.Combine(Environment.CurrentDirectory, "test-artifacts", Guid.NewGuid().ToString("N"));
    var source = Path.Combine(root, "missing-source");
    var destination = Path.Combine(root, "backup", "repo");

    Directory.CreateDirectory(root);

    try
    {
        var profile = ValidProfile();
        profile.SourcePath = source;
        profile.Target.Path = destination;

        var report = new ProfileAvailabilityChecker().Check(profile);

        Assert(report.HasErrors, "Expected missing source to be an availability error.");
        Assert(report.HasWarnings, "Expected missing but creatable target to be an availability warning.");
        Assert(report.Items.Any(item => item.Label == "Source" && item.State == PathAvailabilityState.Missing), "Expected missing source state.");
        Assert(report.Items.Any(item => item.Label == "Target" && item.State == PathAvailabilityState.Creatable), "Expected creatable target state.");
        Assert(report.Summary.Contains("Source path is unavailable", StringComparison.OrdinalIgnoreCase), $"Unexpected summary: {report.Summary}");
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    return Task.CompletedTask;
}

static Task AvailabilityCheckerReportsUnavailableDrive()
{
    var unavailableRoot = FindUnavailableDriveRoot();
    if (unavailableRoot is null)
    {
        Console.WriteLine("SKIP no unused Windows drive letter available for unavailable-drive availability check");
        return Task.CompletedTask;
    }

    var profile = ValidProfile();
    profile.SourcePath = Path.Combine(unavailableRoot, "source");
    profile.Target.Path = Path.Combine(unavailableRoot, "target");

    var report = new ProfileAvailabilityChecker().Check(profile);

    Assert(report.HasErrors, "Expected unavailable drive to be an availability error.");
    Assert(report.Items.Any(item => item.State == PathAvailabilityState.DriveUnavailable), "Expected drive unavailable state.");
    Assert(report.Summary.Contains("drive is unavailable", StringComparison.OrdinalIgnoreCase), $"Unexpected summary: {report.Summary}");
    return Task.CompletedTask;
}

static Task BitLockerParserClassifiesProtectedUnprotectedAndLockedDrives()
{
    const string protectedJson = """
        {"MountPoint":"D:","VolumeStatus":"FullyEncrypted","ProtectionStatus":"On","LockStatus":"Unlocked","EncryptionPercentage":100}
        """;
    const string unprotectedJson = """
        {"MountPoint":"E:","VolumeStatus":"FullyDecrypted","ProtectionStatus":"Off","LockStatus":"Unlocked","EncryptionPercentage":0}
        """;
    const string lockedJson = """
        {"MountPoint":"F:","VolumeStatus":"FullyEncrypted","ProtectionStatus":"On","LockStatus":"Locked","EncryptionPercentage":100}
        """;

    Assert(BitLockerStatusParser.TryParseJson(protectedJson, out var protectedStatus), "Expected protected BitLocker JSON to parse.");
    Assert(BitLockerStatusParser.TryParseJson(unprotectedJson, out var unprotectedStatus), "Expected unprotected BitLocker JSON to parse.");
    Assert(BitLockerStatusParser.TryParseJson(lockedJson, out var lockedStatus), "Expected locked BitLocker JSON to parse.");

    var protectedItem = BitLockerStatusParser.ToSecurityItem("Target drive", @"D:\backup", @"D:\", protectedStatus);
    var unprotectedItem = BitLockerStatusParser.ToSecurityItem("Target drive", @"E:\backup", @"E:\", unprotectedStatus);
    var lockedItem = BitLockerStatusParser.ToSecurityItem("Shuttle drive", @"F:\Replicator\shuttle", @"F:\", lockedStatus);

    Assert(protectedItem.State == DriveSecurityState.Protected, "Expected protected state.");
    Assert(protectedItem.Severity == DriveSecuritySeverity.Info, "Expected protected drive to be informational.");
    Assert(unprotectedItem.State == DriveSecurityState.Unprotected, "Expected unprotected state.");
    Assert(unprotectedItem.Severity == DriveSecuritySeverity.Warning, "Expected unprotected drive to warn.");
    Assert(lockedItem.State == DriveSecurityState.Locked, "Expected locked state.");
    Assert(lockedItem.Severity == DriveSecuritySeverity.Error, "Expected locked drive to error.");
    return Task.CompletedTask;
}

static Task BitLockerAccessDeniedIsClassifiedAsPermissionRequired()
{
    var item = BitLockerQueryFailureClassifier.ToSecurityItem(
        "Source drive",
        @"D:\repos\personal",
        @"D:\",
        "Get-CimInstance : Access denied");

    Assert(item.State == DriveSecurityState.PermissionRequired, $"Expected permission-required state, got {item.State}.");
    Assert(item.Severity == DriveSecuritySeverity.Warning, "Permission-required posture should warn without blocking.");
    Assert(item.Message.Contains(@"D:\", StringComparison.OrdinalIgnoreCase), $"Expected drive root in message: {item.Message}");
    Assert(item.Message.Contains("elevated permissions", StringComparison.OrdinalIgnoreCase), $"Expected elevation guidance: {item.Message}");
    Assert(!item.Message.Contains("Get-CimInstance", StringComparison.OrdinalIgnoreCase), $"Expected raw PowerShell command text to be hidden: {item.Message}");
    Assert(!item.Message.Contains("Access denied", StringComparison.OrdinalIgnoreCase), $"Expected raw access-denied text to be hidden: {item.Message}");
    return Task.CompletedTask;
}

static async Task PowerShellBitLockerProviderMapsAccessDeniedToPermissionRequired()
{
    if (!OperatingSystem.IsWindows())
    {
        return;
    }

    var runner = new FakeProcessRunner(new ProcessResult(1, "", "Get-CimInstance : Access denied"));
    var item = await new PowerShellBitLockerStatusProvider(runner).CheckAsync(
        "Source drive",
        @"D:\repos\personal",
        @"D:\");

    Assert(item.State == DriveSecurityState.PermissionRequired, $"Expected permission-required state, got {item.State}.");
    Assert(item.Severity == DriveSecuritySeverity.Warning, "Permission-required provider result should warn without blocking.");
    Assert(item.Message.Contains("elevated permissions", StringComparison.OrdinalIgnoreCase), $"Expected elevation guidance: {item.Message}");
    Assert(!item.Message.Contains("Get-CimInstance", StringComparison.OrdinalIgnoreCase), $"Expected raw command details to be hidden: {item.Message}");
}

static Task DriveSecurityReportTreatsPermissionRequiredAsWarning()
{
    var report = new ProfileDriveSecurityReport(
    [
        new DriveSecurityItem(
            "Source drive",
            @"D:\repos\personal",
            @"D:\",
            DriveSecurityState.PermissionRequired,
            DriveSecuritySeverity.Warning,
            @"Drive security: Source drive BitLocker status requires elevated permissions (D:\). Replicator can continue, but encryption state was not confirmed."),
        new DriveSecurityItem(
            "Target drive",
            @"H:\dev\personal",
            @"H:\",
            DriveSecurityState.Protected,
            DriveSecuritySeverity.Info,
            @"Drive security: Target drive is BitLocker protected (H:\).")
    ]);

    Assert(report.HasWarnings, "Permission-required posture should set warning status.");
    Assert(!report.HasErrors, "Permission-required posture should not be an error while drive-security is visibility-only.");
    Assert(report.Summary.Contains("requires elevated permissions", StringComparison.OrdinalIgnoreCase), $"Unexpected summary: {report.Summary}");
    return Task.CompletedTask;
}

static Task DriveSecurityReportMarksPermissionRequiredChecksAsElevationReady()
{
    var report = new ProfileDriveSecurityReport(
    [
        new DriveSecurityItem(
            "Source drive",
            @"D:\repos\personal",
            @"D:\",
            DriveSecurityState.PermissionRequired,
            DriveSecuritySeverity.Warning,
            @"Drive security: Source drive BitLocker status requires elevated permissions (D:\). Replicator can continue, but encryption state was not confirmed.")
    ]);

    Assert(report.RequiresElevation, "Expected permission-required drive security report to expose an elevated retry action.");
    return Task.CompletedTask;
}

static async Task DriveSecurityCacheWarmsUniqueRootsAcrossProfiles()
{
    var first = ValidProfile();
    first.SourcePath = @"C:\work\alpha";
    first.Target.Path = @"D:\backup\alpha";

    var second = ValidProfile();
    second.SourcePath = @"C:\work\beta";
    second.Target.Path = @"E:\backup\beta";

    var provider = new CountingBitLockerStatusProvider();
    provider.Results[@"C:\"] = new DriveSecurityItem(
        "Cached drive",
        @"C:\",
        @"C:\",
        DriveSecurityState.Protected,
        DriveSecuritySeverity.Info,
        @"Drive security: Cached drive is BitLocker protected (C:\).");
    provider.Results[@"D:\"] = new DriveSecurityItem(
        "Cached drive",
        @"D:\",
        @"D:\",
        DriveSecurityState.Protected,
        DriveSecuritySeverity.Info,
        @"Drive security: Cached drive is BitLocker protected (D:\).");
    provider.Results[@"E:\"] = new DriveSecurityItem(
        "Cached drive",
        @"E:\",
        @"E:\",
        DriveSecurityState.Unprotected,
        DriveSecuritySeverity.Warning,
        @"Drive security: Cached drive is not BitLocker protected (E:\).");

    var cache = new ProfileDriveSecurityCache();
    await cache.WarmMissingAsync([first, second], provider);

    Assert(provider.Calls.Count == 3, $"Expected one check per unique root, got {provider.Calls.Count}.");
    Assert(provider.Calls.Count(root => root == @"C:\") == 1, "Expected shared source root to be checked once.");

    var firstReport = cache.Report(first);
    var secondReport = cache.Report(second);

    Assert(firstReport.Items.Count == 2, "Expected cached source and target items for first profile.");
    Assert(secondReport.Items.Count == 2, "Expected cached source and target items for second profile.");
    Assert(secondReport.Summary.Contains("Target drive is not BitLocker protected", StringComparison.OrdinalIgnoreCase), $"Expected target-specific cached message: {secondReport.Summary}");

    await cache.WarmMissingAsync([first, second], provider);
    Assert(provider.Calls.Count == 3, $"Expected cached roots to avoid repeat checks, got {provider.Calls.Count} calls.");
}

static async Task DriveSecurityCacheRefreshesSelectedProfileRootsOnly()
{
    var selected = ValidProfile();
    selected.SourcePath = @"D:\repos\personal";
    selected.Target.Path = @"H:\dev\personal";

    var other = ValidProfile();
    other.SourcePath = @"F:\work";
    other.Target.Path = @"H:\work";

    var provider = new CountingBitLockerStatusProvider();
    provider.Results[@"D:\"] = new DriveSecurityItem(
        "Cached drive",
        @"D:\",
        @"D:\",
        DriveSecurityState.Unprotected,
        DriveSecuritySeverity.Warning,
        @"Drive security: Cached drive is not BitLocker protected (D:\).");
    provider.Results[@"H:\"] = new DriveSecurityItem(
        "Cached drive",
        @"H:\",
        @"H:\",
        DriveSecurityState.Unprotected,
        DriveSecuritySeverity.Warning,
        @"Drive security: Cached drive is not BitLocker protected (H:\).");
    provider.Results[@"F:\"] = new DriveSecurityItem(
        "Cached drive",
        @"F:\",
        @"F:\",
        DriveSecurityState.Protected,
        DriveSecuritySeverity.Info,
        @"Drive security: Cached drive is BitLocker protected (F:\).");

    var cache = new ProfileDriveSecurityCache();

    await cache.RefreshAsync(selected, provider);

    Assert(provider.Calls.SequenceEqual([@"D:\", @"H:\"], StringComparer.OrdinalIgnoreCase), $"Expected only selected profile roots, got {string.Join(", ", provider.Calls)}.");
}

static async Task DriveSecurityCachePreservesUnknownCheckFailureReason()
{
    var profile = ValidProfile();
    profile.SourcePath = @"D:\repos\personal";
    profile.Target.Path = @"D:\backups\personal";

    var provider = new FakeBitLockerStatusProvider();
    provider.Items[@"D:\"] = new DriveSecurityItem(
        "Source drive",
        profile.SourcePath,
        @"D:\",
        DriveSecurityState.Unknown,
        DriveSecuritySeverity.Warning,
        @"Drive security: Source drive BitLocker status unknown (D:\). Elevated BitLocker status check failed with exit code 1.");

    var cache = new ProfileDriveSecurityCache();

    await cache.RefreshAsync([profile], provider);
    var report = cache.Report(profile);

    Assert(report.Summary.Contains("exit code 1", StringComparison.OrdinalIgnoreCase), $"Expected unknown failure reason to be preserved: {report.Summary}");
}

static async Task ElevatedBitLockerProviderLaunchesEncodedAdminHelperAndParsesResultFile()
{
    if (!OperatingSystem.IsWindows())
    {
        return;
    }

    var root = Path.Combine(Environment.CurrentDirectory, "test-artifacts", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);

    try
    {
        var runner = new FakeElevatedProcessRunner(arguments =>
        {
            Assert(arguments.Contains("-WindowStyle"), "Expected elevated PowerShell to receive a window style argument.");
            Assert(arguments.Contains("Hidden"), "Expected elevated PowerShell to run hidden.");
            Assert(arguments.Contains("-EncodedCommand"), "Expected elevated launch to pass helper script as an encoded command.");
            Assert(!arguments.Any(argument => argument.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase)), "Elevated launch should not execute generated ps1 files.");
            Assert(!arguments.Any(argument => argument.EndsWith(".vbs", StringComparison.OrdinalIgnoreCase)), "Elevated launch should not execute generated vbs files.");
            Assert(!Directory.EnumerateFiles(root, "*.ps1").Any(), "Provider should not create executable ps1 helpers in the writable work directory.");
            Assert(!Directory.EnumerateFiles(root, "*.vbs").Any(), "Provider should not create executable vbs helpers in the writable work directory.");

            var requestPath = FindElevatedRequestPath(root);
            var requestsJson = File.ReadAllText(requestPath);
            Assert(requestsJson.Contains(@"""Root"":""D:\\""", StringComparison.Ordinal), $"Expected request file to contain D root: {requestsJson}");

            File.WriteAllText(OutputPathFromRequestPath(requestPath), """
                {"Root":"D:\\","Succeeded":true,"MountPoint":"D:","VolumeStatus":"FullyEncrypted","ProtectionStatus":"On","LockStatus":"Unlocked","EncryptionPercentage":100}
                """);

            return Task.FromResult(0);
        });

        var item = await new ElevatedPowerShellBitLockerStatusProvider(runner, root).CheckAsync(
            "Source drive",
            @"D:\repos\personal",
            @"D:\");

        Assert(item.State == DriveSecurityState.Protected, $"Expected protected state, got {item.State}.");
        Assert(runner.LastFileName.EndsWith("powershell.exe", StringComparison.OrdinalIgnoreCase), $"Unexpected elevated file: {runner.LastFileName}");
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

static async Task ElevatedBitLockerEncodedCommandRunsHelperScript()
{
    if (!OperatingSystem.IsWindows())
    {
        return;
    }

    var root = Path.Combine(Environment.CurrentDirectory, "test-artifacts", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);

    try
    {
        var requestsPath = Path.Combine(root, "requests.json");
        var outputPath = Path.Combine(root, "output.json");
        await File.WriteAllTextAsync(requestsPath, """[{"Root":"D:\\"}]""");
        var encodedCommand = BuildElevatedBitLockerEncodedCommand(requestsPath, outputPath);
        var command = string.Join(
            Environment.NewLine,
            [
                "function Get-BitLockerVolume {",
                "    param([string]$MountPoint)",
                "    [pscustomobject]@{",
                "        MountPoint = $MountPoint",
                "        VolumeStatus = 'FullyEncrypted'",
                "        ProtectionStatus = 'On'",
                "        LockStatus = 'Unlocked'",
                "        EncryptionPercentage = 100",
                "    }",
                "}",
                $"$decoded = [Text.Encoding]::Unicode.GetString([Convert]::FromBase64String('{encodedCommand}'))",
                "Invoke-Expression $decoded",
                "exit $LASTEXITCODE"
            ]);

        var result = await RunWindowsPowerShellAsync(command);

        Assert(result.ExitCode == 0, $"Expected encoded helper success. stdout: {result.StandardOutput} stderr: {result.StandardError}");
        Assert(File.Exists(outputPath), "Expected encoded helper to run PowerShell helper and write output.");
        var output = await File.ReadAllTextAsync(outputPath);
        Assert(output.Contains(@"""Root"":""D:\\""", StringComparison.Ordinal), $"Expected helper to receive request path and write BitLocker output, got: {output}");
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

static async Task ElevatedBitLockerProviderBatchesMultipleRootsIntoOneAdminLaunch()
{
    if (!OperatingSystem.IsWindows())
    {
        return;
    }

    var root = Path.Combine(Environment.CurrentDirectory, "test-artifacts", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);

    try
    {
        var runner = new FakeElevatedProcessRunner(arguments =>
        {
            var requestPath = FindElevatedRequestPath(root);
            var requestsJson = File.ReadAllText(requestPath);
            Assert(requestsJson.Contains(@"""Root"":""D:\\""", StringComparison.Ordinal), $"Expected request file to contain D root: {requestsJson}");
            Assert(requestsJson.Contains(@"""Root"":""H:\\""", StringComparison.Ordinal), $"Expected request file to contain H root: {requestsJson}");

            File.WriteAllText(OutputPathFromRequestPath(requestPath), """
                [
                  {"Root":"D:\\","Succeeded":true,"MountPoint":"D:","VolumeStatus":"FullyEncrypted","ProtectionStatus":"On","LockStatus":"Unlocked","EncryptionPercentage":100},
                  {"Root":"H:\\","Succeeded":true,"MountPoint":"H:","VolumeStatus":"FullyDecrypted","ProtectionStatus":"Off","LockStatus":"Unlocked","EncryptionPercentage":0}
                ]
                """);

            return Task.FromResult(0);
        });

        var provider = new ElevatedPowerShellBitLockerStatusProvider(runner, root);
        var results = await provider.CheckAsync(
            [
                new DriveSecurityCandidate(Guid.NewGuid(), "Source drive", @"D:\repos\personal", @"D:\"),
                new DriveSecurityCandidate(Guid.NewGuid(), "Target drive", @"H:\dev\personal", @"H:\")
            ]);

        Assert(runner.RunCount == 1, $"Expected one elevated launch, got {runner.RunCount}.");
        Assert(results.Count == 2, $"Expected two drive results, got {results.Count}.");
        Assert(results[@"D:\"].State == DriveSecurityState.Protected, $"Unexpected D state: {results[@"D:\"].State}");
        Assert(results[@"H:\"].State == DriveSecurityState.Unprotected, $"Unexpected H state: {results[@"H:\"].State}");
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

static async Task ElevatedBitLockerHelperScriptEnumeratesWindowsPowerShellJsonRequests()
{
    if (!OperatingSystem.IsWindows())
    {
        return;
    }

    var root = Path.Combine(Environment.CurrentDirectory, "test-artifacts", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);

    try
    {
        var scriptPath = Path.Combine(root, "bitlocker-helper.ps1");
        var requestPath = Path.Combine(root, "bitlocker-helper-requests.json");
        var outputPath = Path.Combine(root, "bitlocker-helper-output.json");

        File.WriteAllText(scriptPath, BuildElevatedBitLockerHelperScript());
        File.WriteAllText(requestPath, """[{"Root":"D:\\"},{"Root":"H:\\"}]""");

        var command = string.Join(
            Environment.NewLine,
            [
                "function Get-BitLockerVolume {",
                "    param([string]$MountPoint)",
                "    [pscustomobject]@{",
                "        MountPoint = $MountPoint",
                "        VolumeStatus = 'FullyEncrypted'",
                "        ProtectionStatus = if ($MountPoint -eq 'H:') { 'Off' } else { 'On' }",
                "        LockStatus = 'Unlocked'",
                "        EncryptionPercentage = if ($MountPoint -eq 'H:') { 0 } else { 100 }",
                "    }",
                "}",
                $"& {PowerShellSingleQuoted(scriptPath)} -RequestsPath {PowerShellSingleQuoted(requestPath)} -OutputPath {PowerShellSingleQuoted(outputPath)}",
                "exit $LASTEXITCODE"
            ]);

        var result = await RunWindowsPowerShellAsync(command);

        Assert(result.ExitCode == 0, $"Expected helper script success. stdout: {result.StandardOutput} stderr: {result.StandardError}");
        Assert(File.Exists(outputPath), "Expected helper script to write output JSON.");

        var output = File.ReadAllText(outputPath);
        Assert(output.Contains(@"""Root"":""D:\\""", StringComparison.Ordinal), $"Expected scalar D root result: {output}");
        Assert(output.Contains(@"""Root"":""H:\\""", StringComparison.Ordinal), $"Expected scalar H root result: {output}");
        Assert(!output.Contains(@"""Root"":[""", StringComparison.Ordinal), $"Expected roots to be written per result, not as an array: {output}");
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

static async Task ElevatedBitLockerHelperScriptWritesOutputForStartupFailures()
{
    if (!OperatingSystem.IsWindows())
    {
        return;
    }

    var root = Path.Combine(Environment.CurrentDirectory, "test-artifacts", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);

    try
    {
        var scriptPath = Path.Combine(root, "bitlocker-helper.ps1");
        var missingRequestPath = Path.Combine(root, "missing-requests.json");
        var outputPath = Path.Combine(root, "bitlocker-helper-output.json");

        File.WriteAllText(scriptPath, BuildElevatedBitLockerHelperScript());

        var command = string.Join(
            Environment.NewLine,
            [
                $"& {PowerShellSingleQuoted(scriptPath)} -RequestsPath {PowerShellSingleQuoted(missingRequestPath)} -OutputPath {PowerShellSingleQuoted(outputPath)}",
                "exit $LASTEXITCODE"
            ]);

        var result = await RunWindowsPowerShellAsync(command);

        Assert(result.ExitCode == 1, $"Expected helper script startup failure. stdout: {result.StandardOutput} stderr: {result.StandardError}");
        Assert(File.Exists(outputPath), "Expected helper script to write output JSON even when startup fails.");

        var output = File.ReadAllText(outputPath);
        Assert(output.Contains(@"""Succeeded"":false", StringComparison.Ordinal), $"Expected failed output payload: {output}");
        Assert(output.Contains("could not start", StringComparison.OrdinalIgnoreCase), $"Expected startup failure reason: {output}");
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

static async Task ElevatedBitLockerProviderTimesOutHungAdminHelper()
{
    if (!OperatingSystem.IsWindows())
    {
        return;
    }

    var runner = new BlockingElevatedProcessRunner();
    var provider = new ElevatedPowerShellBitLockerStatusProvider(
        runner,
        Path.Combine(Environment.CurrentDirectory, "test-artifacts", Guid.NewGuid().ToString("N")),
        TimeSpan.FromMilliseconds(25));

    var item = await provider.CheckAsync(
        "Source drive",
        @"D:\repos\personal",
        @"D:\");

    Assert(item.State == DriveSecurityState.Unknown, $"Expected timeout to report unknown state, got {item.State}.");
    Assert(item.Message.Contains("timed out", StringComparison.OrdinalIgnoreCase), $"Expected timeout message, got {item.Message}.");
    Assert(runner.ObservedCancellation, "Expected provider timeout to cancel the elevated process runner.");
}

static async Task ElevatedBitLockerProviderTreatsCanceledAdminPromptAsPermissionRequired()
{
    if (!OperatingSystem.IsWindows())
    {
        return;
    }

    var runner = new FakeElevatedProcessRunner(_ => throw new Win32Exception(1223, "The operation was canceled by the user."));
    var item = await new ElevatedPowerShellBitLockerStatusProvider(runner).CheckAsync(
        "Source drive",
        @"D:\repos\personal",
        @"D:\");

    Assert(item.State == DriveSecurityState.PermissionRequired, $"Expected permission-required state, got {item.State}.");
    Assert(item.Message.Contains("administrator check was canceled", StringComparison.OrdinalIgnoreCase), $"Unexpected cancellation message: {item.Message}");
}

static async Task ProfileDriveSecurityCheckerSummarizesBitLockerPosture()
{
    var profile = ValidProfile();
    profile.SourcePath = @"C:\work\scratch";
    profile.Target.Path = @"D:\backups\scratch";

    var provider = new FakeBitLockerStatusProvider();
    provider.Items[@"C:\"] = new DriveSecurityItem(
        "Source drive",
        profile.SourcePath,
        @"C:\",
        DriveSecurityState.Protected,
        DriveSecuritySeverity.Info,
        @"Drive security: Source drive is BitLocker protected (C:\).");
    provider.Items[@"D:\"] = new DriveSecurityItem(
        "Target drive",
        profile.Target.Path,
        @"D:\",
        DriveSecurityState.Unprotected,
        DriveSecuritySeverity.Warning,
        @"Drive security: Target drive is not BitLocker protected (D:\).");

    var report = await new ProfileDriveSecurityChecker(provider).CheckAsync(profile);

    Assert(report.HasWarnings, "Expected unprotected target drive to warn.");
    Assert(report.Items.Count == 2, "Expected source and target drive security items.");
    Assert(report.Summary.Contains("Target drive is not BitLocker protected", StringComparison.OrdinalIgnoreCase), $"Unexpected security summary: {report.Summary}");
}

static string? FindUnavailableDriveRoot()
{
    if (!OperatingSystem.IsWindows())
    {
        return null;
    }

    var existing = DriveInfo
        .GetDrives()
        .Select(drive => char.ToUpperInvariant(drive.Name[0]))
        .ToHashSet();

    for (var letter = 'Z'; letter >= 'G'; letter--)
    {
        if (!existing.Contains(letter))
        {
            return $"{letter}:\\";
        }
    }

    return null;
}

static BackupProfile ValidProfile()
{
    return new BackupProfile
    {
        Name = "Scratch",
        SourcePath = @"C:\work\scratch",
        Target = new BackupTarget
        {
            Kind = BackupTargetKind.LocalPath,
            Path = @"D:\backups\scratch"
        },
        Engine = BackupEngineKind.NativePowerShell,
        Schedule = new BackupSchedule
        {
            Enabled = true,
            Cadence = ScheduleCadence.Daily,
            TimeOfDay = new TimeOnly(18, 0),
            IntervalHours = 6
        },
        DryRun = true,
        MirrorDeletes = false
    };
}

static BackupProfile ValidShuttleProfile(Guid profileId, string sourcePath, string shuttleRoot)
{
    return new BackupProfile
    {
        Id = profileId,
        Name = "Scratch Shuttle",
        Mode = ProfileMode.Shuttle,
        SourcePath = sourcePath,
        Target = new BackupTarget
        {
            Kind = BackupTargetKind.LocalPath,
            Path = shuttleRoot
        },
        Engine = BackupEngineKind.NativePowerShell,
        Schedule = new BackupSchedule
        {
            Enabled = true,
            Cadence = ScheduleCadence.Hourly,
            TimeOfDay = new TimeOnly(18, 0),
            IntervalHours = 1
        },
        DryRun = false,
        MirrorDeletes = false,
        ExcludePatterns = ["node_modules"]
    };
}

static string FindElevatedRequestPath(string workingDirectory)
{
    return Directory.GetFiles(workingDirectory, "bitlocker-check-*-requests.json").Single();
}

static string OutputPathFromRequestPath(string requestPath)
{
    const string suffix = "-requests.json";
    Assert(requestPath.EndsWith(suffix, StringComparison.OrdinalIgnoreCase), $"Unexpected elevated request path: {requestPath}");
    return string.Concat(requestPath.AsSpan(0, requestPath.Length - suffix.Length), ".json");
}

static string BuildElevatedBitLockerHelperScript()
{
    var method = typeof(ElevatedPowerShellBitLockerStatusProvider).GetMethod(
        "BuildScript",
        BindingFlags.NonPublic | BindingFlags.Static);

    if (method is null)
    {
        throw new InvalidOperationException("Expected elevated BitLocker provider to expose a private helper script builder.");
    }

    return (string?)method.Invoke(null, null)
        ?? throw new InvalidOperationException("Elevated BitLocker helper script builder returned no script.");
}

static string BuildElevatedBitLockerEncodedCommand(string requestsPath, string outputPath)
{
    var method = typeof(ElevatedPowerShellBitLockerStatusProvider).GetMethod(
        "BuildEncodedCommand",
        BindingFlags.NonPublic | BindingFlags.Static);

    if (method is null)
    {
        throw new InvalidOperationException("Expected elevated BitLocker provider to expose a private encoded command builder.");
    }

    return (string?)method.Invoke(null, [requestsPath, outputPath])
        ?? throw new InvalidOperationException("Elevated BitLocker encoded command builder returned no command.");
}

static string PowerShellSingleQuoted(string value)
{
    return $"'{value.Replace("'", "''")}'";
}

static async Task<(int ExitCode, string StandardOutput, string StandardError)> RunWindowsPowerShellAsync(string command)
{
    var startInfo = new ProcessStartInfo("powershell.exe")
    {
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true
    };

    startInfo.ArgumentList.Add("-NoProfile");
    startInfo.ArgumentList.Add("-ExecutionPolicy");
    startInfo.ArgumentList.Add("Bypass");
    startInfo.ArgumentList.Add("-Command");
    startInfo.ArgumentList.Add(command);

    using var process = Process.Start(startInfo)
        ?? throw new InvalidOperationException("Failed to start Windows PowerShell.");

    var standardOutputTask = process.StandardOutput.ReadToEndAsync();
    var standardErrorTask = process.StandardError.ReadToEndAsync();

    await process.WaitForExitAsync();

    return (
        process.ExitCode,
        await standardOutputTask,
        await standardErrorTask);
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static bool TimestampsWithinTolerance(DateTime first, DateTime second)
{
    return (first - second).Duration() <= TimeSpan.FromSeconds(2);
}

static string Sha256(string path)
{
    using var stream = File.OpenRead(path);
    return Convert.ToHexString(SHA256.HashData(stream));
}

static IReadOnlyList<string> BuildScheduledTaskArguments(BackupProfile profile)
{
    var method = typeof(WindowsScheduledTaskService).GetMethod(
        "BuildCreateArguments",
        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

    if (method is null)
    {
        throw new InvalidOperationException("Expected scheduled task argument builder.");
    }

    return (IReadOnlyList<string>)method.Invoke(null, [profile, @"C:\Replicator\profile.ps1", ScheduledTaskName.ForProfile(profile)])!;
}

static Replicator.Presentation.ViewModels.MainWindowViewModel CreateMainWindowViewModel(
    IReadOnlyList<BackupProfile>? profiles = null,
    FakeProfileStore? profileStore = null,
    FakeScheduledTaskService? scheduledTasks = null,
    FakeScheduledTaskInventoryService? taskInventoryService = null,
    FakeFolderPicker? folderPicker = null,
    FakeUserConfirmation? confirmation = null,
    IProcessRunner? processRunner = null,
    IShuttleService? shuttleService = null,
    FakeBitLockerStatusProvider? driveSecurityProvider = null,
    FakeBitLockerStatusProvider? elevatedDriveSecurityProvider = null)
{
    var root = Path.Combine(Environment.CurrentDirectory, "test-artifacts", Guid.NewGuid().ToString("N"));
    var paths = new ReplicatorPaths(root);
    paths.EnsureCreated();

    return new Replicator.Presentation.ViewModels.MainWindowViewModel(
        paths,
        profileStore ?? new FakeProfileStore(profiles ?? []),
        new ProfileAvailabilityChecker(),
        driveSecurityProvider ?? new FakeBitLockerStatusProvider(),
        elevatedDriveSecurityProvider ?? new FakeBitLockerStatusProvider(),
        new ProfileDriveSecurityCache(),
        new PowerShellScriptGenerator(paths.ScriptsDirectory, paths.LogsDirectory),
        new BackupLogReader(paths.LogsDirectory),
        new BackupRunStatusReader(paths.LogsDirectory),
        shuttleService ?? new ShuttleService(new MachineIdentity("test-machine", "Test Machine")),
        processRunner ?? new ProcessRunner(),
        scheduledTasks ?? new FakeScheduledTaskService(),
        taskInventoryService ?? new FakeScheduledTaskInventoryService(),
        folderPicker ?? new FakeFolderPicker(),
        confirmation ?? new FakeUserConfirmation());
}

sealed class FakeProfileStore(IReadOnlyList<BackupProfile> profiles) : IProfileStore
{
    public List<BackupProfile> Profiles { get; } = profiles.ToList();

    public List<BackupProfile> Upserts { get; } = [];

    public List<Guid> Deletes { get; } = [];

    public Task<IReadOnlyList<BackupProfile>> LoadAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult((IReadOnlyList<BackupProfile>)Profiles);
    }

    public Task SaveAllAsync(IEnumerable<BackupProfile> profiles, CancellationToken cancellationToken = default)
    {
        Profiles.Clear();
        Profiles.AddRange(profiles);
        return Task.CompletedTask;
    }

    public Task UpsertAsync(BackupProfile profile, CancellationToken cancellationToken = default)
    {
        Upserts.Add(profile);
        var index = Profiles.FindIndex(existing => existing.Id == profile.Id);
        if (index >= 0)
        {
            Profiles[index] = profile;
        }
        else
        {
            Profiles.Add(profile);
        }

        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid profileId, CancellationToken cancellationToken = default)
    {
        Deletes.Add(profileId);
        Profiles.RemoveAll(profile => profile.Id == profileId);
        return Task.CompletedTask;
    }
}

sealed class FakeScheduledTaskService : IScheduledTaskService
{
    public List<BackupProfile> Installs { get; } = [];

    public string LastInstallScriptPath { get; private set; } = string.Empty;

    public List<BackupProfile> Runs { get; } = [];

    public List<BackupProfile> Enables { get; } = [];

    public List<BackupProfile> Disables { get; } = [];

    public List<BackupProfile> Deletes { get; } = [];

    public ScheduledTaskSnapshot QueryResult { get; set; } = new(
        string.Empty,
        ScheduledTaskState.Missing,
        string.Empty,
        string.Empty,
        0,
        string.Empty);

    public Task<TaskOperationResult> InstallOrUpdateAsync(
        BackupProfile profile,
        string scriptPath,
        CancellationToken cancellationToken = default)
    {
        Installs.Add(profile);
        LastInstallScriptPath = scriptPath;
        return Task.FromResult(new TaskOperationResult(true, "Task installed.", "install output"));
    }

    public Task<TaskOperationResult> RunAsync(BackupProfile profile, CancellationToken cancellationToken = default)
    {
        Runs.Add(profile);
        return Task.FromResult(new TaskOperationResult(true, "Task started.", "run output"));
    }

    public Task<TaskOperationResult> EnableAsync(BackupProfile profile, CancellationToken cancellationToken = default)
    {
        Enables.Add(profile);
        return Task.FromResult(new TaskOperationResult(true, "Task enabled.", "enable output"));
    }

    public Task<TaskOperationResult> DisableAsync(BackupProfile profile, CancellationToken cancellationToken = default)
    {
        Disables.Add(profile);
        return Task.FromResult(new TaskOperationResult(true, "Task disabled.", "disable output"));
    }

    public Task<TaskOperationResult> DeleteAsync(BackupProfile profile, CancellationToken cancellationToken = default)
    {
        Deletes.Add(profile);
        return Task.FromResult(new TaskOperationResult(true, "Task deleted.", "delete output"));
    }

    public Task<ScheduledTaskSnapshot> QueryAsync(
        BackupProfile profile,
        string? expectedScriptPath = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(QueryResult with { TaskName = ScheduledTaskName.ForProfile(profile) });
    }
}

sealed class FakeScheduledTaskInventoryService : IScheduledTaskInventoryService
{
    public ScheduledTaskInventoryResult Result { get; set; } = new(
        [],
        new ScheduledTaskInventorySummary(0, 0, 0, 0, 0, 0),
        string.Empty);

    public int ScanCount { get; private set; }

    public IReadOnlyList<BackupProfile> LastProfiles { get; private set; } = [];

    public IReadOnlyDictionary<Guid, string> LastExpectedScriptPaths { get; private set; } = new Dictionary<Guid, string>();

    public Task<ScheduledTaskInventoryResult> ScanAsync(
        IReadOnlyList<BackupProfile> profiles,
        IReadOnlyDictionary<Guid, string> expectedScriptPaths,
        CancellationToken cancellationToken = default)
    {
        ScanCount++;
        LastProfiles = profiles.ToList();
        LastExpectedScriptPaths = new Dictionary<Guid, string>(expectedScriptPaths);
        return Task.FromResult(Result);
    }
}

sealed class FakeFolderPicker : Replicator.Presentation.Services.IFolderPicker
{
    public Queue<string?> Results { get; } = [];

    public List<string> InitialPaths { get; } = [];

    public Task<string?> PickFolderAsync(string initialPath, CancellationToken cancellationToken = default)
    {
        InitialPaths.Add(initialPath);
        return Task.FromResult(Results.Count > 0 ? Results.Dequeue() : null);
    }
}

sealed class FakeUserConfirmation : Replicator.Presentation.Services.IUserConfirmation
{
    public Queue<bool> Results { get; } = [];

    public List<string> Titles { get; } = [];

    public Task<bool> ConfirmAsync(string title, string message, string confirmText, string cancelText)
    {
        Titles.Add(title);
        return Task.FromResult(Results.Count > 0 && Results.Dequeue());
    }
}

sealed class FakeBitLockerStatusProvider : IBitLockerStatusProvider
{
    public Dictionary<string, DriveSecurityItem> Items { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Task<DriveSecurityItem> CheckAsync(
        string label,
        string path,
        string root,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Items.TryGetValue(root, out var item)
            ? item
            : new DriveSecurityItem(label, path, root, DriveSecurityState.Unknown, DriveSecuritySeverity.Warning, $"Unknown: {root}"));
    }
}

sealed class CountingBitLockerStatusProvider : IBitLockerStatusProvider
{
    public Dictionary<string, DriveSecurityItem> Results { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Calls { get; } = [];

    public Task<DriveSecurityItem> CheckAsync(
        string label,
        string path,
        string root,
        CancellationToken cancellationToken = default)
    {
        Calls.Add(root);
        return Task.FromResult(Results.TryGetValue(root, out var result)
            ? result
            : new DriveSecurityItem(label, path, root, DriveSecurityState.Unknown, DriveSecuritySeverity.Warning, $"Unknown: {root}"));
    }
}

sealed class FakeProcessRunner(ProcessResult result) : IProcessRunner
{
    public string LastFileName { get; private set; } = string.Empty;

    public IReadOnlyList<string> LastArguments { get; private set; } = [];

    public Task<ProcessResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        CancellationToken cancellationToken = default)
    {
        LastFileName = fileName;
        LastArguments = arguments.ToList();
        return Task.FromResult(result);
    }
}

sealed class BlockingProcessRunner : IProcessRunner
{
    private readonly TaskCompletionSource<ProcessResult> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<ProcessResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        CancellationToken cancellationToken = default)
    {
        Started.TrySetResult(true);
        return _completion.Task.WaitAsync(cancellationToken);
    }

    public void Complete(ProcessResult result)
    {
        _completion.TrySetResult(result);
    }
}

sealed class BlockingShuttleService : IShuttleService
{
    private readonly TaskCompletionSource<ShuttleOperationResult> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool ObservedCancellation { get; private set; }

    public Task<ShuttleOperationResult> PrepareAsync(
        BackupProfile profile,
        IProgress<ShuttleOperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(cancellationToken);
    }

    public Task<ShuttleOperationResult> DepartAsync(
        BackupProfile profile,
        IProgress<ShuttleOperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(cancellationToken);
    }

    public Task<ShuttleOperationResult> DockAsync(
        BackupProfile profile,
        IProgress<ShuttleOperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(cancellationToken);
    }

    public Task<ShuttleOperationResult> ReceiveAsync(
        BackupProfile profile,
        IProgress<ShuttleOperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(cancellationToken);
    }

    public void Complete(ShuttleOperationResult result)
    {
        _completion.TrySetResult(result);
    }

    private async Task<ShuttleOperationResult> RunAsync(CancellationToken cancellationToken)
    {
        Started.TrySetResult(true);
        try
        {
            return await _completion.Task.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            ObservedCancellation = true;
            throw;
        }
    }
}

sealed class FakeElevatedProcessRunner(Func<IReadOnlyList<string>, Task<int>> run) : IElevatedProcessRunner
{
    public int RunCount { get; private set; }

    public string LastFileName { get; private set; } = string.Empty;

    public IReadOnlyList<string> LastArguments { get; private set; } = [];

    public async Task<int> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        CancellationToken cancellationToken = default)
    {
        RunCount++;
        LastFileName = fileName;
        LastArguments = arguments.ToList();
        return await run(LastArguments);
    }
}

sealed class BlockingElevatedProcessRunner : IElevatedProcessRunner
{
    public bool ObservedCancellation { get; private set; }

    public async Task<int> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            ObservedCancellation = true;
            throw;
        }

        return 0;
    }
}
