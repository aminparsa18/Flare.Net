using Flare.Api.Model;
using Xunit;

namespace Flare.Api.Tests.Model;

/// <summary>Covers <see cref="MaintenanceWindowRequest.Validate"/>.</summary>
public class MaintenanceWindowValidationTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 25, 10, 0, 0, TimeSpan.Zero);

    private static MaintenanceWindowRequest Request(
        TimeSpan? duration = null,
        MaintenanceWindowRecurrence? recurrence = null,
        DayOfWeek[]? days = null,
        DateTimeOffset? repeatUntil = null,
        string? timeZone = null,
        string name = "deploy") => new()
    {
        Name = name,
        StartsAt = Start,
        EndsAt = Start + (duration ?? TimeSpan.FromHours(1)),
        Recurrence = recurrence,
        DaysOfWeek = days,
        RepeatUntil = repeatUntil,
        TimeZone = timeZone,
    };

    [Fact]
    public void OneOff_IsValid()
    {
        Assert.Null(Request().Validate());
    }

    [Fact]
    public void Weekly_WithDaysAndZone_IsValid()
    {
        Assert.Null(Request(recurrence: MaintenanceWindowRecurrence.Weekly, days: [DayOfWeek.Saturday, DayOfWeek.Sunday], timeZone: "Europe/Berlin", repeatUntil: Start.AddDays(30)).Validate());
    }

    [Fact]
    public void BlankName_IsRejected()
    {
        Assert.NotNull(Request(name: " ").Validate());
    }

    [Fact]
    public void EndNotAfterStart_IsRejected()
    {
        Assert.NotNull(Request(duration: TimeSpan.Zero).Validate());
    }

    [Fact]
    public void UnknownTimeZone_IsRejected()
    {
        Assert.NotNull(Request(timeZone: "Not/AZone").Validate());
    }

    [Fact]
    public void OneOff_WithRepeatUntilOrDays_IsRejected()
    {
        Assert.NotNull(Request(repeatUntil: Start.AddDays(1)).Validate());
        Assert.NotNull(Request(days: [DayOfWeek.Monday]).Validate());
    }

    [Fact]
    public void Daily_LongerThanADay_IsRejected()
    {
        Assert.NotNull(Request(duration: TimeSpan.FromHours(25), recurrence: MaintenanceWindowRecurrence.Daily).Validate());
    }

    [Fact]
    public void Weekly_WithoutDays_IsRejected()
    {
        Assert.NotNull(Request(recurrence: MaintenanceWindowRecurrence.Weekly).Validate());
    }

    [Fact]
    public void Weekly_DuplicateDays_IsRejected()
    {
        Assert.NotNull(Request(recurrence: MaintenanceWindowRecurrence.Weekly, days: [DayOfWeek.Monday, DayOfWeek.Monday]).Validate());
    }

    [Fact]
    public void Weekly_LongerThanAWeek_IsRejected()
    {
        Assert.NotNull(Request(duration: TimeSpan.FromDays(8), recurrence: MaintenanceWindowRecurrence.Weekly, days: [DayOfWeek.Monday]).Validate());
    }

    [Fact]
    public void RepeatUntil_NotAfterStart_IsRejected()
    {
        Assert.NotNull(Request(recurrence: MaintenanceWindowRecurrence.Daily, repeatUntil: Start).Validate());
    }
}
