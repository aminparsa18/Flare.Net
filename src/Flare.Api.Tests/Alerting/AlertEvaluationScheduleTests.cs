using Flare.Api.Alerting;
using Xunit;

namespace Flare.Api.Tests.Alerting;

/// <summary>
/// Covers <see cref="AlertEvaluationSchedule.IsDue"/> - the pure "is this rule due" decision
/// behind per-rule evaluation intervals. Reading/writing the Redis last-evaluated markers
/// themselves lives in <c>AlertEvaluationWorker</c> and is covered by end-to-end runs, same
/// split as the rest of this project.
/// </summary>
public class AlertEvaluationScheduleTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan Poll = TimeSpan.FromSeconds(30);

    [Fact]
    public void ZeroInterval_AlwaysDue_EvenJustEvaluated()
    {
        Assert.True(AlertEvaluationSchedule.IsDue(0, Now, Now, Poll));
    }

    [Fact]
    public void NeverEvaluated_IsDue()
    {
        Assert.True(AlertEvaluationSchedule.IsDue(900, lastEvaluatedAt: null, Now, Poll));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(30)]
    [InlineData(44)]
    public void WithinInterval_IsNotDue(int secondsAgo)
    {
        Assert.False(AlertEvaluationSchedule.IsDue(60, Now.AddSeconds(-secondsAgo), Now, Poll));
    }

    [Theory]
    [InlineData(45)] // interval - poll/2: a slightly-early tick still counts
    [InlineData(59)]
    [InlineData(60)]
    [InlineData(3_600)]
    public void AtOrPastIntervalMinusHalfPoll_IsDue(int secondsAgo)
    {
        Assert.True(AlertEvaluationSchedule.IsDue(60, Now.AddSeconds(-secondsAgo), Now, Poll));
    }

    [Fact]
    public void FifteenMinuteRule_OnThirtySecondPoll_SkipsIntermediateTicks()
    {
        var last = Now;
        var evaluations = 0;
        for (var tick = 1; tick <= 60; tick++) // 30 minutes of 30s ticks
        {
            var at = Now + Poll * tick;
            if (AlertEvaluationSchedule.IsDue(900, last, at, Poll))
            {
                evaluations++;
                last = at;
            }
        }

        Assert.Equal(2, evaluations);
    }

    [Fact]
    public void LastEvaluatedInFuture_ClockSkew_IsNotDue()
    {
        Assert.False(AlertEvaluationSchedule.IsDue(300, Now.AddSeconds(10), Now, Poll));
    }
}
