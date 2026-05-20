namespace Replicator.Core.Models;

public sealed class BackupSchedule
{
    public bool Enabled { get; set; } = true;

    public ScheduleCadence Cadence { get; set; } = ScheduleCadence.Daily;

    public TimeOnly TimeOfDay { get; set; } = new(18, 0);

    public DayOfWeek DayOfWeek { get; set; } = DayOfWeek.Monday;

    public int IntervalHours { get; set; } = 6;

    public int IntervalMinutes { get; set; } = 15;
}
