using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using Replicator.Core;
using Replicator.Core.Availability;
using Replicator.Core.Execution;
using Replicator.Core.Models;
using Replicator.Core.Scheduling;
using Replicator.Core.Scripting;
using Replicator.Core.Security;
using Replicator.Core.Shuttle;
using Replicator.Core.Storage;
using MessageBox = System.Windows.MessageBox;
using WinForms = System.Windows.Forms;

namespace Replicator.App;

public partial class MainWindow : Window
{
    private readonly ReplicatorPaths _paths;
    private readonly IProfileStore _profileStore;
    private readonly ProfileAvailabilityChecker _availabilityChecker;
    private readonly ProfileDriveSecurityChecker _driveSecurityChecker;
    private readonly PowerShellScriptGenerator _scriptGenerator;
    private readonly BackupLogReader _logReader;
    private readonly ShuttleService _shuttleService;
    private readonly ProcessRunner _processRunner;
    private readonly IScheduledTaskService _scheduledTasks;
    private readonly ObservableCollection<BackupProfile> _profiles = [];
    private ScheduledTaskSnapshot? _currentTaskSnapshot;
    private CancellationTokenSource? _currentOperationCancellationSource;
    private bool _isBusy;
    private bool _loadingProfile;

    public MainWindow()
    {
        InitializeComponent();

        _paths = ReplicatorPaths.CreateDefault();
        _paths.EnsureCreated();
        _profileStore = new JsonProfileStore(_paths.ProfilesFile);
        _availabilityChecker = new ProfileAvailabilityChecker();
        _processRunner = new ProcessRunner();
        _driveSecurityChecker = new ProfileDriveSecurityChecker(new PowerShellBitLockerStatusProvider(_processRunner));
        _scriptGenerator = new PowerShellScriptGenerator(_paths.ScriptsDirectory, _paths.LogsDirectory);
        _logReader = new BackupLogReader(_paths.LogsDirectory);
        _shuttleService = new ShuttleService(new MachineIdentityProvider(_paths.MachineIdentityFile).GetOrCreate());
        _scheduledTasks = new WindowsScheduledTaskService(_processRunner);

        ProfilesList.ItemsSource = _profiles;
        ModeComboBox.ItemsSource = Enum.GetValues<ProfileMode>();
        CadenceComboBox.ItemsSource = Enum.GetValues<ScheduleCadence>();
        DayOfWeekComboBox.ItemsSource = Enum.GetValues<DayOfWeek>();
        UpdateActionSurface(null, null);
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadProfilesAsync();
    }

    private async Task LoadProfilesAsync()
    {
        _profiles.Clear();

        foreach (var profile in await _profileStore.LoadAsync())
        {
            _profiles.Add(Normalize(profile));
        }

        if (_profiles.Count == 0)
        {
            _profiles.Add(BackupProfileFactory.CreateDefault());
        }

        ProfilesList.SelectedIndex = 0;
        UpdateActionSurface(GetSelectedProfile(showStatus: false), _currentTaskSnapshot);
        ShowStatus("Ready.", true);
    }

    private void NewProfile_Click(object sender, RoutedEventArgs e)
    {
        var profile = BackupProfileFactory.CreateDefault();
        _profiles.Add(profile);
        ProfilesList.SelectedItem = profile;
        _currentTaskSnapshot = null;
        UpdateActionSurface(profile, null);
        ShowStatus("New profile created.", true);
    }

    private async void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        var profile = GetSelectedProfile();
        if (profile is null)
        {
            return;
        }

        var result = MessageBox.Show(
            $"Remove profile '{profile.Name}' and its scheduled task?",
            "Remove Profile",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        var taskResult = await _scheduledTasks.DeleteAsync(profile);
        await _profileStore.DeleteAsync(profile.Id);
        _profiles.Remove(profile);

        if (_profiles.Count == 0)
        {
            _profiles.Add(BackupProfileFactory.CreateDefault());
        }

        ProfilesList.SelectedIndex = 0;
        _currentTaskSnapshot = null;
        UpdateActionSurface(GetSelectedProfile(showStatus: false), null);
        AppendOutput(taskResult.Output);
        ShowStatus("Profile removed.", true);
    }

