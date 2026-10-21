using Flare.Api.Model;
using Xunit;

namespace Flare.Api.Tests.Model;

public class CheckInRunHistoryTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 15, 2, 0, 0, TimeSpan.Zero);

    private static CheckInPing Ping(int seconds, string status, string message = "") => new(T0.AddSeconds(seconds), status, message);

    [Fact]
    public void A_start_and_a_finish_make_one_run_with_a_duration()
    {
        var runs = CheckInRunHistory.Build([Ping(0, "start"), Ping(90, "ok")], 0, T0.AddHours(1));

        var run = Assert.Single(runs);
        Assert.Equal(T0, run.StartedAt);
        Assert.Equal(T0.AddSeconds(90), run.FinishedAt);
        Assert.Equal(90, run.DurationSeconds);
        Assert.Equal(CheckInRunOutcome.Ok, run.Outcome);
    }

    [Fact]
    public void Runs_come_back_newest_first_and_a_failure_keeps_its_message()
    {
        var runs = CheckInRunHistory.Build(
            [Ping(0, "start"), Ping(10, "ok"), Ping(3600, "start", "begin"), Ping(3630, "fail", "disk full")],
            0,
            T0.AddHours(2));

        Assert.Equal(2, runs.Count);
        Assert.Equal(CheckInRunOutcome.Failed, runs[0].Outcome);
        Assert.Equal("disk full", runs[0].Message);
        Assert.Equal(30, runs[0].DurationSeconds);
        Assert.Equal(CheckInRunOutcome.Ok, runs[1].Outcome);
    }

    [Fact]
    public void A_finish_without_a_start_is_a_run_with_no_duration()
    {
        var run = Assert.Single(CheckInRunHistory.Build([Ping(0, "ok")], 0, T0.AddHours(1)));

        Assert.Null(run.StartedAt);
        Assert.Null(run.DurationSeconds);
        Assert.Equal(T0, run.FinishedAt);
    }

    [Fact]
    public void A_start_replaced_by_a_later_start_is_unfinished()
    {
        var runs = CheckInRunHistory.Build([Ping(0, "start"), Ping(3600, "start"), Ping(3660, "ok")], 0, T0.AddHours(2));

        Assert.Equal(CheckInRunOutcome.Ok, runs[0].Outcome);
        Assert.Equal(60, runs[0].DurationSeconds);
        Assert.Equal(CheckInRunOutcome.Unfinished, runs[1].Outcome);
        Assert.Null(runs[1].DurationSeconds);
    }

    [Fact]
    public void A_trailing_start_is_running_until_its_maximum_runtime_passes()
    {
        var pings = new[] { Ping(0, "start") };

        Assert.Equal(CheckInRunOutcome.Running, CheckInRunHistory.Build(pings, 600, T0.AddSeconds(600)).Single().Outcome);
        Assert.Equal(CheckInRunOutcome.Unfinished, CheckInRunHistory.Build(pings, 600, T0.AddSeconds(601)).Single().Outcome);
        Assert.Equal(CheckInRunOutcome.Running, CheckInRunHistory.Build(pings, 0, T0.AddDays(3)).Single().Outcome);
    }

    [Fact]
    public void No_pings_make_no_runs() =>
        Assert.Empty(CheckInRunHistory.Build([], 0, T0));
}
