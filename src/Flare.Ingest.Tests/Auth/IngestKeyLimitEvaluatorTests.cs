using Flare.Identity.IngestKeys;
using Flare.Ingest.Auth;
using Xunit;

namespace Flare.Ingest.Tests.Auth;

public class IngestKeyLimitEvaluatorTests
{
    // 2026-09-24T10:15:20Z - 40s left in the minute, 13h44m40s left in the UTC day.
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 10, 15, 20, TimeSpan.Zero);

    private static IngestKeyUsage Usage(long minuteEvents = 0, long minuteBytes = 0, long dayEvents = 0, long dayBytes = 0) =>
        new(new IngestKeyWindowUsage(minuteEvents, minuteBytes), new IngestKeyWindowUsage(dayEvents, dayBytes));

    [Fact]
    public void Evaluate_ReturnsNull_WhenLimitsAreNotEnforced()
    {
        Assert.Null(IngestKeyLimitEvaluator.Evaluate(IngestApiKeyLimits.None, Usage(minuteEvents: long.MaxValue), Now));
        Assert.Null(IngestKeyLimitEvaluator.Evaluate(new IngestApiKeyLimits(false, 1, 1, 1, 1), Usage(minuteEvents: 5), Now));
    }

    [Fact]
    public void Evaluate_ReturnsNull_WhileUsageIsBelowEveryCap()
    {
        var limits = new IngestApiKeyLimits(true, 100, 1_000, 10_000, 100_000);

        Assert.Null(IngestKeyLimitEvaluator.Evaluate(limits, Usage(99, 999, 9_999, 99_999), Now));
    }

    [Fact]
    public void Evaluate_RetriesAtTheNextMinute_WhenAPerMinuteCapIsReached()
    {
        var limits = new IngestApiKeyLimits(true, MaxEventsPerMinute: 100, null, null, null);

        Assert.Equal(TimeSpan.FromSeconds(40), IngestKeyLimitEvaluator.Evaluate(limits, Usage(minuteEvents: 100), Now));
    }

    [Fact]
    public void Evaluate_ChecksBytesIndependentlyOfEvents()
    {
        var limits = new IngestApiKeyLimits(true, null, MaxBytesPerMinute: 1_000, null, null);

        Assert.Equal(TimeSpan.FromSeconds(40), IngestKeyLimitEvaluator.Evaluate(limits, Usage(minuteEvents: 1, minuteBytes: 1_500), Now));
    }

    [Fact]
    public void Evaluate_RetriesAtUtcMidnight_WhenADailyCapIsReached_EvenIfAMinuteCapIsToo()
    {
        var limits = new IngestApiKeyLimits(true, MaxEventsPerMinute: 10, null, MaxEventsPerDay: 1_000, null);

        var retryAfter = IngestKeyLimitEvaluator.Evaluate(limits, Usage(minuteEvents: 10, dayEvents: 1_000), Now);

        Assert.Equal(new TimeSpan(13, 44, 40), retryAfter);
    }

    [Fact]
    public void Evaluate_IgnoresUnsetCaps()
    {
        var limits = new IngestApiKeyLimits(true, null, null, null, MaxBytesPerDay: 1_000_000);

        Assert.Null(IngestKeyLimitEvaluator.Evaluate(limits, Usage(minuteEvents: long.MaxValue, dayEvents: long.MaxValue), Now));
    }

    private static IngestKeyUsage ReplayUsage(long minuteReplay = 0, long dayReplay = 0) =>
        new(new IngestKeyWindowUsage(0, minuteReplay, minuteReplay), new IngestKeyWindowUsage(0, dayReplay, dayReplay));

    [Fact]
    public void Evaluate_AppliesReplayCaps_OnlyToAReplayRequest()
    {
        var limits = new IngestApiKeyLimits(true, null, null, null, null, MaxReplayBytesPerMinute: 1_000);
        var usage = ReplayUsage(minuteReplay: 1_000);

        Assert.Equal(TimeSpan.FromSeconds(40), IngestKeyLimitEvaluator.Evaluate(limits, usage, Now, isReplay: true));
        Assert.Null(IngestKeyLimitEvaluator.Evaluate(limits, usage, Now, isReplay: false));
    }

    [Fact]
    public void Evaluate_RetriesAtUtcMidnight_WhenTheDailyReplayCapIsReached()
    {
        var limits = new IngestApiKeyLimits(true, null, null, null, null, MaxReplayBytesPerDay: 5_000);

        Assert.Equal(new TimeSpan(13, 44, 40), IngestKeyLimitEvaluator.Evaluate(limits, ReplayUsage(dayReplay: 5_000), Now, isReplay: true));
    }

    [Fact]
    public void Evaluate_StillAppliesTheSharedByteCaps_ToAReplayRequest()
    {
        var limits = new IngestApiKeyLimits(true, null, MaxBytesPerMinute: 1_000, null, null, MaxReplayBytesPerMinute: 10_000);

        Assert.Equal(TimeSpan.FromSeconds(40), IngestKeyLimitEvaluator.Evaluate(limits, ReplayUsage(minuteReplay: 1_000), Now, isReplay: true));
    }

    [Fact]
    public void IsEnforced_IsTrue_WhenOnlyAReplayCapIsSet()
    {
        Assert.True(new IngestApiKeyLimits(true, null, null, null, null, MaxReplayBytesPerDay: 1).IsEnforced);
    }
}
