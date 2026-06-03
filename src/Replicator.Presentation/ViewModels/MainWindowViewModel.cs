using System.Collections.ObjectModel;
using Replicator.Core;
using Replicator.Core.Availability;
using Replicator.Core.Execution;
using Replicator.Core.Models;
using Replicator.Core.Scheduling;
using Replicator.Core.Scripting;
using Replicator.Core.Security;
using Replicator.Core.Shuttle;
using Replicator.Core.Storage;
using Replicator.Presentation.Commands;
using Replicator.Presentation.State;

namespace Replicator.Presentation.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private readonly IProfileStore _profileStore;
    private readonly ProfileAvailabilityChecker _availabilityChecker;
    private readonly IBitLockerStatusProvider _driveSecurityProvider;
    private readonly IBitLockerStatusProvider _elevatedDriveSecurityProvider;
    private readonly ProfileDriveSecurityCache _driveSecurityCache;
    private readonly PowerShellScriptGenerator _scriptGenerator;
    private readonly BackupLogReader _logReader;
    private readonly BackupRunStatusReader _runStatusReader;
    private readonly IProcessRunner _processRunner;
    private readonly IShuttleService _shuttleService;
    private readonly IScheduledTaskService _scheduledTasks;
    private readonly IScheduledTaskInventoryService _taskInventoryService;
    private readonly Replicator.Presentation.Services.IFolderPicker _folderPicker;
    private readonly Replicator.Presentation.Services.IUserConfirmation _confirmation;
    private BackupProfile? _selectedProfile;
    private ScheduledTaskSnapshot? _currentTaskSnapshot;
    private CancellationTokenSource? _currentOperationCancellationSource;
    private StatusMessage _status = StatusMessage.Ready;
    private string _headerText = "Replicator";
    private string _taskSummaryText = "No profile selected.";
    private string _availabilityText = "Availability not checked.";
    private string _driveSecurityText = "Drive security not checked.";
    private string _taskNameText = "";
    private string _nextRunText = "";
    private string _lastRunText = "";
    private string _latestLogText = "";
    private string _runSummaryText = "";
    private string _outputText = "";
    private bool _isBusy;
    private bool _canCancel;
    private bool _driveSecurityRequiresElevation;
    private ActionSurfaceState _actionSurface = ActionSurfaceState.Empty;

    public MainWindowViewModel(
        ReplicatorPaths paths,
        IProfileStore profileStore,
        ProfileAvailabilityChecker availabilityChecker,
        IBitLockerStatusProvider driveSecurityProvider,
        IBitLockerStatusProvider elevatedDriveSecurityProvider,
        ProfileDriveSecurityCache driveSecurityCache,
        PowerShellScriptGenerator scriptGenerator,
        BackupLogReader logReader,
        BackupRunStatusReader runStatusReader,
        IShuttleService shuttleService,
        IProcessRunner processRunner,
        IScheduledTaskService scheduledTasks,
        IScheduledTaskInventoryService taskInventoryService,
        Replicator.Presentation.Services.IFolderPicker folderPicker,
        Replicator.Presentation.Services.IUserConfirmation confirmation)
    {
        paths.EnsureCreated();
        _profileStore = profileStore;
        _availabilityChecker = availabilityChecker;
        _driveSecurityProvider = driveSecurityProvider;
        _elevatedDriveSecurityProvider = elevatedDriveSecurityProvider;
        _driveSecurityCache = driveSecurityCache;
        _scriptGenerator = scriptGenerator;
        _logReader = logReader;
        _runStatusReader = runStatusReader;
        _processRunner = processRunner;
        _shuttleService = shuttleService;
        _scheduledTasks = scheduledTasks;
        _taskInventoryService = taskInventoryService;
        _folderPicker = folderPicker;
        _confirmation = confirmation;
        LoadCommand = new AsyncCommand(LoadAsync);
        NewProfileCommand = new RelayCommand(CreateNewProfile, () => !IsBusy);
        DeleteProfileCommand = new AsyncCommand(DeleteSelectedProfileAsync, () => SelectedProfile is not null && !IsBusy);
        BrowseSourceCommand = new AsyncCommand(BrowseSourceAsync, () => SelectedProfile is not null && !IsBusy);
        BrowseDestinationCommand = new AsyncCommand(BrowseDestinationAsync, () => SelectedProfile is not null && !IsBusy);
        SaveProfileCommand = new AsyncCommand(SaveSelectedProfileAsync, () => SelectedProfile is not null && !IsBusy);
        GenerateScriptCommand = new AsyncCommand(GenerateScriptAsync, () => SelectedProfile is not null && !IsBusy);
        InstallTaskCommand = new AsyncCommand(InstallTaskAsync, () => SelectedProfile is not null && !IsBusy);
        PreviewDryRunCommand = new AsyncCommand(PreviewDryRunAsync, () => SelectedProfile is not null && !IsBusy);
        RunNowCommand = new AsyncCommand(RunNowAsync, () => SelectedProfile is not null && !IsBusy);
        EnableTaskCommand = new AsyncCommand(() => ChangeTaskAsync(profile => _scheduledTasks.EnableAsync(profile)), () => SelectedProfile is not null && !IsBusy);
        DisableTaskCommand = new AsyncCommand(() => ChangeTaskAsync(profile => _scheduledTasks.DisableAsync(profile)), () => SelectedProfile is not null && !IsBusy);
        RemoveTaskCommand = new AsyncCommand(() => ChangeTaskAsync(profile => _scheduledTasks.DeleteAsync(profile)), () => SelectedProfile is not null && !IsBusy);
        StartScheduledTaskCommand = new AsyncCommand(StartScheduledTaskAsync, () => SelectedProfile is not null && !IsBusy);
        RefreshStatusCommand = new AsyncCommand(RefreshStatusAsync, () => SelectedProfile is not null && !IsBusy);
        ReviewTaskInventoryCommand = new AsyncCommand(ReviewTaskInventoryAsync, () => !IsBusy);
        RepairSelectedInventoryTaskCommand = new AsyncCommand(RepairSelectedInventoryTaskAsync, () => SelectedProfile is not null && !IsBusy);
        CheckDriveSecurityAsAdminCommand = new AsyncCommand(CheckDriveSecurityAsAdminAsync, () => SelectedProfile is not null && !IsBusy);
        PrepareShuttleCommand = new AsyncCommand(
            () => RunShuttleAsync((profile, progress, cancellationToken) => _shuttleService.PrepareAsync(profile, progress, cancellationToken)),
            () => SelectedProfile is not null && !IsBusy);
        DepartShuttleCommand = new AsyncCommand(
            () => RunShuttleAsync((profile, progress, cancellationToken) => _shuttleService.DepartAsync(profile, progress, cancellationToken)),
            () => SelectedProfile is not null && !IsBusy);
        DockShuttleCommand = new AsyncCommand(
            () => RunShuttleAsync((profile, progress, cancellationToken) => _shuttleService.DockAsync(profile, progress, cancellationToken)),
            () => SelectedProfile is not null && !IsBusy);
        ReceiveShuttleCommand = new AsyncCommand(
            () => RunShuttleAsync((profile, progress, cancellationToken) => _shuttleService.ReceiveAsync(profile, progress, cancellationToken)),
            () => SelectedProfile is not null && !IsBusy);
        CancelOperationCommand = new RelayCommand(CancelOperation, () => CanCancel);
    }

    public ObservableCollection<BackupProfile> Profiles { get; } = [];

    public AsyncCommand LoadCommand { get; }

    public RelayCommand NewProfileCommand { get; }

    public AsyncCommand DeleteProfileCommand { get; }

    public AsyncCommand BrowseSourceCommand { get; }

    public AsyncCommand BrowseDestinationCommand { get; }

    public AsyncCommand SaveProfileCommand { get; }

    public AsyncCommand GenerateScriptCommand { get; }

    public AsyncCommand InstallTaskCommand { get; }

    public AsyncCommand PreviewDryRunCommand { get; }

    public AsyncCommand RunNowCommand { get; }

    public AsyncCommand EnableTaskCommand { get; }

    public AsyncCommand DisableTaskCommand { get; }

    public AsyncCommand RemoveTaskCommand { get; }

    public AsyncCommand StartScheduledTaskCommand { get; }

    public AsyncCommand RefreshStatusCommand { get; }

    public AsyncCommand ReviewTaskInventoryCommand { get; }

    public AsyncCommand RepairSelectedInventoryTaskCommand { get; }

    public AsyncCommand CheckDriveSecurityAsAdminCommand { get; }

    public AsyncCommand PrepareShuttleCommand { get; }

    public AsyncCommand DepartShuttleCommand { get; }

    public AsyncCommand DockShuttleCommand { get; }

    public AsyncCommand ReceiveShuttleCommand { get; }

    public RelayCommand CancelOperationCommand { get; }

    public BackupProfile? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (SetProperty(ref _selectedProfile, value))
            {
                _currentTaskSnapshot = null;
                LoadProfileIntoState(value);
                RecalculateActionSurface();
                RaiseCommandStatesChanged();
            }
        }
    }

    public ProfileFormViewModel Form { get; } = new();

    public TaskInventoryViewModel TaskInventory { get; } = new();

    public StatusMessage Status { get => _status; private set => SetProperty(ref _status, value); }

    public string HeaderText { get => _headerText; private set => SetProperty(ref _headerText, value); }

    public string TaskSummaryText { get => _taskSummaryText; private set => SetProperty(ref _taskSummaryText, value); }

    public string AvailabilityText { get => _availabilityText; private set => SetProperty(ref _availabilityText, value); }

    public string DriveSecurityText { get => _driveSecurityText; private set => SetProperty(ref _driveSecurityText, value); }

    public string TaskNameText { get => _taskNameText; private set => SetProperty(ref _taskNameText, value); }

    public string NextRunText { get => _nextRunText; private set => SetProperty(ref _nextRunText, value); }

    public string LastRunText { get => _lastRunText; private set => SetProperty(ref _lastRunText, value); }

    public string LatestLogText { get => _latestLogText; private set => SetProperty(ref _latestLogText, value); }

    public string RunSummaryText { get => _runSummaryText; private set => SetProperty(ref _runSummaryText, value); }

    public string OutputText { get => _outputText; private set => SetProperty(ref _outputText, value); }

    public bool IsBusy { get => _isBusy; private set => SetProperty(ref _isBusy, value); }

    public bool CanCancel { get => _canCancel; private set => SetProperty(ref _canCancel, value); }

    public bool DriveSecurityRequiresElevation { get => _driveSecurityRequiresElevation; private set => SetProperty(ref _driveSecurityRequiresElevation, value); }

    public ActionSurfaceState ActionSurface { get => _actionSurface; private set => SetProperty(ref _actionSurface, value); }

    public async Task LoadAsync()
    {
        Profiles.Clear();

        foreach (var profile in await _profileStore.LoadAsync())
        {
            Profiles.Add(Normalize(profile));
        }

        if (Profiles.Count == 0)
        {
            Profiles.Add(BackupProfileFactory.CreateDefault());
        }

        SelectedProfile = Profiles[0];
        if (SelectedProfile is not null)
        {
            await RefreshDriveSecurityAsync(SelectedProfile);
        }

        ShowStatus("Ready.", succeeded: true);
        await RefreshTaskInventoryAsync();
    }

    public void RecalculateActionSurface()
    {
        ActionSurface = ActionSurfaceState.From(
            SelectedProfile,
            _currentTaskSnapshot,
            IsBusy,
            CanCancel,
            DriveSecurityRequiresElevation);
        TaskSummaryText = ActionSurface.TaskSummaryText;
    }

    public bool TryApplyForm(out BackupProfile profile)
    {
        profile = null!;

        if (SelectedProfile is null)
        {
            ShowStatus("Select a profile.", succeeded: false);
            return false;
        }

        var result = Form.TryApply(SelectedProfile);
        if (!result.Succeeded)
        {
            ShowStatus(result.Message, succeeded: false);
            return false;
        }

        profile = SelectedProfile;
        ShowAvailability(_availabilityChecker.Check(profile));
        RecalculateActionSurface();
        return true;
    }

    private void CreateNewProfile()
    {
        var profile = BackupProfileFactory.CreateDefault();
        Profiles.Add(profile);
        SelectedProfile = profile;
        ShowStatus("New profile created.", succeeded: true);
    }

    private async Task DeleteSelectedProfileAsync()
    {
        var profile = SelectedProfile;
        if (profile is null)
        {
            ShowStatus("Select a profile.", succeeded: false);
            return;
        }

        var confirmed = await _confirmation.ConfirmAsync(
            "Remove Profile",
            $"Remove profile '{profile.Name}' and its scheduled task?",
            "Remove",
            "Cancel");
        if (!confirmed)
        {
            return;
        }

        var taskResult = await _scheduledTasks.DeleteAsync(profile);
        await _profileStore.DeleteAsync(profile.Id);
        Profiles.Remove(profile);

        if (Profiles.Count == 0)
        {
            Profiles.Add(BackupProfileFactory.CreateDefault());
        }

        SelectedProfile = Profiles[0];
        AppendOutput(taskResult.Output);
        ShowStatus("Profile removed.", succeeded: true);
        await RefreshTaskInventoryAsync();
    }

    private async Task BrowseSourceAsync()
    {
        var selectedPath = await _folderPicker.PickFolderAsync(Form.SourcePath);
        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            Form.SourcePath = selectedPath;
        }
    }

    private async Task BrowseDestinationAsync()
    {
        var selectedPath = await _folderPicker.PickFolderAsync(Form.DestinationPath);
        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            Form.DestinationPath = selectedPath;
        }
    }

    private async Task SaveSelectedProfileAsync()
    {
        if (!TryApplyForm(out var profile))
        {
            return;
        }

        await _profileStore.UpsertAsync(profile);
        HeaderText = profile.Name;
        ShowStatus("Profile saved.", succeeded: true);
        await RefreshTaskInventoryAsync();
    }

    private async Task GenerateScriptAsync()
    {
        if (!TryApplyForm(out var profile))
        {
            return;
        }

        await _profileStore.UpsertAsync(profile);
        var script = await _scriptGenerator.WriteAsync(profile);
        AppendOutput($"Generated script: {script.Path}");
        ShowStatus("Script generated.", succeeded: true);
    }

    private async Task InstallTaskAsync()
    {
        if (!TryApplyForm(out var profile))
        {
            return;
        }

        var result = await InstallOrUpdateTaskForProfileAsync(profile);
        AppendOutput(result.Output);
        ShowStatus(result.Message, result.Succeeded);
        await RefreshTaskStatusAsync(profile);
        await RefreshTaskInventoryAsync();
    }

    private async Task PreviewDryRunAsync()
    {
        if (!TryApplyForm(out var profile))
        {
            return;
        }

        await RunScriptAsync(profile, forceDryRun: true);
    }

    private async Task RunNowAsync()
    {
        await RunBusyAsync("Running profile now...", async _ =>
        {
            if (!TryApplyForm(out var profile))
            {
                return;
            }

            await RunScriptAsync(profile, forceDryRun: false);
        });
    }

    private async Task RunShuttleAsync(
        Func<BackupProfile, IProgress<ShuttleOperationProgress>, CancellationToken, Task<ShuttleOperationResult>> operation)
    {
        await RunBusyAsync("Running shuttle operation...", async cancellationToken =>
        {
            if (!TryApplyForm(out var profile))
            {
                return;
            }

            await _profileStore.UpsertAsync(profile);
            ShowAvailability(_availabilityChecker.Check(profile));
            var acceptProgress = true;
            var progress = new Progress<ShuttleOperationProgress>(progress =>
            {
                if (acceptProgress)
                {
                    ShowShuttleProgress(progress);
                }
            });
            var result = await Task.Run(() => operation(profile, progress, cancellationToken), cancellationToken);
            acceptProgress = false;
            OutputText = result.ToDisplayString();
            ShowStatus(result.Message, result.Succeeded);
            RecalculateActionSurface();
        }, canCancel: true);
    }

    private async Task<TaskOperationResult> InstallOrUpdateTaskForProfileAsync(BackupProfile profile)
    {
        await _profileStore.UpsertAsync(profile);
        var script = await _scriptGenerator.WriteAsync(profile);
        return await _scheduledTasks.InstallOrUpdateAsync(profile, script.Path);
    }

    private async Task ChangeTaskAsync(Func<BackupProfile, Task<TaskOperationResult>> operation)
    {
        var profile = SelectedProfile;
        if (profile is null)
        {
            ShowStatus("Select a profile.", succeeded: false);
            return;
        }

        var result = await operation(profile);
        AppendOutput(result.Output);
        ShowStatus(result.Message, result.Succeeded);
        await RefreshTaskStatusAsync(profile);
        await RefreshTaskInventoryAsync();
    }

    private async Task StartScheduledTaskAsync()
    {
        var profile = SelectedProfile;
        if (profile is null)
        {
            ShowStatus("Select a profile.", succeeded: false);
            return;
        }

        var expectedScriptPath = _scriptGenerator.ScriptPathFor(profile);
        var snapshot = await _scheduledTasks.QueryAsync(profile, expectedScriptPath);
        _currentTaskSnapshot = snapshot;
        RecalculateActionSurface();

        if (snapshot.State == ScheduledTaskState.Missing)
        {
            ShowStatus("No scheduled task is installed for this profile. Use Install Task or Run Now.", succeeded: false);
            AppendOutput(snapshot.RawOutput);
            return;
        }

        if (snapshot.NeedsRepair)
        {
            ShowStatus("Scheduled task needs repair before it can be started. Use Repair Task.", succeeded: false);
            AppendOutput(string.Join(Environment.NewLine, snapshot.RepairReasons));
            return;
        }

        var result = await _scheduledTasks.RunAsync(profile);
        AppendOutput(result.Output);
        ShowStatus(result.Succeeded ? "Scheduled task started. Waiting for status..." : result.Message, result.Succeeded);
    }

    private async Task RefreshStatusAsync()
    {
        var profile = SelectedProfile;
        if (profile is null)
        {
            ShowStatus("Select a profile.", succeeded: false);
            return;
        }

        await RefreshTaskStatusAsync(profile);
        ShowLatestRun(profile);
        await RefreshTaskInventoryAsync();
    }

    private async Task ReviewTaskInventoryAsync()
    {
        if (TaskInventory.Items.Count == 0)
        {
            await RefreshTaskInventoryAsync();
        }

        if (TaskInventory.Items.Count == 0)
        {
            ShowStatus("No Replicator scheduled tasks were found.", succeeded: true);
            return;
        }

        TaskInventory.OpenReview();
    }

    private async Task RepairSelectedInventoryTaskAsync()
    {
        if (!TryApplyForm(out var profile))
        {
            return;
        }

        var repairItem = GetSelectedProfileRepairItem();
        if (repairItem is null)
        {
            ShowStatus("No repairable scheduled task is selected.", succeeded: false);
            return;
        }

        var result = await InstallOrUpdateTaskForProfileAsync(profile);
        AppendOutput(result.Output);
        ShowStatus(result.Message, result.Succeeded);
        await RefreshTaskStatusAsync(profile);
        await RefreshTaskInventoryAsync();
    }

    private ScheduledTaskInventoryItem? GetSelectedProfileRepairItem()
    {
        var profileId = SelectedProfile?.Id;
        if (profileId is null)
        {
            return null;
        }

        return TaskInventory.Items.FirstOrDefault(item => item.ProfileId == profileId && item.CanRepair);
    }

    private async Task RefreshDriveSecurityAsync(BackupProfile profile)
    {
        await RefreshDriveSecurityCacheAsync(
            profile,
            _driveSecurityProvider,
            "Drive security: checking...");
    }

    private async Task CheckDriveSecurityAsAdminAsync()
    {
        if (!TryApplyForm(out var profile))
        {
            return;
        }

        await RefreshDriveSecurityCacheAsync(
            profile,
            _elevatedDriveSecurityProvider,
            "Drive security: waiting for administrator approval...",
            refreshSelectedProfile: true);
    }

    private async Task RefreshDriveSecurityCacheAsync(
        BackupProfile profile,
        IBitLockerStatusProvider provider,
        string checkingMessage,
        bool refreshSelectedProfile = false,
        CancellationToken cancellationToken = default)
    {
        var profileId = profile.Id;
        DriveSecurityText = checkingMessage;

        try
        {
            if (refreshSelectedProfile)
            {
                await _driveSecurityCache.RefreshAsync(profile, provider, cancellationToken);
            }
            else
            {
                await _driveSecurityCache.WarmMissingAsync(Profiles.ToList(), provider, cancellationToken);
            }

            if (SelectedProfile?.Id != profileId)
            {
                return;
            }

            ShowDriveSecurity(_driveSecurityCache.Report(profile));
        }
        catch (Exception exception)
        {
            if (SelectedProfile?.Id != profileId)
            {
                return;
            }

            DriveSecurityText = $"Drive security: check failed. {exception.Message}";
            DriveSecurityRequiresElevation = false;
            RecalculateActionSurface();
        }
    }

    private void ShowDriveSecurity(ProfileDriveSecurityReport report)
    {
        DriveSecurityText = report.Summary;
        DriveSecurityRequiresElevation = report.RequiresElevation;
        RecalculateActionSurface();
    }

    private async Task RefreshTaskStatusAsync(BackupProfile profile)
    {
        var expectedScriptPath = _scriptGenerator.ScriptPathFor(profile);
        var snapshot = await _scheduledTasks.QueryAsync(profile, expectedScriptPath);
        _currentTaskSnapshot = snapshot;
        TaskNameText = snapshot.TaskName;
        NextRunText = string.IsNullOrWhiteSpace(snapshot.NextRunTime) ? snapshot.State.ToString() : snapshot.NextRunTime;
        LastRunText = string.IsNullOrWhiteSpace(snapshot.LastRunTime)
            ? $"Last result: {snapshot.LastResult}"
            : $"{snapshot.LastRunTime}; result {snapshot.LastResult}";
        RecalculateActionSurface();
    }

    private async Task RefreshTaskInventoryAsync()
    {
        ScheduledTaskInventoryResult inventory;
        try
        {
            var expectedPaths = Profiles.ToDictionary(profile => profile.Id, profile => _scriptGenerator.ScriptPathFor(profile));
            inventory = await _taskInventoryService.ScanAsync(Profiles.ToList(), expectedPaths);
        }
        catch (Exception exception)
        {
            inventory = BuildInventoryFailure(exception);
        }

        var selectedIssue = SelectedProfile is null
            ? null
            : ScheduledTaskInventoryIssueSelector.ForProfile(inventory, SelectedProfile.Id);
        TaskInventory.Apply(inventory, selectedIssue);
    }

    private void LoadProfileIntoState(BackupProfile? profile)
    {
        if (profile is null)
        {
            HeaderText = "Replicator";
            TaskSummaryText = "No profile selected.";
            AvailabilityText = "Availability not checked.";
            DriveSecurityText = "Drive security not checked.";
            DriveSecurityRequiresElevation = false;
            TaskNameText = "";
            NextRunText = "";
            LastRunText = "";
            LatestLogText = "";
            RunSummaryText = "";
            OutputText = "";
            return;
        }

        Normalize(profile);
        HeaderText = profile.Name;
        Form.Load(profile);
        TaskNameText = ScheduledTaskName.ForProfile(profile);
        NextRunText = "";
        LastRunText = "";
        ShowLatestRun(profile);
        ShowAvailability(_availabilityChecker.Check(profile));
        DriveSecurityText = "Drive security not checked.";
        DriveSecurityRequiresElevation = false;
    }

    private void ShowAvailability(ProfileAvailabilityReport report)
    {
        AvailabilityText = report.Summary;
    }

    private async Task RunScriptAsync(BackupProfile profile, bool forceDryRun)
    {
        var availability = _availabilityChecker.Check(profile);
        ShowAvailability(availability);
        if (availability.HasErrors)
        {
            OutputText = availability.ToDisplayString();
            ShowStatus(availability.Summary, succeeded: false);
            return;
        }

        await _profileStore.UpsertAsync(profile);
        var script = await _scriptGenerator.WriteAsync(profile);
        var arguments = new List<string>
        {
            "-NoProfile",
            "-ExecutionPolicy",
            "Bypass",
            "-File",
            script.Path
        };

        if (forceDryRun)
        {
            arguments.Add("-DryRun");
        }

        var result = await _processRunner.RunAsync("powershell.exe", arguments);
        AppendOutput(result.StandardOutput);
        AppendOutput(result.StandardError);
        ShowLatestRun(profile, preserveExistingOutput: true);
        RecalculateActionSurface();

        var dryRunActive = forceDryRun || profile.DryRun;
        var action = dryRunActive ? "Dry run" : "Run";
        ShowStatus(
            result.Succeeded ? $"{action} completed. Latest log is shown below." : $"{action} failed with exit code {result.ExitCode}.",
            result.Succeeded);
    }

    private void ShowLatestRun(BackupProfile profile, bool preserveExistingOutput = false)
    {
        var status = _runStatusReader.ReadLatest(profile);
        var summary = _logReader.ReadLatest(profile);
        if (status is null && summary is null)
        {
            LatestLogText = "No log has been written for this profile.";
            RunSummaryText = "";
            if (!preserveExistingOutput)
            {
                OutputText = "";
            }

            return;
        }

        if (status is not null)
        {
            LatestLogText = string.IsNullOrWhiteSpace(status.LogPath)
                ? "No log path was written for the latest run."
                : status.LogPath;
            RunSummaryText = status.ToDisplayString();
            OutputText = summary?.Tail ?? status.Message;
            return;
        }

        LatestLogText = summary!.LogPath;
        RunSummaryText = summary.ToDisplayString();
        OutputText = summary.Tail;
    }

    private void ShowStatus(string message, bool succeeded)
    {
        Status = new StatusMessage(message, succeeded ? StatusKind.Success : StatusKind.Error);
    }

    private async Task RunBusyAsync(string busyMessage, Func<CancellationToken, Task> operation, bool canCancel = false)
    {
        using var cancellationSource = new CancellationTokenSource();
        _currentOperationCancellationSource = canCancel ? cancellationSource : null;
        SetBusy(true, busyMessage);

        try
        {
            await operation(cancellationSource.Token);
        }
        catch (OperationCanceledException)
        {
            AppendOutput("Operation canceled by user.");
            ShowStatus("Operation canceled.", succeeded: false);
        }
        catch (Exception exception)
        {
            AppendOutput(exception.ToString());
            ShowStatus(exception.Message, succeeded: false);
        }
        finally
        {
            _currentOperationCancellationSource = null;
            SetBusy(false, "");
        }
    }

    private void SetBusy(bool busy, string message)
    {
        IsBusy = busy;
        CanCancel = busy && _currentOperationCancellationSource is not null && !_currentOperationCancellationSource.IsCancellationRequested;
        RecalculateActionSurface();
        RaiseCommandStatesChanged();

        if (busy)
        {
            ShowStatus(message, succeeded: true);
        }
    }

    private void CancelOperation()
    {
        ShowStatus("Cancellation requested...", succeeded: false);
        _currentOperationCancellationSource?.Cancel();
        CanCancel = false;
        RecalculateActionSurface();
        RaiseCommandStatesChanged();
    }

    private void RaiseCommandStatesChanged()
    {
        LoadCommand.RaiseCanExecuteChanged();
        NewProfileCommand.RaiseCanExecuteChanged();
        DeleteProfileCommand.RaiseCanExecuteChanged();
        BrowseSourceCommand.RaiseCanExecuteChanged();
        BrowseDestinationCommand.RaiseCanExecuteChanged();
        SaveProfileCommand.RaiseCanExecuteChanged();
        GenerateScriptCommand.RaiseCanExecuteChanged();
        InstallTaskCommand.RaiseCanExecuteChanged();
        PreviewDryRunCommand.RaiseCanExecuteChanged();
        RunNowCommand.RaiseCanExecuteChanged();
        EnableTaskCommand.RaiseCanExecuteChanged();
        DisableTaskCommand.RaiseCanExecuteChanged();
        RemoveTaskCommand.RaiseCanExecuteChanged();
        StartScheduledTaskCommand.RaiseCanExecuteChanged();
        RefreshStatusCommand.RaiseCanExecuteChanged();
        ReviewTaskInventoryCommand.RaiseCanExecuteChanged();
        RepairSelectedInventoryTaskCommand.RaiseCanExecuteChanged();
        CheckDriveSecurityAsAdminCommand.RaiseCanExecuteChanged();
        PrepareShuttleCommand.RaiseCanExecuteChanged();
        DepartShuttleCommand.RaiseCanExecuteChanged();
        DockShuttleCommand.RaiseCanExecuteChanged();
        ReceiveShuttleCommand.RaiseCanExecuteChanged();
        CancelOperationCommand.RaiseCanExecuteChanged();
    }

    private void ShowShuttleProgress(ShuttleOperationProgress progress)
    {
        ShowStatus(
            progress.TotalFiles <= 0
                ? progress.Message
                : $"{progress.Message} {FormatPercent(progress.PercentComplete)}",
            succeeded: true);
    }

    private static string FormatPercent(double percent)
    {
        return percent > 0 && percent < 1
            ? "<1%"
            : $"{percent:0}%";
    }

    private void AppendOutput(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return;
        }

        OutputText = string.Concat(OutputText, output.TrimEnd(), Environment.NewLine);
    }

    private static ScheduledTaskInventoryResult BuildInventoryFailure(Exception exception)
    {
        var item = new ScheduledTaskInventoryItem(
            @"\Replicator",
            null,
            "Task Scheduler",
            ScheduledTaskInventoryState.Unknown,
            ScheduledTaskState.Unknown,
            string.Empty,
            string.Empty,
            0,
            string.Empty,
            string.Empty,
            false,
            [],
            $"Failed to scan scheduled tasks. {exception.Message}",
            exception.ToString());

        return new ScheduledTaskInventoryResult(
            [item],
            new ScheduledTaskInventorySummary(1, 0, 0, 0, 0, 1),
            exception.ToString());
    }

    private static BackupProfile Normalize(BackupProfile profile)
    {
        profile.Target ??= new BackupTarget();
        profile.Schedule ??= new BackupSchedule();
        profile.ExcludePatterns ??= [];
        return profile;
    }
}
