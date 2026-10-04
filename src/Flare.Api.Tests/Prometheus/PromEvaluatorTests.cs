using Flare.Api.Model;
using Flare.Api.Prometheus;
using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Prometheus;

public class PromEvaluatorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

    private static MetricSeriesPoint Point(int minute, double value) =>
        new() { BucketStart = T0.AddMinutes(minute), Value = value };

    private static PromSeries Series(string labels, params (long ts, double v)[] samples)
    {
        var s = new PromSeries(labels.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=')).ToDictionary(p => p[0], p => p[1]));
        s.Samples.AddRange(samples.Select(x => new PromSample(x.ts, x.v)));
        return s;
    }

    [Fact]
    public async Task Rate_SlidesWindowOverBuckets_AndDividesByWindowSeconds()
    {
        // Increases of 60 per minute bucket => 1/s. A 2m window spans two 1m buckets: (60+60)/120.
        var stub = new StubMetrics(
            [new MetricNameInfo { MetricName = "http.requests", ServiceName = "api", Type = MetricPointType.Sum, SeriesCount = 1 }],
            [new MetricSeries { ServiceName = "api", Attributes = new Dictionary<string, string> { ["http.route"] = "/a" }, Points = [Point(0, 60), Point(1, 60), Point(2, 120)] }]);
        var evaluator = new PromEvaluator(stub);

        var result = await evaluator.EvaluateAsync(
            PromQlParser.Parse("rate(http_requests_total[2m])"), T0, T0.AddMinutes(2), TimeSpan.FromMinutes(1), default);

        var series = Assert.Single(result.Series);
        Assert.Equal("api", series.Labels["service_name"]);
        Assert.Equal("/a", series.Labels["http_route"]);
        Assert.DoesNotContain("__name__", series.Labels.Keys);
        Assert.Equal([0.5, 1.0, 1.5], series.Samples.Select(s => s.Value));
        Assert.Equal(60, stub.LastQuery!.BucketWidthSeconds);
        Assert.Equal(MetricPointType.Sum, stub.LastQuery.Type);
    }

    [Fact]
    public async Task Increase_IsRateTimesRange()
    {
        var stub = new StubMetrics(
            [new MetricNameInfo { MetricName = "jobs", ServiceName = "api", Type = MetricPointType.Sum, SeriesCount = 1 }],
            [new MetricSeries { ServiceName = "api", Attributes = new Dictionary<string, string>(), Points = [Point(0, 30), Point(1, 30)] }]);

        var result = await new PromEvaluator(stub).EvaluateAsync(
            PromQlParser.Parse("increase(jobs_total[2m])"), T0, T0.AddMinutes(1), TimeSpan.FromMinutes(1), default);

        Assert.Equal(60, Assert.Single(result.Series).Samples[^1].Value);
    }

    [Fact]
    public async Task RateOnGauge_TreatsItAsCounter()
    {
        var stub = new StubMetrics(
            [new MetricNameInfo { MetricName = "legacy_total", ServiceName = "api", Type = MetricPointType.Gauge, SeriesCount = 1 }],
            [new MetricSeries { ServiceName = "api", Attributes = new Dictionary<string, string>(), Points = [Point(0, 60)] }]);

        await new PromEvaluator(stub).EvaluateAsync(
            PromQlParser.Parse("rate(legacy_total[1m])"), T0, T0, TimeSpan.FromMinutes(1), default);

        Assert.True(stub.LastQuery!.TreatAsCounter);
        Assert.Equal(MetricPointType.Gauge, stub.LastQuery.Type);
    }

    [Fact]
    public async Task BareCounterSelector_IsRejectedWithGuidance()
    {
        var stub = new StubMetrics(
            [new MetricNameInfo { MetricName = "hits", ServiceName = "api", Type = MetricPointType.Sum, SeriesCount = 1 }], []);

        var ex = await Assert.ThrowsAsync<PromQlException>(() => new PromEvaluator(stub).EvaluateAsync(
            PromQlParser.Parse("hits_total"), T0, T0, TimeSpan.FromMinutes(1), default));

        Assert.Contains("rate()", ex.Message);
    }

    [Fact]
    public async Task UnknownMetric_IsEmptyNotAnError()
    {
        var result = await new PromEvaluator(new StubMetrics([], [])).EvaluateAsync(
            PromQlParser.Parse("nope"), T0, T0, TimeSpan.FromMinutes(1), default);

        Assert.Empty(result.Series);
    }

    [Fact]
    public async Task GaugeSelector_PushesEqualityMatchersDown_AndPostFiltersTheRest()
    {
        var stub = new StubMetrics(
            [new MetricNameInfo { MetricName = "temp", ServiceName = "api", Type = MetricPointType.Gauge, SeriesCount = 2 }],
            [
                new MetricSeries { ServiceName = "api", Attributes = new Dictionary<string, string> { ["room"] = "a" }, Points = [Point(0, 1)] },
                new MetricSeries { ServiceName = "api", Attributes = new Dictionary<string, string> { ["room"] = "b" }, Points = [Point(0, 2)] },
            ],
            attributeKeys: ["room"]);

        var result = await new PromEvaluator(stub).EvaluateAsync(
            PromQlParser.Parse("""temp{room!="a",service_name="api"}"""), T0, T0, TimeSpan.FromMinutes(1), default);

        Assert.Equal("b", Assert.Single(result.Series).Labels["room"]);
        Assert.Equal(["api"], stub.LastQuery!.Filter.Services);
    }

    [Fact]
    public void Aggregate_SumBy_GroupsAndDropsOtherLabels()
    {
        var input = new List<PromSeries>
        {
            Series("job=a,pod=1", (0, 1), (60000, 2)),
            Series("job=a,pod=2", (0, 3), (60000, 4)),
            Series("job=b,pod=3", (0, 10)),
        };

        var result = PromEvaluator.Aggregate((PromAggregation)PromQlParser.Parse("sum by (job) (x)"), input);

        Assert.Equal(2, result.Count);
        var a = result.Single(s => s.Labels["job"] == "a");
        Assert.Equal([4.0, 6.0], a.Samples.Select(s => s.Value));
        Assert.Single(a.Labels);
    }

    [Theory]
    [InlineData("avg", 2.0)]
    [InlineData("min", 1.0)]
    [InlineData("max", 3.0)]
    [InlineData("count", 2.0)]
    public void Aggregate_Operators(string op, double expected)
    {
        var input = new List<PromSeries> { Series("pod=1", (0, 1)), Series("pod=2", (0, 3)) };

        var result = PromEvaluator.Aggregate((PromAggregation)PromQlParser.Parse($"{op}(x)"), input);

        Assert.Equal(expected, Assert.Single(Assert.Single(result).Samples).Value);
    }

    [Fact]
    public void Aggregate_Without_RemovesListedLabels()
    {
        var input = new List<PromSeries> { Series("job=a,pod=1", (0, 1)), Series("job=a,pod=2", (0, 1)) };

        var result = PromEvaluator.Aggregate((PromAggregation)PromQlParser.Parse("sum without (pod) (x)"), input);

        Assert.Equal("a", Assert.Single(result).Labels["job"]);
    }

    [Fact]
    public void Quantile_InterpolatesWithinTheRankBucket()
    {
        var buckets = new SortedDictionary<(double Lower, double Upper), double>
        {
            [(0, 1)] = 50,
            [(1, 2)] = 50,
        };

        Assert.Equal(1.0, PromEvaluator.Quantile(0.5, buckets));
        Assert.Equal(1.5, PromEvaluator.Quantile(0.75, buckets));
        Assert.Equal(double.PositiveInfinity, PromEvaluator.Quantile(1.5, buckets));
        Assert.Equal(double.NegativeInfinity, PromEvaluator.Quantile(-1, buckets));
        Assert.Null(PromEvaluator.Quantile(0.5, []));
    }

    [Fact]
    public async Task HistogramQuantile_MergesSeriesInAGroup()
    {
        MetricSeries Hist(string route, double[] counts) => new()
        {
            ServiceName = "api",
            Attributes = new Dictionary<string, string> { ["route"] = route },
            Points =
            [
                new MetricSeriesPoint
                {
                    BucketStart = T0,
                    BucketLowers = [0, 1],
                    BucketUppers = [1, 2],
                    BucketCounts = counts,
                },
            ],
        };
        var stub = new StubMetrics(
            [new MetricNameInfo { MetricName = "d", ServiceName = "api", Type = MetricPointType.Histogram, Unit = "s", SeriesCount = 2 }],
            [Hist("/a", [10, 0]), Hist("/b", [0, 10])]);

        var result = await new PromEvaluator(stub).EvaluateAsync(
            PromQlParser.Parse("histogram_quantile(0.5, sum by (le) (rate(d_seconds_bucket[1m])))"), T0, T0, TimeSpan.FromMinutes(1), default);

        // 20 observations, half in each bucket: the median is the boundary.
        Assert.Equal(1.0, Assert.Single(Assert.Single(result.Series).Samples).Value);
        Assert.True(stub.LastQuery!.IncludeBuckets);

        var byRoute = await new PromEvaluator(stub).EvaluateAsync(
            PromQlParser.Parse("histogram_quantile(0.5, sum by (le, route) (rate(d_seconds_bucket[1m])))"), T0, T0, TimeSpan.FromMinutes(1), default);
        Assert.Equal(2, byRoute.Series.Count);
    }

    private sealed class StubMetrics(
        IReadOnlyList<MetricNameInfo> names,
        IReadOnlyList<MetricSeries> series,
        string[]? attributeKeys = null) : IMetricQueryService
    {
        public MetricQueryRequest? LastQuery { get; private set; }

        public Task<MetricNamesResponse> GetNamesAsync(MetricNamesRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new MetricNamesResponse { Metrics = names });

        public Task<MetricQueryResponse> QueryAsync(MetricQueryRequest request, CancellationToken cancellationToken)
        {
            LastQuery = request;
            return Task.FromResult(new MetricQueryResponse { Series = series });
        }

        public Task<MetricAttributeKeysResponse> GetAttributeKeysAsync(MetricAttributeKeysRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new MetricAttributeKeysResponse
            {
                Keys = (attributeKeys ?? []).Select(k => new MetricAttributeKeyInfo { Key = k, DistinctValueCount = 1 }).ToList(),
            });
    }
}
