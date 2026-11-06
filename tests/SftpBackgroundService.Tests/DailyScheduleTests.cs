using SftpBackgroundService.Scheduling;

namespace SftpBackgroundService.Tests;

public class DailyScheduleTests
{
    private static readonly TimeOnly TwoAm = new(2, 0);

    [Fact]
    public void NextRun_BeforeRunTimeToday_ReturnsToday()
    {
        var now = new DateTimeOffset(2026, 11, 6, 1, 0, 0, TimeSpan.Zero);

        var next = DailySchedule.NextRun(now, TwoAm, TimeZoneInfo.Utc);

        Assert.Equal(new DateTimeOffset(2026, 11, 6, 2, 0, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void NextRun_AfterRunTimeToday_ReturnsTomorrow()
    {
        var now = new DateTimeOffset(2026, 11, 6, 9, 0, 0, TimeSpan.Zero);

        var next = DailySchedule.NextRun(now, TwoAm, TimeZoneInfo.Utc);

        Assert.Equal(new DateTimeOffset(2026, 11, 7, 2, 0, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void NextRun_ExactlyAtRunTime_ReturnsTomorrow()
    {
        var now = new DateTimeOffset(2026, 11, 6, 2, 0, 0, TimeSpan.Zero);

        var next = DailySchedule.NextRun(now, TwoAm, TimeZoneInfo.Utc);

        Assert.Equal(new DateTimeOffset(2026, 11, 7, 2, 0, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void NextRun_HonoursConfiguredTimeZone()
    {
        var pacific = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");
        // 2026-11-06 is after the US fall-back, so Pacific is UTC-8. 02:00 Pacific == 10:00 UTC.
        var now = new DateTimeOffset(2026, 11, 6, 9, 0, 0, TimeSpan.Zero);

        var next = DailySchedule.NextRun(now, TwoAm, pacific);

        Assert.Equal(new DateTimeOffset(2026, 11, 6, 10, 0, 0, TimeSpan.Zero), next.ToUniversalTime());
    }

    [Fact]
    public void NextRun_IsAlwaysStrictlyInTheFuture_AndAtMostOneDayAway()
    {
        var now = DateTimeOffset.UtcNow;

        var next = DailySchedule.NextRun(now, TwoAm, TimeZoneInfo.Local);

        Assert.True(next > now);
        Assert.True(next - now <= TimeSpan.FromDays(1) + TimeSpan.FromHours(1));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Local")]
    [InlineData("local")]
    public void ResolveTimeZone_LocalAliases_ReturnLocal(string? id)
    {
        Assert.Same(TimeZoneInfo.Local, DailySchedule.ResolveTimeZone(id));
    }

    [Fact]
    public void ResolveTimeZone_Utc_ReturnsUtc()
    {
        Assert.Same(TimeZoneInfo.Utc, DailySchedule.ResolveTimeZone("utc"));
    }
}
