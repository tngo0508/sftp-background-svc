namespace SftpBackgroundService.Options;

/// <summary>When the daily SFTP download runs. Bound from the "Schedule" section.</summary>
public sealed class ScheduleOptions
{
    public const string SectionName = "Schedule";

    /// <summary>Wall-clock time of day in <see cref="TimeZone"/>, e.g. "02:00" or "14:30:00".</summary>
    public TimeOnly RunAt { get; set; } = new(2, 0);

    /// <summary>"Local", "UTC", or any system time zone id (Windows or IANA, e.g. "America/Los_Angeles").</summary>
    public string TimeZone { get; set; } = "Local";

    /// <summary>Also run a download immediately when the service starts. Handy for development and for catching up after downtime.</summary>
    public bool RunOnStartup { get; set; }
}
