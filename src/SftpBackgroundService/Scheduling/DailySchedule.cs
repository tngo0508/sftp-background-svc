namespace SftpBackgroundService.Scheduling;

/// <summary>Pure time arithmetic for a once-a-day schedule, kept free of I/O so it is trivially testable.</summary>
public static class DailySchedule
{
    /// <summary>Returns the next instant strictly after <paramref name="now"/> at which the wall clock in <paramref name="timeZone"/> reads <paramref name="runAt"/>.</summary>
    public static DateTimeOffset NextRun(DateTimeOffset now, TimeOnly runAt, TimeZoneInfo timeZone)
    {
        var localNow = TimeZoneInfo.ConvertTime(now, timeZone);
        var today = DateOnly.FromDateTime(localNow.DateTime);

        var candidate = ToInstant(today, runAt, timeZone);
        if (candidate <= now)
        {
            candidate = ToInstant(today.AddDays(1), runAt, timeZone);
        }

        return candidate;
    }

    public static TimeZoneInfo ResolveTimeZone(string? id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Equals("Local", StringComparison.OrdinalIgnoreCase))
        {
            return TimeZoneInfo.Local;
        }

        if (id.Equals("UTC", StringComparison.OrdinalIgnoreCase))
        {
            return TimeZoneInfo.Utc;
        }

        return TimeZoneInfo.FindSystemTimeZoneById(id);
    }

    private static DateTimeOffset ToInstant(DateOnly date, TimeOnly time, TimeZoneInfo timeZone)
    {
        var wallClock = date.ToDateTime(time, DateTimeKind.Unspecified);

        // Spring-forward gap: a 02:30 run on the day the clock jumps from 02:00 to 03:00 does not exist. Push it forward an hour.
        if (timeZone.IsInvalidTime(wallClock))
        {
            wallClock = wallClock.AddHours(1);
        }

        return new DateTimeOffset(wallClock, timeZone.GetUtcOffset(wallClock));
    }
}
