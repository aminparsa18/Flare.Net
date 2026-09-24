using Flare.Ingest.Otlp;
using Xunit;

namespace Flare.Ingest.Tests;

public class ClockSkewTests
{
    private static readonly DateTimeOffset IngestedAt = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Nanos_IsPositive_ForAnEventInThePast()
    {
        Assert.Equal(1_500_000_000L, ClockSkew.Nanos(IngestedAt, IngestedAt.AddSeconds(-1.5)));
    }

    [Fact]
    public void Nanos_IsNegative_ForAnEventInTheFuture()
    {
        Assert.Equal(-2_000_000_000L, ClockSkew.Nanos(IngestedAt, IngestedAt.AddSeconds(2)));
    }

    [Fact]
    public void Nanos_IsClampedToAnHour_EitherWay()
    {
        var maxNanos = ClockSkew.MaxMagnitude.Ticks * 100;

        Assert.Equal(maxNanos, ClockSkew.Nanos(IngestedAt, DateTimeOffset.UnixEpoch));
        Assert.Equal(-maxNanos, ClockSkew.Nanos(IngestedAt, IngestedAt.AddYears(200)));
    }

    [Fact]
    public void Nanos_SummedOverAMinuteOfBogusTimestamps_StaysInLongRange()
    {
        // The regression: five 1970-stamped records used to overflow Redis's HINCRBY.
        long sum = 0;
        for (var i = 0; i < 1_000_000; i++)
        {
            sum = checked(sum + ClockSkew.Nanos(IngestedAt, DateTimeOffset.UnixEpoch));
        }

        Assert.True(sum > 0);
    }
}
