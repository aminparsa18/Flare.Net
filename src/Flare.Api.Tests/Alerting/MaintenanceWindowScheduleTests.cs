using Flare.Api.Alerting;
using Flare.Api.Model;
using Xunit;

namespace Flare.Api.Tests.Alerting;

/// <summary>
/// Covers <see cref="MaintenanceWindowSchedule"/> - the pure "is this window active now"
/// decision behind maintenance-window alert suppression. Reading windows from ClickHouse and
/// recording the suppressed event live in <c>AlertEvaluationWorker</c> and are covered by
/// end-to-end runs, same split as the rest of this project.
/// </summary>
public class MaintenanceWindowScheduleTests
{
    private static MaintenanceWindow Window(
        DateTimeOffset startsAt,
        TimeSpan duration,
        MaintenanceWindowRecurrence recurrence = MaintenanceWindowRecurrence.None,
        DayOfWeek[]? days = null,
        DateTimeOffset? repeatUntil = null,
        string timeZone = "UTC",
        Guid[]? ruleIds = null) => new()
    {
        Id = Guid.NewGuid(),
        Name = "deploy",
        RuleIds = ruleIds ?? [],
        StartsAt = startsAt,
        EndsAt = startsAt + duration,
        Recurrence = recurrence,
        DaysOfWeek = days ?? [],
        RepeatUntil = repeatUntil,
        TimeZone = timeZone,
        CreatedAt = startsAt,
        UpdatedAt = startsAt,
    };

    private static DateTimeOffset Utc(int month, int day, int hour, int minute = 0) => new(2026, month, day, hour, minute, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(9, 59, false)]
    [InlineData(10, 0, true)] // start is inclusive
    [InlineData(11, 59, true)]
    [InlineData(12, 0, false)] // end is exclusive
    public void OneOff_ActiveOnlyBetweenStartAndEnd(int hour, int minute, bool expected)
    {
        var window = Window(Utc(9, 25, 10), TimeSpan.FromHours(2));

        Assert.Equal(expected, MaintenanceWindowSchedule.IsActive(window, Utc(9, 25, hour, minute)));
    }

    [Fact]
    public void Daily_RepeatsEveryDay_AtTheSameTime()
    {
        var window = Window(Utc(9, 1, 2), TimeSpan.FromHours(1), MaintenanceWindowRecurrence.Daily);

        Assert.True(MaintenanceWindowSchedule.IsActive(window, Utc(9, 20, 2, 30)));
        Assert.False(MaintenanceWindowSchedule.IsActive(window, Utc(9, 20, 3, 30)));
        Assert.False(MaintenanceWindowSchedule.IsActive(window, Utc(8, 31, 2, 30))); // before the first occurrence
    }

    [Fact]
    public void Daily_OvernightOccurrence_CoversPastMidnight()
    {
        var window = Window(Utc(9, 1, 23), TimeSpan.FromHours(3), MaintenanceWindowRecurrence.Daily);

        Assert.True(MaintenanceWindowSchedule.IsActive(window, Utc(9, 20, 1)));
        Assert.False(MaintenanceWindowSchedule.IsActive(window, Utc(9, 20, 2)));
    }

    [Fact]
    public void Daily_KeepsLocalTimeOfDayAcrossDst()
    {
        // 02:00 Berlin in January is 01:00 UTC (CET); in July it's 00:00 UTC (CEST).
        var window = Window(Utc(1, 10, 1), TimeSpan.FromHours(1), MaintenanceWindowRecurrence.Daily, timeZone: "Europe/Berlin");

        Assert.True(MaintenanceWindowSchedule.IsActive(window, Utc(7, 1, 0, 30)));
        Assert.False(MaintenanceWindowSchedule.IsActive(window, Utc(7, 1, 1, 30)));
    }

    [Fact]
    public void Daily_StartSkippedBySpringForward_ShiftsAnHourLater()
    {
        // 02:30 Berlin doesn't exist on 2026-03-29 (clocks jump 02:00 -> 03:00 CEST), so that
        // day's occurrence starts at 03:30 CEST = 01:30 UTC.
        var window = Window(Utc(1, 10, 1, 30), TimeSpan.FromMinutes(30), MaintenanceWindowRecurrence.Daily, timeZone: "Europe/Berlin");

        Assert.True(MaintenanceWindowSchedule.IsActive(window, Utc(3, 29, 1, 45)));
        Assert.False(MaintenanceWindowSchedule.IsActive(window, Utc(3, 29, 0, 45)));
    }

