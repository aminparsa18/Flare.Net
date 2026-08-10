using System.Text.Json;
using Flare.Ingest.Model;
using Flare.Ingest.Pipeline;
using Xunit;

namespace Flare.Ingest.Tests.Pipeline;

/// <summary>
/// Round-trip tests for the wire format <see cref="Sinks.RedisStreamMetricEventSink"/>
/// and <see cref="MetricFlushWorker"/> share, same role as <see cref="SpanEventJsonContextTests"/>.
/// Also the one place that actually exercises <see cref="MetricPointRecord"/>'s
/// polymorphic serialization (<c>JsonPolymorphic</c>/<c>JsonDerivedType</c>) - the
/// mechanism the "one shared stream for all three point types" pipeline decision
/// (Planning.md's v6) depends on.
/// </summary>
public class MetricEventJsonContextTests
{
    [Fact]
    public void RoundTrips_GaugePointRecord_AsItsConcreteType()
    {
        MetricPointRecord original = new GaugePointRecord
        {
            MetricName = "process.threads",
            Description = "Number of OS threads",
            Unit = "1",
            ServiceName = "payments-api",
            ResourceSchemaUrl = "https://opentelemetry.io/schemas/1.0.0",
            ResourceAttributes = new Dictionary<string, string> { ["service.name"] = "payments-api" },
            ScopeSchemaUrl = "https://opentelemetry.io/schemas/1.0.0",
            ScopeName = "manual-test",
            ScopeVersion = "1.0",
            ScopeAttributes = new Dictionary<string, string> { ["scope.key"] = "scope-value" },
            DataPointAttributes = new Dictionary<string, string> { ["state"] = "active" },
            StartTime = new DateTimeOffset(2026, 8, 10, 11, 0, 0, TimeSpan.Zero),
            Time = new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero),
            Value = 42,
        };

        var roundTripped = AssertRoundTrips(original);

        var gauge = Assert.IsType<GaugePointRecord>(roundTripped);
        Assert.Equal(42, gauge.Value);
    }

    [Fact]
    public void RoundTrips_SumPointRecord_AsItsConcreteType()
    {
        MetricPointRecord original = new SumPointRecord
        {
            MetricName = "http.server.request.count",
            ResourceAttributes = new Dictionary<string, string>(),
            ScopeAttributes = new Dictionary<string, string>(),
            DataPointAttributes = new Dictionary<string, string>(),
            Time = DateTimeOffset.UnixEpoch,
            Value = 7,
            AggregationTemporality = 2,
            IsMonotonic = true,
        };

        var roundTripped = AssertRoundTrips(original);

        var sum = Assert.IsType<SumPointRecord>(roundTripped);
        Assert.Equal(7, sum.Value);
        Assert.Equal(2, sum.AggregationTemporality);
        Assert.True(sum.IsMonotonic);
    }

    [Fact]
    public void RoundTrips_HistogramPointRecord_AsItsConcreteType()
    {
        MetricPointRecord original = new HistogramPointRecord
        {
            MetricName = "http.server.request.duration",
            ResourceAttributes = new Dictionary<string, string>(),
            ScopeAttributes = new Dictionary<string, string>(),
            DataPointAttributes = new Dictionary<string, string>(),
            Time = DateTimeOffset.UnixEpoch,
            AggregationTemporality = 2,
            Count = 3,
            Sum = 12.5,
            BucketCounts = [1UL, 2UL, 0UL],
            ExplicitBounds = [10.0, 50.0],
        };

        var roundTripped = AssertRoundTrips(original);

        var histogram = Assert.IsType<HistogramPointRecord>(roundTripped);
        Assert.Equal(3UL, histogram.Count);
        Assert.Equal(12.5, histogram.Sum);
        Assert.Equal([1UL, 2UL, 0UL], histogram.BucketCounts);
        Assert.Equal([10.0, 50.0], histogram.ExplicitBounds);
    }

    [Fact]
    public void RoundTrips_HistogramPointRecord_WithNullSum()
    {
        MetricPointRecord original = new HistogramPointRecord
        {
            MetricName = "http.server.request.duration",
            ResourceAttributes = new Dictionary<string, string>(),
            ScopeAttributes = new Dictionary<string, string>(),
            DataPointAttributes = new Dictionary<string, string>(),
            Time = DateTimeOffset.UnixEpoch,
            AggregationTemporality = 0,
            Count = 0,
            Sum = null,
            BucketCounts = [],
            ExplicitBounds = [],
        };

        var roundTripped = AssertRoundTrips(original);

        Assert.Null(Assert.IsType<HistogramPointRecord>(roundTripped).Sum);
    }

    private static MetricPointRecord AssertRoundTrips(MetricPointRecord original)
    {
        var json = JsonSerializer.Serialize(original, MetricEventJsonContext.Default.MetricPointRecord);
        var roundTripped = JsonSerializer.Deserialize(json, MetricEventJsonContext.Default.MetricPointRecord);

        Assert.NotNull(roundTripped);
        Assert.Equal(original.GetType(), roundTripped.GetType());
        Assert.Equal(original.MetricName, roundTripped.MetricName);
        Assert.Equal(original.Description, roundTripped.Description);
        Assert.Equal(original.Unit, roundTripped.Unit);
        Assert.Equal(original.ServiceName, roundTripped.ServiceName);
        Assert.Equal(original.ResourceSchemaUrl, roundTripped.ResourceSchemaUrl);
        Assert.Equal(original.ResourceAttributes, roundTripped.ResourceAttributes);
        Assert.Equal(original.ScopeSchemaUrl, roundTripped.ScopeSchemaUrl);
        Assert.Equal(original.ScopeName, roundTripped.ScopeName);
        Assert.Equal(original.ScopeVersion, roundTripped.ScopeVersion);
        Assert.Equal(original.ScopeAttributes, roundTripped.ScopeAttributes);
        Assert.Equal(original.DataPointAttributes, roundTripped.DataPointAttributes);
        Assert.Equal(original.StartTime, roundTripped.StartTime);
        Assert.Equal(original.Time, roundTripped.Time);
        return roundTripped;
    }
}
