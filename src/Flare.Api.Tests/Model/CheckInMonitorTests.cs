using Flare.Api.Model;
using Xunit;

namespace Flare.Api.Tests.Model;

public class CheckInMonitorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 15, 0, 0, 0, TimeSpan.Zero);

    private static CheckInMonitor Hourly(int grace = 300, int maxRuntime = 0, bool enabled = true) => new()
    {
        Id = Guid.NewGuid(),
        Slug = "job",
        Name = "Job",
        IntervalSeconds = 3600,
        GraceSeconds = grace,
        MaxRuntimeSeconds = maxRuntime,
        Enabled = enabled,
        CreatedAt = T0,
        UpdatedAt = T0,
    };

    private static CheckInMonitor DailyAt2(string zone = "UTC", int grace = 300) => Hourly(grace) with
    {
        IntervalSeconds = 0,
        CronExpression = "0 2 * * *",
        TimeZone = zone,
    };

    private static CheckInPings Finished(DateTimeOffset at, bool failed = false) => new(null, at, failed);

    [Fact]
    public void A_new_monitor_is_pending_until_the_first_ping_is_overdue()
    {
        var monitor = Hourly();

        Assert.Equal(CheckInState.Pending, CheckInEvaluator.Evaluate(monitor, CheckInPings.None, T0.AddMinutes(10)).State);
        Assert.Equal(CheckInState.Pending, CheckInEvaluator.Evaluate(monitor, CheckInPings.None, T0.AddSeconds(3600 + 300)).State);
        Assert.Equal(CheckInState.Late, CheckInEvaluator.Evaluate(monitor, CheckInPings.None, T0.AddSeconds(3600 + 301)).State);
    }

    [Fact]
    public void A_finished_run_is_ok_until_the_next_one_is_overdue()
    {
        var monitor = Hourly();
        var pings = Finished(T0.AddMinutes(10));

        var ok = CheckInEvaluator.Evaluate(monitor, pings, T0.AddMinutes(30));
        Assert.Equal(CheckInState.Ok, ok.State);
        Assert.Equal(T0.AddMinutes(70), ok.NextExpected);
        Assert.True(ok.Up);

        Assert.Equal(CheckInState.Ok, CheckInEvaluator.Evaluate(monitor, pings, T0.AddMinutes(75)).State);
        var late = CheckInEvaluator.Evaluate(monitor, pings, T0.AddMinutes(75).AddSeconds(1));
        Assert.Equal(CheckInState.Late, late.State);
        Assert.False(late.Up);
    }

    [Fact]
    public void A_failed_run_stays_failed_until_a_run_succeeds()
    {
        var monitor = Hourly();

        var failed = CheckInEvaluator.Evaluate(monitor, Finished(T0.AddMinutes(10), failed: true), T0.AddMinutes(11));
        Assert.Equal(CheckInState.Failed, failed.State);
        Assert.False(failed.Up);

        // A new run starting does not clear the failure; only an ok finish does.
        var restarted = new CheckInPings(T0.AddMinutes(60), T0.AddMinutes(10), LastFinishFailed: true);
        Assert.Equal(CheckInState.Failed, CheckInEvaluator.Evaluate(monitor, restarted, T0.AddMinutes(61)).State);
        Assert.Equal(CheckInState.Ok, CheckInEvaluator.Evaluate(monitor, Finished(T0.AddMinutes(65)), T0.AddMinutes(66)).State);
    }

    [Fact]
    public void A_started_run_is_running_then_overran_past_its_maximum_runtime()
    {
        var monitor = Hourly(maxRuntime: 600);
        var pings = new CheckInPings(T0.AddMinutes(5), null, false);

        Assert.Equal(CheckInState.Running, CheckInEvaluator.Evaluate(monitor, pings, T0.AddMinutes(10)).State);
        Assert.Equal(CheckInState.Running, CheckInEvaluator.Evaluate(monitor, pings, T0.AddMinutes(15)).State);
        var overran = CheckInEvaluator.Evaluate(monitor, pings, T0.AddMinutes(15).AddSeconds(1));
        Assert.Equal(CheckInState.Overran, overran.State);
        Assert.False(overran.Up);
    }

    [Fact]
    public void A_run_with_no_maximum_runtime_that_never_finishes_is_late_at_the_next_scheduled_time()
    {
        var monitor = Hourly();
        var pings = new CheckInPings(T0.AddMinutes(5), null, false);

        Assert.Equal(CheckInState.Running, CheckInEvaluator.Evaluate(monitor, pings, T0.AddMinutes(30)).State);
        Assert.Equal(CheckInState.Late, CheckInEvaluator.Evaluate(monitor, pings, T0.AddMinutes(70).AddSeconds(1)).State);
    }

    [Fact]
    public void A_finish_after_the_start_ends_the_run()
    {
        var pings = new CheckInPings(T0.AddMinutes(5), T0.AddMinutes(9), false);
        Assert.Equal(CheckInState.Ok, CheckInEvaluator.Evaluate(Hourly(maxRuntime: 60), pings, T0.AddMinutes(20)).State);
    }

    [Fact]
    public void A_disabled_monitor_is_disabled_and_not_down()
    {
        var status = CheckInEvaluator.Evaluate(Hourly(enabled: false), CheckInPings.None, T0.AddDays(30));
        Assert.Equal(CheckInState.Disabled, status.State);
        Assert.True(status.Up);
    }

    [Fact]
    public void Editing_a_monitor_restarts_its_clock()
    {
        var monitor = Hourly() with { UpdatedAt = T0.AddDays(2) };

        // The only ping is two days older than the edit, which would be long overdue without the reset.
        Assert.Equal(CheckInState.Ok, CheckInEvaluator.Evaluate(monitor, Finished(T0), T0.AddDays(2).AddMinutes(30)).State);
    }

    [Fact]
    public void A_cron_schedule_is_read_in_its_time_zone()
    {
        // 02:00 UTC daily: a ping at 02:00:03 expects the next at 02:00 the following day.
        var utc = DailyAt2();
        var pings = Finished(T0.AddHours(2).AddSeconds(3));
        var tomorrow = T0.AddDays(1).AddHours(2);

        Assert.Equal(tomorrow, CheckInEvaluator.Evaluate(utc, pings, T0.AddHours(12)).NextExpected);
        Assert.Equal(CheckInState.Ok, CheckInEvaluator.Evaluate(utc, pings, tomorrow.AddSeconds(300)).State);
        Assert.Equal(CheckInState.Late, CheckInEvaluator.Evaluate(utc, pings, tomorrow.AddSeconds(301)).State);

        // 02:00 in New York is 07:00 UTC in January.
        var ny = DailyAt2("America/New_York");
        Assert.Equal(T0.AddDays(1).AddHours(7), CheckInEvaluator.NextExpected(ny, T0.AddHours(8)));
    }

    [Fact]
    public void LastPing_is_the_newer_of_start_and_finish()
    {
        Assert.Equal(T0.AddMinutes(9), new CheckInPings(T0.AddMinutes(5), T0.AddMinutes(9), false).LastPing);
        Assert.Equal(T0.AddMinutes(9), new CheckInPings(T0.AddMinutes(9), T0.AddMinutes(5), false).LastPing);
        Assert.Equal(T0, new CheckInPings(T0, null, false).LastPing);
        Assert.Null(CheckInPings.None.LastPing);
    }

    private static CheckInMonitorRequest Request(string? cron = null, int? interval = null) => new()
    {
        Name = "Nightly DB backup",
        CronExpression = cron,
        IntervalSeconds = interval,
    };

    [Theory]
    [InlineData("0 2 * * *", null)]
    [InlineData("@daily", null)]
    [InlineData(null, 3600)]
    public void A_schedule_of_exactly_one_kind_is_valid(string? cron, int? interval) =>
        Assert.Null(Request(cron, interval).Validate(creating: true));

    [Theory]
    [InlineData(null, null)]
    [InlineData("0 2 * * *", 3600)]
    [InlineData("not a cron", null)]
    [InlineData("0 0 2 * * *", null)]
    [InlineData(null, 59)]
    [InlineData(null, CheckInMonitorRequest.MaxIntervalSeconds + 1)]
    public void A_missing_double_or_malformed_schedule_is_rejected(string? cron, int? interval) =>
        Assert.NotNull(Request(cron, interval).Validate(creating: true));

    [Fact]
    public void Time_zone_grace_and_runtime_are_checked()
    {
        Assert.NotNull((Request("0 2 * * *") with { TimeZone = "Mars/Olympus" }).Validate(true));
        Assert.NotNull((Request(interval: 3600) with { GraceSeconds = -1 }).Validate(true));
        Assert.NotNull((Request(interval: 3600) with { MaxRuntimeSeconds = CheckInMonitorRequest.MaxRuntimeSecondsLimit + 1 }).Validate(true));
        Assert.Null((Request("0 2 * * *") with { TimeZone = "Europe/Berlin", GraceSeconds = 0, MaxRuntimeSeconds = 600 }).Validate(true));
    }

    [Fact]
    public void The_slug_is_derived_from_the_name_unless_given()
    {
        Assert.Equal("nightly-db-backup", Request(interval: 3600).EffectiveSlug());
        Assert.Equal("db", (Request(interval: 3600) with { Slug = " db " }).EffectiveSlug());
        Assert.Null((Request(interval: 3600) with { Slug = "db" }).Validate(creating: true));
        Assert.NotNull((Request(interval: 3600) with { Slug = "Bad Slug" }).Validate(creating: true));
        // A slug is only checked on create: update ignores it.
        Assert.Null((Request(interval: 3600) with { Slug = "Bad Slug" }).Validate(creating: false));
    }

    [Theory]
    [InlineData("Nightly DB backup!", "nightly-db-backup")]
    [InlineData("  --Hello__World--  ", "hello-world")]
    [InlineData("a/b/c", "a-b-c")]
    [InlineData("!!!", "")]
    public void Slugify_reduces_text_to_a_slug(string text, string expected) =>
        Assert.Equal(expected, CheckInMonitorRequest.Slugify(text));
}
