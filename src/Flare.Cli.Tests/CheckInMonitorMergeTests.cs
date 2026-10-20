using Flare.Cli.Commands;
using Xunit;

namespace Flare.Cli.Tests;

public class CheckInMonitorMergeTests
{
    private static CheckInMonitorWire Cron() => new()
    {
        Id = Guid.NewGuid(), Slug = "backup", Name = "Backup", CronExpression = "0 2 * * *", TimeZone = "Europe/Berlin", GraceSeconds = 600,
    };

    private static CheckInMonitorWire Interval() => new()
    {
        Id = Guid.NewGuid(), Slug = "poll", Name = "Poll", IntervalSeconds = 300,
    };

    [Fact]
    public void NoOptions_KeepsCronSchedule()
    {
        var request = CheckInMonitorFormat.MergeUpdate(Cron(), null, null, null, null, null, null, null, null);

        Assert.Equal("0 2 * * *", request.CronExpression);
        Assert.Null(request.IntervalSeconds);
        Assert.Equal("Europe/Berlin", request.TimeZone);
        Assert.Equal(600, request.GraceSeconds);
    }

    [Fact]
    public void NoOptions_KeepsIntervalSchedule()
    {
        var request = CheckInMonitorFormat.MergeUpdate(Interval(), null, null, null, null, null, null, null, null);

        Assert.Null(request.CronExpression);
        Assert.Equal(300, request.IntervalSeconds);
    }

    [Fact]
    public void Interval_ReplacesCron()
    {
        var request = CheckInMonitorFormat.MergeUpdate(Cron(), null, null, null, 900, null, null, null, null);

        Assert.Null(request.CronExpression);
        Assert.Equal(900, request.IntervalSeconds);
    }

    [Fact]
    public void Cron_ReplacesInterval()
    {
        var request = CheckInMonitorFormat.MergeUpdate(Interval(), null, null, "@daily", null, null, null, null, null);

        Assert.Equal("@daily", request.CronExpression);
        Assert.Null(request.IntervalSeconds);
    }

    [Fact]
    public void OnlyPassedOptionsChange()
    {
        var request = CheckInMonitorFormat.MergeUpdate(Cron(), null, null, null, null, null, null, null, enabled: false);

        Assert.False(request.Enabled);
        Assert.Equal("Backup", request.Name);
    }

    [Theory]
    [InlineData(300, "5 min")]
    [InlineData(3600, "1 h")]
    [InlineData(86400, "1 d")]
    [InlineData(90, "90 s")]
    public void Duration_PicksLargestWholeUnit(int seconds, string expected) =>
        Assert.Equal(expected, CheckInMonitorFormat.Duration(seconds));
}