    [Theory]
    [InlineData(26, true)] // Saturday
    [InlineData(27, true)] // Sunday
    [InlineData(28, false)] // Monday
    [InlineData(25, false)] // Friday
    public void Weekly_ActiveOnlyOnSelectedDays(int day, bool expected)
    {
        var window = Window(Utc(9, 1, 0), TimeSpan.FromHours(6), MaintenanceWindowRecurrence.Weekly, days: [DayOfWeek.Saturday, DayOfWeek.Sunday]);

        Assert.Equal(expected, MaintenanceWindowSchedule.IsActive(window, Utc(9, day, 3)));
    }

    [Fact]
    public void Weekly_OccurrenceSpanningIntoAnUnselectedDay_StillCoversIt()
    {
        // Friday 22:00 for 6 hours ends Saturday 04:00, even though only Friday is selected.
        var window = Window(Utc(9, 4, 22), TimeSpan.FromHours(6), MaintenanceWindowRecurrence.Weekly, days: [DayOfWeek.Friday]);

        Assert.True(MaintenanceWindowSchedule.IsActive(window, Utc(9, 26, 2)));
        Assert.False(MaintenanceWindowSchedule.IsActive(window, Utc(9, 26, 5)));
    }

    [Fact]
    public void Weekly_WeekdaysAreLocalToTheTimeZone()
    {
        // Monday 01:00 in Tokyo is Sunday 16:00 UTC - the window is Monday-only locally.
        var window = Window(new DateTimeOffset(2026, 9, 7, 1, 0, 0, TimeSpan.FromHours(9)), TimeSpan.FromHours(1), MaintenanceWindowRecurrence.Weekly, days: [DayOfWeek.Monday], timeZone: "Asia/Tokyo");

        Assert.True(MaintenanceWindowSchedule.IsActive(window, Utc(9, 27, 16, 30)));
        Assert.False(MaintenanceWindowSchedule.IsActive(window, Utc(9, 28, 16, 30)));
    }

    [Fact]
    public void RepeatUntil_StopsLaterOccurrences()
    {
        var window = Window(Utc(9, 1, 2), TimeSpan.FromHours(1), MaintenanceWindowRecurrence.Daily, repeatUntil: Utc(9, 10, 0));

        Assert.True(MaintenanceWindowSchedule.IsActive(window, Utc(9, 9, 2, 30)));
        Assert.False(MaintenanceWindowSchedule.IsActive(window, Utc(9, 10, 2, 30)));
    }

    [Fact]
    public void UnknownTimeZone_FallsBackToUtc()
    {
        var window = Window(Utc(9, 1, 2), TimeSpan.FromHours(1), MaintenanceWindowRecurrence.Daily, timeZone: "Not/AZone");

        Assert.True(MaintenanceWindowSchedule.IsActive(window, Utc(9, 20, 2, 30)));
    }

    [Fact]
    public void EmptyRuleIds_CoversEveryRule()
    {
        Assert.True(MaintenanceWindowSchedule.Covers(Window(Utc(9, 1, 0), TimeSpan.FromHours(1)), Guid.NewGuid()));
    }

    [Fact]
    public void FindActive_OnlyMatchesActiveWindowsCoveringTheRule()
    {
        var rule = Guid.NewGuid();
        var otherRule = Window(Utc(9, 25, 10), TimeSpan.FromHours(2), ruleIds: [Guid.NewGuid()]);
        var inactive = Window(Utc(9, 24, 10), TimeSpan.FromHours(2), ruleIds: [rule]);
        var match = Window(Utc(9, 25, 11), TimeSpan.FromHours(2), ruleIds: [rule]);

        Assert.Same(match, MaintenanceWindowSchedule.FindActive([otherRule, inactive, match], rule, Utc(9, 25, 11, 30)));
        Assert.Null(MaintenanceWindowSchedule.FindActive([otherRule, inactive], rule, Utc(9, 25, 11, 30)));
    }
}
