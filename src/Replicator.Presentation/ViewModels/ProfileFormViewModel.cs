using System.Globalization;
using Replicator.Core.Models;
using Replicator.Presentation.State;

namespace Replicator.Presentation.ViewModels;

public sealed class ProfileFormViewModel : ObservableObject
{
    private string _name = "";
    private string _sourcePath = "";
    private string _destinationPath = "";
    private ProfileMode _mode;
    private ScheduleCadence _cadence;
    private string _timeText = "";
    private DayOfWeek _dayOfWeek;
    private string _intervalText = "";
    private bool _scheduleEnabled;
    private bool _dryRun;
    private bool _mirrorDeletes;
    private string _excludePatternsText = "";

    public string Name { get => _name; set => SetProperty(ref _name, value); }
    public string SourcePath { get => _sourcePath; set => SetProperty(ref _sourcePath, value); }
    public string DestinationPath { get => _destinationPath; set => SetProperty(ref _destinationPath, value); }

    public ProfileMode Mode
    {
        get => _mode;
        set
        {
            if (SetProperty(ref _mode, value))
            {
                OnPropertyChanged(nameof(DestinationLabel));
            }
        }
    }

    public ScheduleCadence Cadence
    {
        get => _cadence;
        set
        {
            if (SetProperty(ref _cadence, value))
            {
                OnPropertyChanged(nameof(IntervalLabel));
            }
        }
    }

    public string TimeText { get => _timeText; set => SetProperty(ref _timeText, value); }
    public DayOfWeek DayOfWeek { get => _dayOfWeek; set => SetProperty(ref _dayOfWeek, value); }
    public string IntervalText { get => _intervalText; set => SetProperty(ref _intervalText, value); }
    public bool ScheduleEnabled { get => _scheduleEnabled; set => SetProperty(ref _scheduleEnabled, value); }
    public bool DryRun { get => _dryRun; set => SetProperty(ref _dryRun, value); }
    public bool MirrorDeletes { get => _mirrorDeletes; set => SetProperty(ref _mirrorDeletes, value); }
    public string ExcludePatternsText { get => _excludePatternsText; set => SetProperty(ref _excludePatternsText, value); }

    public string DestinationLabel => Mode == ProfileMode.Shuttle ? "Shuttle path" : "Destination";

    public string IntervalLabel => Cadence switch
    {
        ScheduleCadence.Minutes => "Minute interval",
        ScheduleCadence.Hourly => "Hourly interval",
        _ => "Interval"
    };

    public void Load(BackupProfile profile)
    {
        Normalize(profile);
        Name = profile.Name;
        SourcePath = profile.SourcePath;
        Mode = profile.Mode;
        DestinationPath = profile.Target.Path;
        Cadence = profile.Schedule.Cadence;
        TimeText = profile.Schedule.TimeOfDay.ToString("HH:mm", CultureInfo.InvariantCulture);
        DayOfWeek = profile.Schedule.DayOfWeek;
        IntervalText = profile.Schedule.Cadence == ScheduleCadence.Minutes
            ? profile.Schedule.IntervalMinutes.ToString(CultureInfo.InvariantCulture)
            : profile.Schedule.IntervalHours.ToString(CultureInfo.InvariantCulture);
        ScheduleEnabled = profile.Schedule.Enabled;
        DryRun = profile.DryRun;
        MirrorDeletes = profile.MirrorDeletes;
        ExcludePatternsText = string.Join(Environment.NewLine, profile.ExcludePatterns);
    }

    public FormApplyResult TryApply(BackupProfile profile)
    {
        Normalize(profile);
        profile.Name = Name.Trim();
        profile.SourcePath = SourcePath.Trim();
        profile.Mode = Mode;
        profile.Target.Kind = BackupTargetKind.LocalPath;
        profile.Target.Path = DestinationPath.Trim();
        profile.Engine = BackupEngineKind.NativePowerShell;
        profile.Schedule.Enabled = ScheduleEnabled;
        profile.DryRun = DryRun;
        profile.MirrorDeletes = MirrorDeletes;
        profile.ExcludePatterns = ParseExcludePatterns(ExcludePatternsText);
        profile.Schedule.Cadence = Cadence;
        profile.Schedule.DayOfWeek = DayOfWeek;

        if (!TimeOnly.TryParse(TimeText.Trim(), CultureInfo.CurrentCulture, DateTimeStyles.None, out var timeOfDay))
        {
            return new FormApplyResult(false, "Start time must be a valid time.");
        }

        profile.Schedule.TimeOfDay = timeOfDay;

        if (!int.TryParse(IntervalText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var interval))
        {
            return new FormApplyResult(false, "Interval must be a number.");
        }

        if (profile.Schedule.Cadence == ScheduleCadence.Minutes)
        {
            profile.Schedule.IntervalMinutes = interval;
        }
        else
        {
            profile.Schedule.IntervalHours = interval;
        }

        var issues = BackupProfileValidator.Validate(profile);
        return issues.Count == 0
            ? new FormApplyResult(true, "Profile applied.")
            : new FormApplyResult(false, string.Join(" ", issues.Select(issue => issue.Message)));
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
}

public sealed record FormApplyResult(bool Succeeded, string Message);