    private async void ProfilesList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        var profile = GetSelectedProfile(showStatus: false);
        _currentTaskSnapshot = null;
        LoadProfileIntoForm(profile);
        UpdateActionSurface(profile, null);

        if (profile is not null)
        {
            await RefreshTaskStatusAsync(profile);
            await RefreshDriveSecurityAsync(profile);
        }
    }

    private void BrowseSource_Click(object sender, RoutedEventArgs e)
    {
        BrowseForFolder(SourcePathTextBox);
    }

    private void BrowseDestination_Click(object sender, RoutedEventArgs e)
    {
        BrowseForFolder(DestinationPathTextBox);
    }

    private void ModeComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_loadingProfile)
        {
            return;
        }

        var profile = GetSelectedProfile(showStatus: false);
        if (profile is null || ModeComboBox.SelectedItem is not ProfileMode mode)
        {
            return;
        }

        profile.Mode = mode;
        DestinationLabel.Content = mode == ProfileMode.Shuttle ? "Shuttle path" : "Destination";
        ShowAvailability(_availabilityChecker.Check(profile));
        UpdateActionSurface(profile, _currentTaskSnapshot);
    }

    private void CadenceComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (CadenceComboBox.SelectedItem is ScheduleCadence cadence)
        {
            UpdateIntervalLabel(cadence);
        }
    }

    private async void SaveProfile_Click(object sender, RoutedEventArgs e)
    {
        await SaveSelectedProfileAsync();
    }

    private async void GenerateScript_Click(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync("Generating script...", async () =>
        {
            if (!TryApplyForm(out var profile))
            {
                return;
            }

            await _profileStore.UpsertAsync(profile);
            var script = await _scriptGenerator.WriteAsync(profile);
            AppendOutput($"Generated script: {script.Path}");
            ShowStatus("Script generated.", true);
        });
    }

    private async void InstallTask_Click(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync("Installing scheduled task...", async () =>
        {
            if (!TryApplyForm(out var profile))
            {
                return;
            }

            await _profileStore.UpsertAsync(profile);
            var script = await _scriptGenerator.WriteAsync(profile);
            var result = await _scheduledTasks.InstallOrUpdateAsync(profile, script.Path);

            AppendOutput(result.Output);
            ShowStatus(result.Message, result.Succeeded);
            await RefreshTaskStatusAsync(profile);
        });
    }

    private async void RunNow_Click(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync("Running profile now...", async () =>
        {
            if (!TryApplyForm(out var profile))
            {
                return;
            }

            await RunScriptAsync(profile, forceDryRun: false);
        });
    }

    private async void RunTask_Click(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync("Starting scheduled task...", async () =>
        {
            var profile = GetSelectedProfile();
            if (profile is null)
            {
                return;
            }

            var snapshot = await _scheduledTasks.QueryAsync(profile);
            if (snapshot.State == ScheduledTaskState.Missing)
            {
                ShowStatus("No scheduled task is installed for this profile. Use Install Task or Run Now.", false);
                AppendOutput(snapshot.RawOutput);
                return;
            }

            var result = await _scheduledTasks.RunAsync(profile);
            AppendOutput(result.Output);
            ShowStatus(result.Succeeded ? "Scheduled task started. Waiting for status..." : result.Message, result.Succeeded);

            if (result.Succeeded)
            {
                await PollTaskCompletionAsync(profile);
                ShowLatestRun(profile);
            }

            await RefreshTaskStatusAsync(profile);
        });
    }

    private async void RunScriptDry_Click(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync("Running dry-run preview...", async () =>
        {
            if (!TryApplyForm(out var profile))
            {
                return;
            }

            await RunScriptAsync(profile, forceDryRun: true);
        });
    }

    private async void EnableTask_Click(object sender, RoutedEventArgs e)
    {
        await ChangeTaskAsync(profile => _scheduledTasks.EnableAsync(profile));
    }

    private async void DisableTask_Click(object sender, RoutedEventArgs e)
    {
        await ChangeTaskAsync(profile => _scheduledTasks.DisableAsync(profile));
    }

    private async void RemoveTask_Click(object sender, RoutedEventArgs e)
    {
        await ChangeTaskAsync(profile => _scheduledTasks.DeleteAsync(profile));
    }

    private async void RefreshStatus_Click(object sender, RoutedEventArgs e)
    {
        var profile = GetSelectedProfile();
        if (profile is not null)
        {
            await RefreshTaskStatusAsync(profile);
            await RefreshDriveSecurityAsync(profile);
            ShowLatestRun(profile);
        }
    }

    private async void PrepareShuttle_Click(object sender, RoutedEventArgs e)
    {
        await RunShuttleAsync("Preparing shuttle...", (profile, progress, cancellationToken) => _shuttleService.PrepareAsync(profile, progress, cancellationToken));
    }

    private async void DepartShuttle_Click(object sender, RoutedEventArgs e)
    {
        await RunShuttleAsync("Marking shuttle ready to depart...", (profile, progress, cancellationToken) => _shuttleService.DepartAsync(profile, progress, cancellationToken));
    }

    private async void DockShuttle_Click(object sender, RoutedEventArgs e)
    {
        await RunShuttleAsync("Docking shuttle...", (profile, progress, cancellationToken) => _shuttleService.DockAsync(profile, progress, cancellationToken));
    }

    private async void ReceiveShuttle_Click(object sender, RoutedEventArgs e)
    {
        await RunShuttleAsync("Receiving shuttle changes...", (profile, progress, cancellationToken) => _shuttleService.ReceiveAsync(profile, progress, cancellationToken));
    }

    private void CancelOperation_Click(object sender, RoutedEventArgs e)
    {
        _currentOperationCancellationSource?.Cancel();
        CancelOperationButton.IsEnabled = false;
        ShowStatus("Cancellation requested...", false);
    }

    private async Task SaveSelectedProfileAsync()
    {
        if (!TryApplyForm(out var profile))
        {
            return;
        }

        await _profileStore.UpsertAsync(profile);
        ProfilesList.Items.Refresh();
        HeaderTextBlock.Text = profile.Name;
        ShowStatus("Profile saved.", true);
        await RefreshDriveSecurityAsync(profile);
    }

    private async Task ChangeTaskAsync(Func<BackupProfile, Task<TaskOperationResult>> operation)
    {
        await RunBusyAsync("Updating scheduled task...", async () =>
        {
            var profile = GetSelectedProfile();
            if (profile is null)
            {
                return;
            }

            var result = await operation(profile);
            AppendOutput(result.Output);
            ShowStatus(result.Message, result.Succeeded);
            await RefreshTaskStatusAsync(profile);
        });
    }

    private async Task RunShuttleAsync(
        string busyMessage,
        Func<BackupProfile, IProgress<ShuttleOperationProgress>, CancellationToken, Task<ShuttleOperationResult>> operation)
    {
        await RunBusyAsync(busyMessage, async cancellationToken =>
        {
            if (!TryApplyForm(out var profile))
            {
                return;
            }

            await _profileStore.UpsertAsync(profile);
            ShowAvailability(_availabilityChecker.Check(profile));
            var progress = new Progress<ShuttleOperationProgress>(ShowShuttleProgress);
            var result = await Task.Run(() => operation(profile, progress, cancellationToken), cancellationToken);
            OutputTextBox.Text = result.ToDisplayString();
            OutputTextBox.ScrollToEnd();
            ShowStatus(result.Message, result.Succeeded);
            UpdateActionSurface(profile, _currentTaskSnapshot);
        }, canCancel: true);
    }

    private async Task RefreshTaskStatusAsync(BackupProfile profile)
    {
        var snapshot = await _scheduledTasks.QueryAsync(profile);
        _currentTaskSnapshot = snapshot;
        TaskNameTextBlock.Text = snapshot.TaskName;
        NextRunTextBlock.Text = string.IsNullOrWhiteSpace(snapshot.NextRunTime) ? snapshot.State.ToString() : snapshot.NextRunTime;
        LastRunTextBlock.Text = string.IsNullOrWhiteSpace(snapshot.LastRunTime)
            ? $"Last result: {snapshot.LastResult}"
            : $"{snapshot.LastRunTime}; result {snapshot.LastResult}";
        TaskSummaryTextBlock.Text = $"{snapshot.State} | {profile.Engine} | {profile.Target.Kind}";
        UpdateActionSurface(profile, snapshot);
    }

    private async Task RunScriptAsync(BackupProfile profile, bool forceDryRun)
    {
        var availability = _availabilityChecker.Check(profile);
        ShowAvailability(availability);
        if (availability.HasErrors)
        {
            OutputTextBox.Text = availability.ToDisplayString();
            OutputTextBox.ScrollToEnd();
            ShowStatus(availability.Summary, false);
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
        ShowLatestRun(profile);
        UpdateActionSurface(profile, _currentTaskSnapshot);

        var dryRunActive = forceDryRun || profile.DryRun;
        var action = dryRunActive ? "Dry run" : "Run";
        ShowStatus(
            result.Succeeded ? $"{action} completed. Latest log is shown below." : $"{action} failed with exit code {result.ExitCode}.",
            result.Succeeded);
    }

    private async Task PollTaskCompletionAsync(BackupProfile profile)
    {
        for (var attempt = 0; attempt < 30; attempt++)
        {
            await Task.Delay(500);
            var snapshot = await _scheduledTasks.QueryAsync(profile);
            _currentTaskSnapshot = snapshot;
            TaskSummaryTextBlock.Text = $"{snapshot.State} | {profile.Engine} | {profile.Target.Kind}";
            UpdateActionSurface(profile, snapshot);

            if (snapshot.State != ScheduledTaskState.Running)
            {
                return;
            }
        }

        ShowStatus("Scheduled task is still running. Refresh status for the final result.", true);
    }

    private void ShowLatestRun(BackupProfile profile)
    {
        var summary = _logReader.ReadLatest(profile);
        if (summary is null)
        {
            LatestLogTextBlock.Text = "No log has been written for this profile.";
            RunSummaryTextBlock.Text = "";
            return;
        }

        LatestLogTextBlock.Text = summary.LogPath;
        RunSummaryTextBlock.Text = summary.ToDisplayString();
        OutputTextBox.Text = summary.Tail;
        OutputTextBox.ScrollToEnd();
    }

    private void LoadProfileIntoForm(BackupProfile? profile)
    {
        _loadingProfile = true;

        try
        {
            if (profile is null)
            {
                HeaderTextBlock.Text = "Replicator";
                TaskSummaryTextBlock.Text = "No profile selected.";
                AvailabilityTextBlock.Text = "Availability not checked.";
                DriveSecurityTextBlock.Text = "Drive security not checked.";
                _currentTaskSnapshot = null;
                UpdateActionSurface(null, null);
                return;
            }

            Normalize(profile);
            HeaderTextBlock.Text = profile.Name;
            NameTextBox.Text = profile.Name;
            SourcePathTextBox.Text = profile.SourcePath;
            ModeComboBox.SelectedItem = profile.Mode;
            DestinationLabel.Content = profile.Mode == ProfileMode.Shuttle ? "Shuttle path" : "Destination";
            DestinationPathTextBox.Text = profile.Target.Path;
            CadenceComboBox.SelectedItem = profile.Schedule.Cadence;
            TimeTextBox.Text = profile.Schedule.TimeOfDay.ToString("HH:mm", CultureInfo.InvariantCulture);
            DayOfWeekComboBox.SelectedItem = profile.Schedule.DayOfWeek;
            IntervalHoursTextBox.Text = profile.Schedule.Cadence == ScheduleCadence.Minutes
                ? profile.Schedule.IntervalMinutes.ToString(CultureInfo.InvariantCulture)
                : profile.Schedule.IntervalHours.ToString(CultureInfo.InvariantCulture);
            UpdateIntervalLabel(profile.Schedule.Cadence);
            ScheduleEnabledCheckBox.IsChecked = profile.Schedule.Enabled;
            DryRunCheckBox.IsChecked = profile.DryRun;
            MirrorDeletesCheckBox.IsChecked = profile.MirrorDeletes;
            ExcludePatternsTextBox.Text = string.Join(Environment.NewLine, profile.ExcludePatterns);
            TaskNameTextBlock.Text = ScheduledTaskName.ForProfile(profile);
            NextRunTextBlock.Text = "";
            LastRunTextBlock.Text = "";
            ShowLatestRun(profile);
            ShowAvailability(_availabilityChecker.Check(profile));
            DriveSecurityTextBlock.Text = "Drive security not checked.";
            UpdateActionSurface(profile, _currentTaskSnapshot);
        }
        finally
        {
            _loadingProfile = false;
        }
    }

    private bool TryApplyForm(out BackupProfile profile)
    {
        profile = null!;

        if (_loadingProfile)
        {
            return false;
        }

        var selectedProfile = GetSelectedProfile();
        if (selectedProfile is null)
        {
            return false;
        }

        Normalize(selectedProfile);

        selectedProfile.Name = NameTextBox.Text.Trim();
        selectedProfile.SourcePath = SourcePathTextBox.Text.Trim();
        if (ModeComboBox.SelectedItem is ProfileMode mode)
        {
            selectedProfile.Mode = mode;
        }

        selectedProfile.Target.Kind = BackupTargetKind.LocalPath;
        selectedProfile.Target.Path = DestinationPathTextBox.Text.Trim();
        selectedProfile.Engine = BackupEngineKind.NativePowerShell;
        selectedProfile.Schedule.Enabled = ScheduleEnabledCheckBox.IsChecked == true;
        selectedProfile.DryRun = DryRunCheckBox.IsChecked == true;
        selectedProfile.MirrorDeletes = MirrorDeletesCheckBox.IsChecked == true;
        selectedProfile.ExcludePatterns = ParseExcludePatterns(ExcludePatternsTextBox.Text);

        if (CadenceComboBox.SelectedItem is ScheduleCadence cadence)
        {
            selectedProfile.Schedule.Cadence = cadence;
        }

        if (DayOfWeekComboBox.SelectedItem is DayOfWeek dayOfWeek)
        {
            selectedProfile.Schedule.DayOfWeek = dayOfWeek;
        }

        if (!TimeOnly.TryParse(TimeTextBox.Text.Trim(), CultureInfo.CurrentCulture, DateTimeStyles.None, out var timeOfDay))
        {
            ShowStatus("Start time must be a valid time.", false);
            return false;
        }

        selectedProfile.Schedule.TimeOfDay = timeOfDay;

        if (!int.TryParse(IntervalHoursTextBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var interval))
        {
            ShowStatus("Interval must be a number.", false);
            return false;
        }

        if (selectedProfile.Schedule.Cadence == ScheduleCadence.Minutes)
        {
            selectedProfile.Schedule.IntervalMinutes = interval;
        }
        else
        {
            selectedProfile.Schedule.IntervalHours = interval;
        }

        var issues = BackupProfileValidator.Validate(selectedProfile);
        if (issues.Count > 0)
        {
            ShowStatus(string.Join(" ", issues.Select(issue => issue.Message)), false);
            return false;
        }

        profile = selectedProfile;
        ProfilesList.Items.Refresh();
        ShowAvailability(_availabilityChecker.Check(profile));
        UpdateActionSurface(profile, _currentTaskSnapshot);
        return true;
    }

    private BackupProfile? GetSelectedProfile(bool showStatus = true)
    {
        if (ProfilesList.SelectedItem is BackupProfile profile)
        {
            return profile;
        }

        if (showStatus)
        {
            ShowStatus("Select a profile.", false);
        }

        return null;
    }

    private static BackupProfile Normalize(BackupProfile profile)
    {
        profile.Target ??= new BackupTarget();
        profile.Schedule ??= new BackupSchedule();
        profile.ExcludePatterns ??= [];
        return profile;
    }

    private static List<string> ParseExcludePatterns(string text)
    {
        return text.Split(
                ["\r\n", "\n", ",", ";"],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void UpdateIntervalLabel(ScheduleCadence cadence)
    {
        IntervalLabel.Content = cadence switch
        {
            ScheduleCadence.Minutes => "Minute interval",
            ScheduleCadence.Hourly => "Hourly interval",
            _ => "Interval"
        };
    }

    private static void BrowseForFolder(System.Windows.Controls.TextBox target)
    {
        using var dialog = new WinForms.FolderBrowserDialog
        {
            InitialDirectory = Directory.Exists(target.Text) ? target.Text : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ShowNewFolderButton = true
        };

        if (dialog.ShowDialog() == WinForms.DialogResult.OK)
        {
            target.Text = dialog.SelectedPath;
        }
    }

    private void ShowStatus(string message, bool success)
    {
        StatusTextBlock.Text = message;
        StatusTextBlock.Foreground = success
            ? (System.Windows.Media.Brush)FindResource("StatusSuccessBrush")
            : (System.Windows.Media.Brush)FindResource("StatusErrorBrush");
    }

    private void ShowAvailability(ProfileAvailabilityReport report)
    {
        AvailabilityTextBlock.Text = report.Summary;
        AvailabilityTextBlock.Foreground = report.HasErrors
            ? (System.Windows.Media.Brush)FindResource("StatusErrorBrush")
            : report.HasWarnings
                ? (System.Windows.Media.Brush)FindResource("WarningBrush")
                : (System.Windows.Media.Brush)FindResource("TextMutedBrush");
    }

    private async Task RefreshDriveSecurityAsync(BackupProfile profile)
    {
        var profileId = profile.Id;
        DriveSecurityTextBlock.Text = "Drive security: checking...";
        DriveSecurityTextBlock.Foreground = (System.Windows.Media.Brush)FindResource("TextMutedBrush");

        try
        {
            var report = await _driveSecurityChecker.CheckAsync(profile);
            if (GetSelectedProfile(showStatus: false)?.Id != profileId)
            {
                return;
            }

            ShowDriveSecurity(report);
        }
        catch (Exception exception)
        {
            if (GetSelectedProfile(showStatus: false)?.Id != profileId)
            {
                return;
            }

            DriveSecurityTextBlock.Text = $"Drive security: check failed. {exception.Message}";
            DriveSecurityTextBlock.Foreground = (System.Windows.Media.Brush)FindResource("WarningBrush");
        }
    }

    private void ShowDriveSecurity(ProfileDriveSecurityReport report)
    {
        DriveSecurityTextBlock.Text = report.Summary;
        DriveSecurityTextBlock.Foreground = report.HasErrors
            ? (System.Windows.Media.Brush)FindResource("StatusErrorBrush")
            : report.HasWarnings
                ? (System.Windows.Media.Brush)FindResource("WarningBrush")
                : (System.Windows.Media.Brush)FindResource("TextMutedBrush");
    }

    private void ShowShuttleProgress(ShuttleOperationProgress progress)
    {
        if (progress.TotalFiles <= 0)
        {
            ActivityProgressBar.IsIndeterminate = true;
        }
        else
        {
            ActivityProgressBar.IsIndeterminate = false;
            ActivityProgressBar.Minimum = 0;
            ActivityProgressBar.Maximum = progress.TotalFiles;
            ActivityProgressBar.Value = Math.Min(progress.ProcessedFiles, progress.TotalFiles);
        }

        ShowStatus(
            progress.TotalFiles <= 0
                ? progress.Message
                : $"{progress.Message} {progress.PercentComplete:0}%",
            true);
    }

    private async Task RunBusyAsync(string busyMessage, Func<Task> operation)
    {
        await RunBusyAsync(busyMessage, _ => operation(), canCancel: false);
    }

    private async Task RunBusyAsync(
        string busyMessage,
        Func<CancellationToken, Task> operation,
        bool canCancel)
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
            ShowStatus("Operation canceled.", false);
        }
        catch (Exception exception)
        {
            AppendOutput(exception.ToString());
            ShowStatus(exception.Message, false);
        }
        finally
        {
            _currentOperationCancellationSource = null;
            SetBusy(false, "");
        }
    }

    private void SetBusy(bool busy, string message)
    {
        _isBusy = busy;
        ActivityProgressBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        ActivityProgressBar.IsIndeterminate = busy;
        UpdateActionSurface(GetSelectedProfile(showStatus: false), _currentTaskSnapshot);
        CancelOperationButton.Visibility = busy && _currentOperationCancellationSource is not null ? Visibility.Visible : Visibility.Collapsed;
        CancelOperationButton.IsEnabled = busy && _currentOperationCancellationSource is not null && !_currentOperationCancellationSource.IsCancellationRequested;

        if (busy)
        {
            ShowStatus(message, true);
        }
    }

    private void UpdateActionSurface(BackupProfile? profile, ScheduledTaskSnapshot? snapshot)
    {
        var hasProfile = profile is not null;
        var isBackupProfile = hasProfile && profile!.Mode == ProfileMode.Backup;
        var isShuttleProfile = hasProfile && profile!.Mode == ProfileMode.Shuttle;

        NewProfileButton.IsEnabled = !_isBusy;
        DeleteProfileButton.Visibility = hasProfile ? Visibility.Visible : Visibility.Collapsed;
        DeleteProfileButton.IsEnabled = hasProfile && !_isBusy;

        SetButton(SaveProfileButton, hasProfile, enabledWhenIdle: true);
        SetButton(GenerateScriptButton, isBackupProfile, enabledWhenIdle: true);
        SetButton(RunNowButton, isBackupProfile, enabledWhenIdle: true);
        SetButton(PreviewDryRunButton, isBackupProfile, enabledWhenIdle: true);

        var supportsScheduledTask = isBackupProfile && profile!.Schedule.Cadence != ScheduleCadence.Manual;
        var taskState = snapshot?.State ?? ScheduledTaskState.Missing;
        var taskExists = supportsScheduledTask && taskState != ScheduledTaskState.Missing;
        var taskRunning = taskState == ScheduledTaskState.Running;
        var taskDisabled = taskState == ScheduledTaskState.Disabled;
        var taskCanStart = taskState is ScheduledTaskState.Ready or ScheduledTaskState.Unknown;

        SetButton(InstallTaskButton, supportsScheduledTask && !taskRunning, enabledWhenIdle: true);
        InstallTaskButton.Content = taskExists ? "Update Task" : "Install Task";

        SetButton(StartScheduledTaskButton, taskExists && taskCanStart, enabledWhenIdle: true);
        SetButton(EnableTaskButton, taskExists && taskDisabled, enabledWhenIdle: true);
        SetButton(DisableTaskButton, taskExists && taskState == ScheduledTaskState.Ready, enabledWhenIdle: true);
        SetButton(RemoveTaskButton, taskExists && !taskRunning, enabledWhenIdle: true);
        SetButton(RefreshStatusButton, supportsScheduledTask, enabledWhenIdle: true);
        CancelOperationButton.Visibility = _isBusy && _currentOperationCancellationSource is not null ? Visibility.Visible : Visibility.Collapsed;
        CancelOperationButton.IsEnabled = _isBusy && _currentOperationCancellationSource is not null && !_currentOperationCancellationSource.IsCancellationRequested;

        SetButton(PrepareShuttleButton, isShuttleProfile, enabledWhenIdle: true);
        SetButton(DepartShuttleButton, isShuttleProfile, enabledWhenIdle: true);
        SetButton(DockShuttleButton, isShuttleProfile, enabledWhenIdle: true);
        SetButton(ReceiveShuttleButton, isShuttleProfile, enabledWhenIdle: true);

        if (!supportsScheduledTask)
        {
            TaskSummaryTextBlock.Text = hasProfile
                ? profile!.Mode == ProfileMode.Shuttle
                    ? $"{profile.Engine} | shuttle mode"
                    : $"{profile.Engine} | manual schedule"
                : "No profile selected.";
        }
    }

    private void SetButton(System.Windows.Controls.Button button, bool visible, bool enabledWhenIdle)
    {
        button.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        button.IsEnabled = visible && enabledWhenIdle && !_isBusy;
    }

    private void AppendOutput(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return;
        }

        OutputTextBox.AppendText(output.TrimEnd() + Environment.NewLine);
        OutputTextBox.ScrollToEnd();
    }
}
