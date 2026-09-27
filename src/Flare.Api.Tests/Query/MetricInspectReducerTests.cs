using Flare.Api.Model;
using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Query;

public class MetricInspectReducerTests
{
    private const long Minute = 60_000;

    private static readonly IReadOnlyDictionary<string, string> NoAttributes = new Dictionary<string, string>();

    private static MetricInspectRawSeries Series(string service, params MetricInspectRawSample[] samples) =>
        new(service, NoAttributes, samples, SamplesTruncated: false);

    private static MetricInspectRawSample Gauge(long timeMs, double value) => new(timeMs, value, IsDelta: false, IsMonotonic: false);

    private static MetricInspectRawSample Cumulative(long timeMs, double value, bool monotonic = true) => new(timeMs, value, IsDelta: false, IsMonotonic: monotonic);

    private static MetricInspectRawSample Delta(long timeMs, double value) => new(timeMs, value, IsDelta: true, IsMonotonic: true);

    [Fact]
    public void Gauge_BucketIsTheAverageOfItsSamples()
    {
        var result = MetricInspectReducer.Reduce(
            MetricPointType.Gauge,
            [Series("api", Gauge(0, 10), Gauge(30_000, 20), Gauge(Minute, 5))],
            bucketWidthSeconds: 60);

        var series = Assert.Single(result.Series);
        Assert.All(series.Samples, s => Assert.Equal(MetricInspectSampleKind.Level, s.Kind));
        Assert.Equal([15.0, 5.0], series.Buckets.Select(b => b.Value));
        Assert.Equal([2, 1], series.Buckets.Select(b => b.SampleCount));
    }

    [Fact]
    public void Gauge_MergeIsTheAverageOfAllSamples_NotTheMeanOfSeriesAverages()
    {
        // Series a: 2 samples averaging 10; series b: 1 sample of 40. Mean of averages would be
        // 25; avg(Value) over the wider group is (5 + 15 + 40) / 3 = 20.
        var result = MetricInspectReducer.Reduce(
            MetricPointType.Gauge,
            [Series("a", Gauge(0, 5), Gauge(10_000, 15)), Series("b", Gauge(20_000, 40))],
            bucketWidthSeconds: 60);

        var merged = Assert.Single(result.Merged);
        Assert.Equal(20.0, merged.Value);
        Assert.Equal(3, merged.SampleCount);
    }

    [Fact]
    public void CumulativeSum_FirstSampleContributesZero_ThenDifferences()
    {
        var result = MetricInspectReducer.Reduce(
            MetricPointType.Sum,
            [Series("api", Cumulative(0, 100), Cumulative(30_000, 110), Cumulative(Minute, 130))],
            bucketWidthSeconds: 60);

        var samples = Assert.Single(result.Series).Samples;
        Assert.Equal([MetricInspectSampleKind.First, MetricInspectSampleKind.Difference, MetricInspectSampleKind.Difference], samples.Select(s => s.Kind));
        Assert.Equal([0.0, 10.0, 20.0], samples.Select(s => s.Contribution));
        Assert.Equal([10.0, 20.0], result.Series[0].Buckets.Select(b => b.Value));
    }

    [Fact]
    public void MonotonicCumulative_DropIsAReset_ValueStandsInForTheIncrease()
    {
        var result = MetricInspectReducer.Reduce(
            MetricPointType.Sum,
            [Series("api", Cumulative(0, 500), Cumulative(10_000, 3))],
            bucketWidthSeconds: 60);

        var reset = Assert.Single(result.Series).Samples[1];
        Assert.Equal(MetricInspectSampleKind.Reset, reset.Kind);
        Assert.Equal(3.0, reset.Contribution);
    }

    [Fact]
    public void NonMonotonicCumulative_DropIsANegativeDifference()
    {
        var result = MetricInspectReducer.Reduce(
            MetricPointType.Sum,
            [Series("api", Cumulative(0, 10, monotonic: false), Cumulative(10_000, 4, monotonic: false))],
            bucketWidthSeconds: 60);

        var sample = Assert.Single(result.Series).Samples[1];
        Assert.Equal(MetricInspectSampleKind.Difference, sample.Kind);
        Assert.Equal(-6.0, sample.Contribution);
    }

    [Fact]
    public void DeltaSamples_ContributeTheirOwnValue_IncludingTheFirst()
    {
        var result = MetricInspectReducer.Reduce(
            MetricPointType.Histogram,
            [Series("api", Delta(0, 4), Delta(10_000, 6))],
            bucketWidthSeconds: 60);

        var series = Assert.Single(result.Series);
        Assert.All(series.Samples, s => Assert.Equal(MetricInspectSampleKind.Delta, s.Kind));
        Assert.Equal(10.0, Assert.Single(series.Buckets).Value);
    }

    [Fact]
    public void Sum_MergeAddsTheSeriesBuckets_AndKeepsBucketsOnlyOneSeriesHas()
    {
        var result = MetricInspectReducer.Reduce(
            MetricPointType.Sum,
            [
                Series("a", Delta(0, 1), Delta(Minute, 2)),
                Series("b", Delta(10_000, 5)),
            ],
            bucketWidthSeconds: 60);

        Assert.Equal([0L, Minute], result.Merged.Select(b => b.BucketStartUnixMs));
        Assert.Equal([6.0, 2.0], result.Merged.Select(b => b.Value));
    }

    [Fact]
    public void Buckets_AreEpochAligned_LikeToStartOfInterval()
    {
        // 12:00:45 with 60s buckets lands in the 12:00:00 bucket.
        var t = new DateTimeOffset(2026, 9, 27, 12, 0, 45, TimeSpan.Zero).ToUnixTimeMilliseconds();

        var result = MetricInspectReducer.Reduce(MetricPointType.Gauge, [Series("api", Gauge(t, 1))], bucketWidthSeconds: 60);

        Assert.Equal(t - 45_000, Assert.Single(result.Merged).BucketStartUnixMs);
    }

    [Fact]
    public void Reduce_PassesSeriesIdentityAndTruncationThrough()
    {
        var attributes = new Dictionary<string, string> { ["route"] = "/checkout" };
        var raw = new MetricInspectRawSeries("api", attributes, [Gauge(0, 1)], SamplesTruncated: true);

        var series = Assert.Single(MetricInspectReducer.Reduce(MetricPointType.Gauge, [raw], 60).Series);

        Assert.Equal("api", series.ServiceName);
        Assert.Same(attributes, series.Attributes);
        Assert.True(series.SamplesTruncated);
    }

    [Fact]
    public void Reduce_WithNoSeries_ReturnsEmpty()
    {
        var result = MetricInspectReducer.Reduce(MetricPointType.Sum, [], 60);

        Assert.Empty(result.Series);
        Assert.Empty(result.Merged);
    }
}
