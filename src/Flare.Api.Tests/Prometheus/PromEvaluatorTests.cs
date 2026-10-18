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

    private static List<PromSeries> Bin(string query, List<PromSeries> left, List<PromSeries>? right = null, double? scalar = null)
    {
        var b = Assert.IsType<PromBinary>(PromQlParser.Parse(query));
        return scalar is { } s
            ? PromBinaryOperations.ApplyScalar(b, left, s, b.Left is PromNumber)
            : PromBinaryOperations.ApplyVectors(b, left, right!);
    }

    [Fact]
    public void Binary_Division_MatchesOnAllLabels_AndDropsTheMetricName()
    {
        var errors = new List<PromSeries> { Series("__name__=err,job=a", (0, 1), (60000, 2)), Series("__name__=err,job=b", (0, 5)) };
        var total = new List<PromSeries> { Series("__name__=req,job=a", (0, 10), (60000, 10)), Series("__name__=req,job=b", (0, 10)) };

        var result = Bin("e / t", errors, total);

        var a = result.Single(s => s.Labels["job"] == "a");
        Assert.Equal([0.1, 0.2], a.Samples.Select(x => x.Value));
        Assert.DoesNotContain("__name__", a.Labels.Keys);
        Assert.Equal(0.5, result.Single(s => s.Labels["job"] == "b").Samples.Single().Value);
    }

    [Fact]
    public void Binary_UnmatchedSeriesAndTimestamps_AreDropped()
    {
        var left = new List<PromSeries> { Series("job=a", (0, 1), (60000, 2)), Series("job=only-left", (0, 1)) };
        var right = new List<PromSeries> { Series("job=a", (60000, 4)), Series("job=only-right", (0, 1)) };

        var result = Bin("a * b", left, right);

        var s = Assert.Single(result);
        Assert.Equal(8, Assert.Single(s.Samples).Value);
        Assert.Equal(60000, s.Samples[0].TimestampMs);
    }

    [Fact]
    public void Binary_On_MatchesOnlyTheListedLabels_AndKeepsOnlyThose()
    {
        var left = new List<PromSeries> { Series("job=a,code=500", (0, 1)) };
        var right = new List<PromSeries> { Series("job=a,pod=p1", (0, 4)) };

        var result = Bin("a / on(job) b", left, right);

        var s = Assert.Single(result);
        Assert.Equal(0.25, s.Samples[0].Value);
        Assert.Equal(["job"], s.Labels.Keys);
    }

    [Fact]
    public void Binary_Ignoring_DropsTheListedLabelsFromMatchingAndResult()
    {
        var left = new List<PromSeries> { Series("job=a,code=500", (0, 1)) };
        var right = new List<PromSeries> { Series("job=a", (0, 4)) };

        var result = Bin("a / ignoring(code) b", left, right);

        Assert.Equal(["job"], Assert.Single(result).Labels.Keys);
    }

    [Fact]
    public void Binary_ManyToOne_WithoutGroupLeft_IsAnError()
    {
        var left = new List<PromSeries> { Series("job=a,code=500", (0, 1)), Series("job=a,code=404", (0, 2)) };
        var right = new List<PromSeries> { Series("job=a", (0, 10)) };

        var ex = Assert.Throws<PromQlException>(() => Bin("a / on(job) b", left, right));

        Assert.Contains("group_left", ex.Message);
    }

    [Fact]
    public void Binary_OneToManyOnTheOneSide_IsAnError()
    {
        var left = new List<PromSeries> { Series("job=a", (0, 10)) };
        var right = new List<PromSeries> { Series("job=a,code=500", (0, 1)), Series("job=a,code=404", (0, 2)) };

        Assert.Throws<PromQlException>(() => Bin("a / on(job) b", left, right));
    }

    [Fact]
    public void Binary_SeriesSharingASignatureAtDifferentInstants_DoNotClash()
    {
        var left = new List<PromSeries> { Series("pod=1", (0, 1)), Series("pod=2", (60000, 2)) };
        var right = new List<PromSeries> { Series("x=y", (0, 10), (60000, 10)) };

        var result = Bin("a * ignoring(pod, x) b", left, right);

        Assert.Equal([10.0, 20.0], Assert.Single(result).Samples.Select(s => s.Value));
    }

    [Fact]
    public void Binary_GroupLeft_KeepsLeftLabels_AndCopiesIncludedOnes()
    {
        var left = new List<PromSeries> { Series("job=a,code=500", (0, 1)), Series("job=a,code=404", (0, 3)) };
        var right = new List<PromSeries> { Series("job=a,team=core", (0, 10)) };

        var result = Bin("a * on(job) group_left(team) b", left, right);

        Assert.Equal(2, result.Count);
        var s500 = result.Single(s => s.Labels["code"] == "500");
        Assert.Equal("core", s500.Labels["team"]);
        Assert.Equal(10, s500.Samples[0].Value);
        Assert.Equal(30, result.Single(s => s.Labels["code"] == "404").Samples[0].Value);
    }

    [Fact]
    public void Binary_GroupRight_KeepsRightLabels_AndKeepsOperandOrder()
    {
        var left = new List<PromSeries> { Series("job=a", (0, 100)) };
        var right = new List<PromSeries> { Series("job=a,code=500", (0, 1)), Series("job=a,code=404", (0, 4)) };

        var result = Bin("a / on(job) group_right b", left, right);

        Assert.Equal(100, result.Single(s => s.Labels["code"] == "500").Samples[0].Value);
        Assert.Equal(25, result.Single(s => s.Labels["code"] == "404").Samples[0].Value);
    }

    [Fact]
    public void Binary_Division_ByZero_IsInfinityNotAnError()
    {
        var result = Bin("a / b", [Series("job=a", (0, 1))], [Series("job=a", (0, 0))]);

        Assert.Equal(double.PositiveInfinity, result[0].Samples[0].Value);
    }

    [Theory]
    [InlineData("a > 5", new[] { 10.0 })]
    [InlineData("a < 5", new[] { 1.0 })]
    [InlineData("a == 10", new[] { 10.0 })]
    [InlineData("a != 10", new[] { 1.0 })]
    [InlineData("a >= 10", new[] { 10.0 })]
    [InlineData("5 < a", new[] { 10.0 })]
    public void Binary_ScalarComparison_FiltersAndKeepsTheVectorValue(string query, double[] expected)
    {
        var vector = new List<PromSeries> { Series("__name__=m,job=a", (0, 1)), Series("__name__=m,job=b", (0, 10)) };
        var b = (PromBinary)PromQlParser.Parse(query);
        var scalar = b.Left is PromNumber l ? l.Value : ((PromNumber)b.Right).Value;

        var result = PromBinaryOperations.ApplyScalar(b, vector, scalar, b.Left is PromNumber);

        Assert.Equal(expected, result.SelectMany(s => s.Samples).Select(s => s.Value).Order());
        Assert.All(result, s => Assert.Equal("m", s.Labels["__name__"]));
    }

    [Fact]
    public void Binary_BoolComparison_Returns1Or0_AndDropsTheName()
    {
        var vector = new List<PromSeries> { Series("__name__=m,job=a", (0, 1), (60000, 10)) };
        var b = (PromBinary)PromQlParser.Parse("m > bool 5");

        var result = PromBinaryOperations.ApplyScalar(b, vector, 5, scalarOnLeft: false);

        Assert.Equal([0.0, 1.0], Assert.Single(result).Samples.Select(s => s.Value));
        Assert.DoesNotContain("__name__", result[0].Labels.Keys);
    }

    [Fact]
    public void Binary_VectorComparison_WithoutBool_KeepsLeftValueWhereTrue()
    {
        var result = Bin("a > b", [Series("job=a", (0, 5), (60000, 1))], [Series("job=a", (0, 3), (60000, 3))]);

        Assert.Equal(5, Assert.Single(Assert.Single(result).Samples).Value);
    }

    [Fact]
    public void Binary_ScalarArithmetic_ScalesEverySample()
    {
        var result = Bin("100 * a", [Series("job=a", (0, 0.5), (60000, 0.25))], scalar: 100);

        Assert.Equal([50.0, 25.0], Assert.Single(result).Samples.Select(s => s.Value));
    }

    [Fact]
    public void Binary_ScalarOnTheLeft_KeepsOperandOrder()
    {
        var result = Bin("1 - a", [Series("job=a", (0, 0.25))], scalar: 1);

        Assert.Equal(0.75, result[0].Samples[0].Value);
    }

    [Fact]
    public void Set_And_KeepsLeftSamplesWhereRightHasASample()
    {
        var left = new List<PromSeries> { Series("job=a", (0, 1), (60000, 2)), Series("job=b", (0, 3)) };
        var right = new List<PromSeries> { Series("job=a", (60000, 99)) };

        var result = Bin("a and b", left, right);

        var s = Assert.Single(result);
        Assert.Equal(2, Assert.Single(s.Samples).Value);
    }

    [Fact]
    public void Set_Unless_RemovesLeftSamplesWhereRightHasASample()
    {
        var left = new List<PromSeries> { Series("job=a", (0, 1), (60000, 2)), Series("job=b", (0, 3)) };
        var right = new List<PromSeries> { Series("job=a", (60000, 99)) };

        var result = Bin("a unless b", left, right);

        Assert.Equal(1, result.Single(s => s.Labels["job"] == "a").Samples.Single().Value);
        Assert.Equal(3, result.Single(s => s.Labels["job"] == "b").Samples.Single().Value);
    }

    [Fact]
    public void Set_Or_AddsRightSamplesWithNoLeftMatch()
    {
        var left = new List<PromSeries> { Series("job=a", (0, 1)) };
        var right = new List<PromSeries> { Series("job=a", (0, 99), (60000, 5)), Series("job=b", (0, 7)) };

        var result = Bin("a or b", left, right);

        Assert.Equal(1, result.Single(s => s.Labels["job"] == "a" && s.Samples[0].TimestampMs == 0).Samples[0].Value);
        Assert.Contains(result, s => s.Labels["job"] == "a" && s.Samples is [{ TimestampMs: 60000, Value: 5 }]);
        Assert.Contains(result, s => s.Labels["job"] == "b");
    }

    [Fact]
    public void Set_And_On_MatchesOnlyTheListedLabels()
    {
        var left = new List<PromSeries> { Series("job=a,code=500", (0, 1)) };
        var right = new List<PromSeries> { Series("job=a", (0, 1)) };

        Assert.Single(Bin("a and on(job) b", left, right));
        Assert.Empty(Bin("a and b", left, right));
    }

    [Fact]
    public void TopK_PicksThePerTimestampLeaders_AndKeepsOriginalLabels()
    {
        var input = new List<PromSeries>
        {
            Series("host=a", (0, 1), (60000, 9)),
            Series("host=b", (0, 5), (60000, 2)),
            Series("host=c", (0, 3), (60000, 3)),
        };

        var result = PromEvaluator.Aggregate((PromAggregation)PromQlParser.Parse("topk(1, x)"), input);

        // a leads at 60s, b leads at 0s; c never ranks first.
        Assert.Equal(2, result.Count);
        Assert.Equal(60000, result.Single(s => s.Labels["host"] == "a").Samples.Single().TimestampMs);
        Assert.Equal(0, result.Single(s => s.Labels["host"] == "b").Samples.Single().TimestampMs);
    }

    [Fact]
    public void BottomK_ByGroup_RanksWithinEachGroup()
    {
        var input = new List<PromSeries>
        {
            Series("job=x,host=a", (0, 1)),
            Series("job=x,host=b", (0, 5)),
            Series("job=y,host=c", (0, 9)),
        };

        var result = PromEvaluator.Aggregate((PromAggregation)PromQlParser.Parse("bottomk by (job) (1, x)"), input);

        Assert.Equal(["a", "c"], result.Select(s => s.Labels["host"]).Order());
    }

    [Fact]
    public void TopK_KLargerThanTheGroup_ReturnsEverySeries_AndZeroReturnsNone()
    {
        var input = new List<PromSeries> { Series("host=a", (0, 1)), Series("host=b", (0, 2)) };

        Assert.Equal(2, PromEvaluator.Aggregate((PromAggregation)PromQlParser.Parse("topk(10, x)"), input).Count);
        Assert.Empty(PromEvaluator.Aggregate((PromAggregation)PromQlParser.Parse("topk(0, x)"), input));
    }

    [Theory]
    [InlineData("quantile(0.5, x)", 2.5)]
    [InlineData("quantile(0, x)", 1.0)]
    [InlineData("quantile(1, x)", 4.0)]
    [InlineData("quantile(0.25, x)", 1.75)]
    [InlineData("group(x)", 1.0)]
    [InlineData("stdvar(x)", 1.25)]
    public void Aggregate_ParameterisedAndStatisticalOperators(string query, double expected)
    {
        var input = new List<PromSeries> { Series("p=1", (0, 1)), Series("p=2", (0, 2)), Series("p=3", (0, 3)), Series("p=4", (0, 4)) };

        var result = PromEvaluator.Aggregate((PromAggregation)PromQlParser.Parse(query), input);

        Assert.Equal(expected, Assert.Single(Assert.Single(result).Samples).Value, 9);
    }

    [Fact]
    public void Aggregate_Stddev_IsThePopulationStandardDeviation()
    {
        var input = new List<PromSeries> { Series("p=1", (0, 2)), Series("p=2", (0, 4)), Series("p=3", (0, 4)), Series("p=4", (0, 4)), Series("p=5", (0, 5)), Series("p=6", (0, 5)), Series("p=7", (0, 7)), Series("p=8", (0, 9)) };

        var result = PromEvaluator.Aggregate((PromAggregation)PromQlParser.Parse("stddev(x)"), input);

        Assert.Equal(2.0, result[0].Samples[0].Value, 9);
    }

    [Fact]
    public void Quantile_OutOfRange_IsInfinite()
    {
        Assert.Equal(double.PositiveInfinity, PromEvaluator.QuantileOf(1.5, [1, 2]));
        Assert.Equal(double.NegativeInfinity, PromEvaluator.QuantileOf(-0.5, [1, 2]));
    }

    [Theory]
    [InlineData("abs(x)", -2.5, 2.5)]
    [InlineData("ceil(x)", 1.2, 2.0)]
    [InlineData("floor(x)", 1.8, 1.0)]
    [InlineData("round(x)", 1.5, 2.0)]
    [InlineData("round(x, 0.5)", 1.3, 1.5)]
    [InlineData("round(x, 10)", 14, 10.0)]
    [InlineData("sqrt(x)", 9, 3.0)]
    [InlineData("exp(x)", 0, 1.0)]
    [InlineData("ln(x)", 1, 0.0)]
    [InlineData("log2(x)", 8, 3.0)]
    [InlineData("log10(x)", 1000, 3.0)]
    [InlineData("sgn(x)", -7, -1.0)]
    [InlineData("clamp(x, 0, 1)", 5, 1.0)]
    [InlineData("clamp(x, 0, 1)", -5, 0.0)]
    [InlineData("clamp_min(x, 3)", 1, 3.0)]
    [InlineData("clamp_max(x, 3)", 10, 3.0)]
    public void InstantFunctions_MapEverySample(string query, double input, double expected)
    {
        var fn = (PromFunction)PromQlParser.Parse(query);

        var result = PromFunctions.Apply(fn, [Series("__name__=m,job=a", (0, input))]);

        Assert.Equal(expected, result[0].Samples[0].Value, 9);
        Assert.DoesNotContain("__name__", result[0].Labels.Keys);
    }

    [Fact]
    public void Clamp_WithMinAboveMax_IsEmpty() =>
        Assert.Empty(PromFunctions.Apply((PromFunction)PromQlParser.Parse("clamp(x, 5, 1)"), [Series("a=b", (0, 1))]));

    [Fact]
    public async Task Vector_IsALabellessSeriesAtEveryStep()
    {
        var result = await new PromEvaluator(new StubMetrics([], [])).EvaluateAsync(
            PromQlParser.Parse("vector(3)"), T0, T0.AddMinutes(2), TimeSpan.FromMinutes(1), default);

        Assert.False(result.IsScalar);
        var series = Assert.Single(result.Series);
        Assert.Empty(series.Labels);
        Assert.Equal([3.0, 3.0, 3.0], series.Samples.Select(s => s.Value));
    }

    [Fact]
    public async Task Offset_FetchesAnEarlierWindow_AndRestampsTheSamples()
    {
        var stub = new StubMetrics(
            [new MetricNameInfo { MetricName = "temp", ServiceName = "api", Type = MetricPointType.Gauge, SeriesCount = 1 }],
            [new MetricSeries { ServiceName = "api", Attributes = new Dictionary<string, string>(), Points = [Point(-60, 7)] }]);

        var result = await new PromEvaluator(stub).EvaluateAsync(
            PromQlParser.Parse("temp offset 1h"), T0, T0, TimeSpan.FromMinutes(1), default);

        Assert.Equal(T0.AddHours(-1), stub.LastQuery!.Filter.From);
        // The hour-old value is reported at the evaluation instant, so it lines up with an un-offset series.
        var sample = Assert.Single(Assert.Single(result.Series).Samples);
        Assert.Equal(T0.ToUnixTimeMilliseconds(), sample.TimestampMs);
        Assert.Equal(7, sample.Value);
    }

    [Fact]
    public async Task OffsetRate_ShiftsTheRateWindowToo()
    {
        var stub = new StubMetrics(
            [new MetricNameInfo { MetricName = "jobs", ServiceName = "api", Type = MetricPointType.Sum, SeriesCount = 1 }],
            [new MetricSeries { ServiceName = "api", Attributes = new Dictionary<string, string>(), Points = [Point(-60, 120)] }]);

        var result = await new PromEvaluator(stub).EvaluateAsync(
            PromQlParser.Parse("rate(jobs_total[1m] offset 1h)"), T0, T0, TimeSpan.FromMinutes(1), default);

        Assert.Equal(T0.AddHours(-1).AddMinutes(-1), stub.LastQuery!.Filter.From);
        Assert.Equal(2.0, Assert.Single(Assert.Single(result.Series).Samples).Value);
    }

    [Fact]
    public async Task Irate_IsTheNewestBucketsIncreasePerSecond()
    {
        var stub = new StubMetrics(
            [new MetricNameInfo { MetricName = "jobs", ServiceName = "api", Type = MetricPointType.Sum, SeriesCount = 1 }],
            [new MetricSeries { ServiceName = "api", Attributes = new Dictionary<string, string>(), Points = [Point(0, 60), Point(1, 180)] }]);

        var result = await new PromEvaluator(stub).EvaluateAsync(
            PromQlParser.Parse("irate(jobs_total[2m])"), T0.AddMinutes(1), T0.AddMinutes(1), TimeSpan.FromMinutes(1), default);

        Assert.Equal(3.0, Assert.Single(Assert.Single(result.Series).Samples).Value);
    }

    [Fact]
    public async Task AvgOverTime_AndLastOverTime_ReduceTheGaugeWindow()
    {
        var stub = new StubMetrics(
            [new MetricNameInfo { MetricName = "temp", ServiceName = "api", Type = MetricPointType.Gauge, SeriesCount = 1 }],
            [new MetricSeries { ServiceName = "api", Attributes = new Dictionary<string, string>(), Points = [Point(0, 2), Point(1, 4), Point(2, 9)] }]);
        var evaluator = new PromEvaluator(stub);

        var avg = await evaluator.EvaluateAsync(PromQlParser.Parse("avg_over_time(temp[3m])"), T0.AddMinutes(2), T0.AddMinutes(2), TimeSpan.FromMinutes(1), default);
        var last = await evaluator.EvaluateAsync(PromQlParser.Parse("last_over_time(temp[3m])"), T0.AddMinutes(2), T0.AddMinutes(2), TimeSpan.FromMinutes(1), default);

        Assert.Equal(5.0, avg.Series[0].Samples[0].Value);
        Assert.Equal(9.0, last.Series[0].Samples[0].Value);
        Assert.False(stub.LastQuery!.TreatAsCounter ?? false);
    }

    [Theory]
    [InlineData("min_over_time", MetricGaugeAggregate.Min, 2.0)]
    [InlineData("max_over_time", MetricGaugeAggregate.Max, 9.0)]
    [InlineData("sum_over_time", MetricGaugeAggregate.Sum, 15.0)]
    [InlineData("count_over_time", MetricGaugeAggregate.Count, 15.0)]
    public async Task MinMaxSumCountOverTime_AskTheQueryForTheRealAggregate_AndCombineBuckets(string function, MetricGaugeAggregate aggregate, double expected)
    {
        // Each bucket value is already the per-bucket min/max/sum/count the SQL produced; the window folds them.
        var stub = new StubMetrics(
            [new MetricNameInfo { MetricName = "temp", ServiceName = "api", Type = MetricPointType.Gauge, SeriesCount = 1 }],
            [new MetricSeries { ServiceName = "api", Attributes = new Dictionary<string, string>(), Points = [Point(0, 2), Point(1, 4), Point(2, 9)] }]);

        var result = await new PromEvaluator(stub).EvaluateAsync(
            PromQlParser.Parse($"{function}(temp[3m])"), T0.AddMinutes(2), T0.AddMinutes(2), TimeSpan.FromMinutes(1), default);

        Assert.Equal(aggregate, stub.LastQuery!.GaugeAggregate);
        Assert.Equal(expected, Assert.Single(Assert.Single(result.Series).Samples).Value);
    }

    [Fact]
    public async Task AvgOverTime_DoesNotAskForAnAggregate()
    {
        var stub = new StubMetrics(
            [new MetricNameInfo { MetricName = "temp", ServiceName = "api", Type = MetricPointType.Gauge, SeriesCount = 1 }],
            [new MetricSeries { ServiceName = "api", Attributes = new Dictionary<string, string>(), Points = [Point(0, 2)] }]);

        await new PromEvaluator(stub).EvaluateAsync(
            PromQlParser.Parse("avg_over_time(temp[3m])"), T0, T0, TimeSpan.FromMinutes(1), default);

        Assert.Null(stub.LastQuery!.GaugeAggregate);
    }

    [Fact]
    public async Task AtModifier_RepeatsThePinnedValueAtEveryStep()
    {
        var stub = new StubMetrics(
            [new MetricNameInfo { MetricName = "temp", ServiceName = "api", Type = MetricPointType.Gauge, SeriesCount = 1 }],
            [new MetricSeries { ServiceName = "api", Attributes = new Dictionary<string, string>(), Points = [Point(0, 3), Point(1, 7)] }]);
        var at = T0.AddMinutes(1).AddSeconds(30);

        var result = await new PromEvaluator(stub).EvaluateAsync(
            PromQlParser.Parse($"temp @ {at.ToUnixTimeSeconds()}"), T0, T0.AddMinutes(3), TimeSpan.FromMinutes(1), default);

        var series = Assert.Single(result.Series);
        Assert.Equal([7.0, 7.0, 7.0, 7.0], series.Samples.Select(s => s.Value));
        Assert.Equal(
            Enumerable.Range(0, 4).Select(m => T0.AddMinutes(m).ToUnixTimeMilliseconds()),
            series.Samples.Select(s => s.TimestampMs));
        // One instant-style fetch at the pinned time (newest sample within the five-minute lookback), not the whole range.
        Assert.Equal(at.AddMinutes(-5), stub.LastQuery!.Filter.From);
        Assert.Equal(at.AddMilliseconds(1), stub.LastQuery.Filter.To);
    }

    [Fact]
    public async Task AtEnd_InAnInstantQuery_MeansTheEvaluationTime_NotTheLookbackWindowsEnd()
    {
        var stub = new StubMetrics(
            [new MetricNameInfo { MetricName = "temp", ServiceName = "api", Type = MetricPointType.Gauge, SeriesCount = 1 }],
            [new MetricSeries { ServiceName = "api", Attributes = new Dictionary<string, string>(), Points = [Point(5, 3)] }]);

        await new PromEvaluator(stub).EvaluateAsync(
            PromQlParser.Parse("temp @ start()"), T0, T0.AddMinutes(5), TimeSpan.FromMinutes(1), default, evaluationTime: T0.AddMinutes(5));

        // start() is the evaluation time, so the lookback ends at T0+5m; the window's own start (T0) would end it at T0.
        Assert.Equal(T0, stub.LastQuery!.Filter.From);
        Assert.Equal(T0.AddMinutes(5).AddMilliseconds(1), stub.LastQuery.Filter.To);
    }

    [Fact]
    public async Task AtModifier_WithOffset_CountsBackFromThePinnedTime()
    {
        var stub = new StubMetrics(
            [new MetricNameInfo { MetricName = "temp", ServiceName = "api", Type = MetricPointType.Gauge, SeriesCount = 1 }],
            [new MetricSeries { ServiceName = "api", Attributes = new Dictionary<string, string>(), Points = [Point(3, 1)] }]);

        await new PromEvaluator(stub).EvaluateAsync(
            PromQlParser.Parse($"temp @ {T0.AddMinutes(5).ToUnixTimeSeconds()} offset 2m"), T0, T0.AddMinutes(1), TimeSpan.FromMinutes(1), default);

        Assert.Equal(T0.AddMinutes(3).AddMilliseconds(1), stub.LastQuery!.Filter.To);
    }

    [Fact]
    public async Task Subquery_MaxOverTimeOfARate_TakesTheWindowsLargestStep()
    {
        // Increases of 60, 180, 60 per minute => 1, 3, 1 per second; the 3-minute window max at minute 2 is 3.
        var stub = new StubMetrics(
            [new MetricNameInfo { MetricName = "jobs", ServiceName = "api", Type = MetricPointType.Sum, SeriesCount = 1 }],
            [new MetricSeries { ServiceName = "api", Attributes = new Dictionary<string, string>(), Points = [Point(0, 60), Point(1, 180), Point(2, 60)] }]);

        var result = await new PromEvaluator(stub).EvaluateAsync(
            PromQlParser.Parse("max_over_time(rate(jobs_total[1m])[3m:1m])"), T0, T0.AddMinutes(2), TimeSpan.FromMinutes(1), default);

        var series = Assert.Single(result.Series);
        Assert.DoesNotContain("__name__", series.Labels.Keys);
        // Window (t-3m, t] over inner samples at minutes 0..2: max so far at each step.
        Assert.Equal([1.0, 3.0, 3.0], series.Samples.Select(s => s.Value));
        // The inner grid reaches back one range before the first step.
        Assert.Equal(T0.AddMinutes(-3 - 1), stub.LastQuery!.Filter.From);
    }

    [Fact]
    public async Task Subquery_CountAndAvg_ReduceTheInnerSamples()
    {
        var stub = new StubMetrics(
            [new MetricNameInfo { MetricName = "temp", ServiceName = "api", Type = MetricPointType.Gauge, SeriesCount = 1 }],
            [new MetricSeries { ServiceName = "api", Attributes = new Dictionary<string, string>(), Points = [Point(0, 2), Point(1, 4), Point(2, 9)] }]);
        var evaluator = new PromEvaluator(stub);

        var count = await evaluator.EvaluateAsync(
            PromQlParser.Parse("count_over_time(temp[2m:1m])"), T0.AddMinutes(2), T0.AddMinutes(2), TimeSpan.FromMinutes(1), default);
        var avg = await evaluator.EvaluateAsync(
            PromQlParser.Parse("avg_over_time((temp * 2)[3m:1m])"), T0.AddMinutes(2), T0.AddMinutes(2), TimeSpan.FromMinutes(1), default);

        // (t-2m, t] holds the samples at minutes 1 and 2; (t-3m, t] holds all three, doubled.
        Assert.Equal(2.0, Assert.Single(count.Series).Samples[0].Value);
        Assert.Equal(10.0, Assert.Single(avg.Series).Samples[0].Value);
    }

    [Fact]
    public async Task Subquery_WithOffset_ShiftsTheWindowBackAndRestampsToTheStep()
    {
        var stub = new StubMetrics(
            [new MetricNameInfo { MetricName = "temp", ServiceName = "api", Type = MetricPointType.Gauge, SeriesCount = 1 }],
            [new MetricSeries { ServiceName = "api", Attributes = new Dictionary<string, string>(), Points = [Point(0, 5), Point(1, 6)] }]);

        var result = await new PromEvaluator(stub).EvaluateAsync(
            PromQlParser.Parse("max_over_time(temp[2m:1m] offset 2m)"), T0.AddMinutes(3), T0.AddMinutes(3), TimeSpan.FromMinutes(1), default);

        var sample = Assert.Single(Assert.Single(result.Series).Samples);
        Assert.Equal(T0.AddMinutes(3).ToUnixTimeMilliseconds(), sample.TimestampMs);
        // (T0-1m, T0+1m] at the shifted time T0+1m: samples at T0 and T0+1m.
        Assert.Equal(6.0, sample.Value);
    }

    [Fact]
    public async Task Subquery_TooManyInnerPoints_IsRejected()
    {
        var stub = new StubMetrics(
            [new MetricNameInfo { MetricName = "temp", ServiceName = "api", Type = MetricPointType.Gauge, SeriesCount = 1 }], []);

        var ex = await Assert.ThrowsAsync<PromQlException>(() => new PromEvaluator(stub).EvaluateAsync(
            PromQlParser.Parse("max_over_time(temp[30d:1s])"), T0, T0, TimeSpan.FromMinutes(1), default));

        Assert.Contains("subquery", ex.Message);
    }

    [Fact]
    public async Task AvgOverTime_OnACounter_IsRejectedWithGuidance()
    {
        var stub = new StubMetrics(
            [new MetricNameInfo { MetricName = "hits", ServiceName = "api", Type = MetricPointType.Sum, SeriesCount = 1 }], []);

        var ex = await Assert.ThrowsAsync<PromQlException>(() => new PromEvaluator(stub).EvaluateAsync(
            PromQlParser.Parse("avg_over_time(hits_total[5m])"), T0, T0, TimeSpan.FromMinutes(1), default));

        Assert.Contains("gauges", ex.Message);
    }

    [Fact]
    public async Task ErrorRatio_EndToEnd_DividesTwoRates()
    {
        // Two metrics resolved by name: 5 failures out of 50 requests per minute.
        var stub = new MultiStubMetrics(
            [
                new MetricNameInfo { MetricName = "errors", ServiceName = "api", Type = MetricPointType.Sum, SeriesCount = 1 },
                new MetricNameInfo { MetricName = "requests", ServiceName = "api", Type = MetricPointType.Sum, SeriesCount = 1 },
            ],
            new Dictionary<string, MetricSeries[]>
            {
                ["errors"] = [new MetricSeries { ServiceName = "api", Attributes = new Dictionary<string, string>(), Points = [Point(0, 5)] }],
                ["requests"] = [new MetricSeries { ServiceName = "api", Attributes = new Dictionary<string, string>(), Points = [Point(0, 50)] }],
            });

        var result = await new PromEvaluator(stub).EvaluateAsync(
            PromQlParser.Parse("100 * sum(rate(errors_total[1m])) / sum(rate(requests_total[1m]))"), T0, T0, TimeSpan.FromMinutes(1), default);

        Assert.Equal(10.0, Assert.Single(Assert.Single(result.Series).Samples).Value, 9);
    }

    private sealed class MultiStubMetrics(IReadOnlyList<MetricNameInfo> names, Dictionary<string, MetricSeries[]> byMetric) : IMetricQueryService
    {
        public Task<MetricNamesResponse> GetNamesAsync(MetricNamesRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new MetricNamesResponse { Metrics = names });

        public Task<MetricQueryResponse> QueryAsync(MetricQueryRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new MetricQueryResponse { Series = byMetric[request.MetricName] });

        public Task<MetricAttributeKeysResponse> GetAttributeKeysAsync(MetricAttributeKeysRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new MetricAttributeKeysResponse { Keys = [] });
    }

    [Fact]
    public async Task Absent_IsOneWhereNoSeriesHasASample_AndLabelledFromEqualityMatchers()
    {
        var stub = new StubMetrics(
            [new MetricNameInfo { MetricName = "queue.depth", ServiceName = "api", Type = MetricPointType.Gauge, SeriesCount = 1 }],
            [new MetricSeries { ServiceName = "api", Attributes = new Dictionary<string, string>(), Points = [Point(0, 5), Point(2, 7)] }]);

        var result = await new PromEvaluator(stub).EvaluateAsync(
            PromQlParser.Parse("absent(queue_depth{service_name=\"api\"})"), T0, T0.AddMinutes(3), TimeSpan.FromMinutes(1), default);

        var series = Assert.Single(result.Series);
        Assert.Equal("api", series.Labels["service_name"]);
        Assert.Equal([T0.AddMinutes(1).ToUnixTimeMilliseconds(), T0.AddMinutes(3).ToUnixTimeMilliseconds()], series.Samples.Select(x => x.TimestampMs));
        Assert.All(series.Samples, x => Assert.Equal(1, x.Value));
    }

    [Fact]
    public async Task Absent_OfUnknownMetric_IsOneAtEveryStep()
    {
        var result = await new PromEvaluator(new StubMetrics([], [])).EvaluateAsync(
            PromQlParser.Parse("absent(nothing_here)"), T0, T0.AddMinutes(2), TimeSpan.FromMinutes(1), default);

        Assert.Equal(3, Assert.Single(result.Series).Samples.Count);
    }

    private sealed class StubExemplars(params MetricExemplarRow[] rows) : IMetricExemplarService
    {
        public MetricExemplarsRequest? LastRequest { get; private set; }

        public Task<IReadOnlyList<MetricExemplarRow>> GetAsync(MetricExemplarsRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult<IReadOnlyList<MetricExemplarRow>>(rows);
        }
    }

    private static MetricExemplarRow ExemplarRow(string route, string traceId) =>
        new("api", new Dictionary<string, string> { ["http.route"] = route }, T0, 1.5, traceId, "span1", new Dictionary<string, string> { ["user.id"] = "u" });

    [Fact]
    public async Task Exemplars_AreLabelledLikeTheirSeries_AndFilteredByMatchers()
    {
        var names = new StubMetrics(
            [new MetricNameInfo { MetricName = "http.server.request.duration", ServiceName = "api", Type = MetricPointType.Histogram, Unit = "s", SeriesCount = 2 }],
            [],
            ["http.route"]);
        var store = new StubExemplars(ExemplarRow("/a", "t1"), ExemplarRow("/b", "t2"));

        var found = await new PromEvaluator(names, store).ExemplarsAsync(
            (PromSelector)PromQlParser.Parse("http_server_request_duration_seconds_bucket{http_route=~\"/a\"}"), T0, T0.AddMinutes(5), 100, default);

        var exemplar = Assert.Single(found);
        Assert.Equal("t1", exemplar.Labels["trace_id"]);
        Assert.Equal("span1", exemplar.Labels["span_id"]);
        Assert.Equal("u", exemplar.Labels["user_id"]);
        Assert.Equal("http_server_request_duration_seconds_bucket", exemplar.SeriesLabels["__name__"]);
        Assert.Equal("/a", exemplar.SeriesLabels["http_route"]);
        Assert.Equal(MetricPointType.Histogram, store.LastRequest!.Type);
        Assert.Equal("http.server.request.duration", store.LastRequest.MetricName);
    }

    [Fact]
    public async Task Exemplars_AreEmpty_ForHistogramSumAndCount_AndWithoutAStore()
    {
        var names = new StubMetrics(
            [new MetricNameInfo { MetricName = "lat", ServiceName = "api", Type = MetricPointType.Histogram, SeriesCount = 1 }], []);
        var withStore = new PromEvaluator(names, new StubExemplars(ExemplarRow("/a", "t1")));

        Assert.Empty(await withStore.ExemplarsAsync((PromSelector)PromQlParser.Parse("lat_sum"), T0, T0.AddMinutes(1), 10, default));
        Assert.Empty(await new PromEvaluator(names).ExemplarsAsync((PromSelector)PromQlParser.Parse("lat_bucket"), T0, T0.AddMinutes(1), 10, default));
    }

    [Fact]
    public void SelectorCollector_FindsSelectorsInsideRangeFunctionsAggregationsAndOperators()
    {
        var selectors = PromSelectors.Collect(PromQlParser.Parse(
            "histogram_quantile(0.9, sum by (le) (rate(a_bucket[5m]))) / on() label_replace(b, \"x\", \"y\", \"z\", \".*\")"));

        Assert.Equal(["a_bucket", "b"], selectors.Select(s => s.MetricName));
    }

    private static StubMetrics GaugeStub(params MetricSeriesPoint[] points) =>
        new(
            [new MetricNameInfo { MetricName = "temp", ServiceName = "api", Type = MetricPointType.Gauge, SeriesCount = 1 }],
            [new MetricSeries { ServiceName = "api", Attributes = new Dictionary<string, string>(), Points = points }]);

    [Fact]
    public async Task Time_IsEachStepsTimestamp_AndExactForAnInstantQuery()
    {
        var evaluator = new PromEvaluator(GaugeStub());
        var range = await evaluator.EvaluateAsync(PromQlParser.Parse("time()"), T0, T0.AddMinutes(2), TimeSpan.FromMinutes(1), default);

        Assert.True(range.IsScalar);
        Assert.Equal(
            Enumerable.Range(0, 3).Select(m => (double)T0.AddMinutes(m).ToUnixTimeSeconds()),
            Assert.Single(range.Series).Samples.Select(s => s.Value));

        // 90s into a minute: the last step is stamped with its bucket start, but the instant is the evaluation time.
        var at = T0.AddSeconds(90);
        var instant = await evaluator.EvaluateAsync(PromQlParser.Parse("time()"), at.AddMinutes(-5), at, TimeSpan.FromMinutes(1), default, evaluationTime: at);
        Assert.Equal(at.ToUnixTimeSeconds(), Assert.Single(instant.Series).Samples[^1].Value);
    }

    [Fact]
    public async Task TimeArithmetic_IsAScalarEvaluatedPerStep()
    {
        var evaluator = new PromEvaluator(GaugeStub());

        var result = await evaluator.EvaluateAsync(PromQlParser.Parse("time() - 60"), T0, T0.AddMinutes(1), TimeSpan.FromMinutes(1), default);

        Assert.True(result.IsScalar);
        Assert.Equal([T0.ToUnixTimeSeconds() - 60.0, T0.ToUnixTimeSeconds()], Assert.Single(result.Series).Samples.Select(s => s.Value));
    }

    [Fact]
    public async Task VectorOfTime_IsASeriesWithNoLabels()
    {
        var result = await new PromEvaluator(GaugeStub()).EvaluateAsync(
            PromQlParser.Parse("vector(time())"), T0, T0, TimeSpan.FromMinutes(1), default);

        var series = Assert.Single(result.Series);
        Assert.Empty(series.Labels);
        Assert.False(result.IsScalar);
        Assert.Equal(T0.ToUnixTimeSeconds(), series.Samples[0].Value);
    }

    [Fact]
    public async Task VectorTimesTime_PairsEachStepWithItsOwnTimestamp()
    {
        // Uptime: time() minus a "started at" gauge, the usual use of time().
        var started = (double)T0.AddMinutes(-10).ToUnixTimeSeconds();
        var stub = GaugeStub(Point(0, started), Point(1, started));

        var result = await new PromEvaluator(stub).EvaluateAsync(
            PromQlParser.Parse("time() - temp"), T0, T0.AddMinutes(1), TimeSpan.FromMinutes(1), default);

        Assert.Equal([600.0, 660.0], Assert.Single(result.Series).Samples.Select(s => s.Value));
    }

    [Fact]
    public async Task Scalar_IsTheOnlySeriesValue_AndNaNWhenThereAreSeveralOrNone()
    {
        var stub = new StubMetrics(
            [new MetricNameInfo { MetricName = "temp", ServiceName = "api", Type = MetricPointType.Gauge, SeriesCount = 2 }],
            [
                new MetricSeries { ServiceName = "api", Attributes = new Dictionary<string, string> { ["host"] = "a" }, Points = [Point(0, 3), Point(1, 5)] },
                new MetricSeries { ServiceName = "api", Attributes = new Dictionary<string, string> { ["host"] = "b" }, Points = [Point(1, 7)] },
            ]);
        var evaluator = new PromEvaluator(stub);

        var result = await evaluator.EvaluateAsync(PromQlParser.Parse("scalar(temp)"), T0, T0.AddMinutes(2), TimeSpan.FromMinutes(1), default);

        // Minute 0: one series. Minute 1: two. Minute 2: none.
        var values = Assert.Single(result.Series).Samples.Select(s => s.Value).ToList();
        Assert.True(result.IsScalar);
        Assert.Equal(3, values[0]);
        Assert.True(double.IsNaN(values[1]));
        Assert.True(double.IsNaN(values[2]));
    }

    [Theory]
    [InlineData("delta", MetricGaugeAggregate.Delta)]
    [InlineData("changes", MetricGaugeAggregate.Changes)]
    [InlineData("resets", MetricGaugeAggregate.Resets)]
    public async Task DeltaChangesResets_AskTheQueryForTheStepAggregate_AndSumTheBuckets(string function, MetricGaugeAggregate aggregate)
    {
        // Each bucket value is already the SQL's per-bucket total; the window adds them.
        var stub = GaugeStub(Point(0, 3), Point(1, -2), Point(2, 5));

        var result = await new PromEvaluator(stub).EvaluateAsync(
            PromQlParser.Parse($"{function}(temp[3m])"), T0.AddMinutes(2), T0.AddMinutes(2), TimeSpan.FromMinutes(1), default);

        Assert.Equal(aggregate, stub.LastQuery!.GaugeAggregate);
        Assert.Equal(6.0, Assert.Single(Assert.Single(result.Series).Samples).Value);
    }

    [Fact]
    public async Task Deriv_IsTheSlopePerSecond_OverTheBucketAveragesAtTheirMiddles()
    {
        // 10, 20, 30 at 30s, 90s, 150s: +10 per minute.
        var stub = GaugeStub(Point(0, 10), Point(1, 20), Point(2, 30));

        var result = await new PromEvaluator(stub).EvaluateAsync(
            PromQlParser.Parse("deriv(temp[3m])"), T0.AddMinutes(2), T0.AddMinutes(3), TimeSpan.FromMinutes(1), default);

        Assert.Null(stub.LastQuery!.GaugeAggregate);
        Assert.Equal(10.0 / 60, Assert.Single(result.Series).Samples[0].Value, 9);
    }

    [Fact]
    public async Task PredictLinear_ExtendsTheFittedLineFromTheEndOfTheNewestBucket()
    {
        var stub = GaugeStub(Point(0, 10), Point(1, 20), Point(2, 30));

        var result = await new PromEvaluator(stub).EvaluateAsync(
            PromQlParser.Parse("predict_linear(temp[3m], 600)"), T0.AddMinutes(2), T0.AddMinutes(3), TimeSpan.FromMinutes(1), default);

        // The line is 10 + (x - 30s) / 6 and is read at T0+3m (180s) plus 600s: 35 + 100.
        Assert.Equal(135.0, Assert.Single(result.Series).Samples[0].Value, 9);
    }

    [Fact]
    public async Task Deriv_WithASingleBucket_HasNoSample()
    {
        var result = await new PromEvaluator(GaugeStub(Point(0, 10))).EvaluateAsync(
            PromQlParser.Parse("deriv(temp[1m])"), T0, T0.AddMinutes(1), TimeSpan.FromMinutes(1), default);

        Assert.Empty(result.Series);
    }

    [Theory]
    [InlineData("delta(temp[3m:1m])", 9.0)]
    [InlineData("changes(temp[3m:1m])", 2.0)]
    [InlineData("resets(temp[3m:1m])", 0.0)]
    public async Task DeltaChangesResets_OverASubquery_UseTheInnerSamples(string query, double expected)
    {
        var stub = GaugeStub(Point(0, 1), Point(1, 4), Point(2, 10));

        var result = await new PromEvaluator(stub).EvaluateAsync(
            PromQlParser.Parse(query), T0.AddMinutes(2), T0.AddMinutes(2), TimeSpan.FromMinutes(1), default);

        Assert.Equal(expected, Assert.Single(Assert.Single(result.Series).Samples).Value);
    }

    [Fact]
    public async Task DerivAndPredictLinear_OverASubquery_FitTheInnerSamples()
    {
        var stub = GaugeStub(Point(0, 0), Point(1, 60), Point(2, 120));
        var evaluator = new PromEvaluator(stub);

        var deriv = await evaluator.EvaluateAsync(PromQlParser.Parse("deriv(temp[3m:1m])"), T0.AddMinutes(2), T0.AddMinutes(2), TimeSpan.FromMinutes(1), default);
        var predicted = await evaluator.EvaluateAsync(PromQlParser.Parse("predict_linear(temp[3m:1m], 60)"), T0.AddMinutes(2), T0.AddMinutes(2), TimeSpan.FromMinutes(1), default);

        // 60 per minute, so 1 per second; the line reaches 120 at the window's end, 180 a minute later.
        Assert.Equal(1.0, Assert.Single(Assert.Single(deriv.Series).Samples).Value, 9);
        Assert.Equal(180.0, Assert.Single(Assert.Single(predicted.Series).Samples).Value, 9);
    }

    [Fact]
    public void Sort_OrdersByTheNewestValue_WithNaNLast()
    {
        var input = new List<PromSeries>
        {
            Series("h=a", (0, 5)),
            Series("h=b", (0, double.NaN)),
            Series("h=c", (0, 1)),
            Series("h=d", (0, 3)),
        };

        var ascending = PromFunctions.Sort((PromSort)PromQlParser.Parse("sort(x)"), input);
        var descending = PromFunctions.Sort((PromSort)PromQlParser.Parse("sort_desc(x)"), input);

        Assert.Equal(["c", "d", "a", "b"], ascending.Select(s => s.Labels["h"]));
        Assert.Equal(["a", "d", "c", "b"], descending.Select(s => s.Labels["h"]));
    }

    [Fact]
    public void SortByLabel_UsesNaturalOrder_AndTheLaterLabelsBreakTies()
    {
        var input = new List<PromSeries>
        {
            Series("node=node10,az=b", (0, 1)),
            Series("node=node2,az=b", (0, 1)),
            Series("node=node2,az=a", (0, 1)),
        };

        var ascending = PromFunctions.Sort((PromSort)PromQlParser.Parse("""sort_by_label(x, "node", "az")"""), input);
        var descending = PromFunctions.Sort((PromSort)PromQlParser.Parse("""sort_by_label_desc(x, "node")"""), input);

        Assert.Equal(["node2/a", "node2/b", "node10/b"], ascending.Select(s => $"{s.Labels["node"]}/{s.Labels["az"]}"));
        Assert.Equal("node10", descending[0].Labels["node"]);
    }

    [Fact]
    public void CountValues_CountsSeriesPerDistinctValue_AtEveryTimestamp()
    {
        var input = new List<PromSeries>
        {
            Series("host=a", (0, 3), (60000, 3)),
            Series("host=b", (0, 3), (60000, 4)),
            Series("host=c", (0, 4)),
        };

        var result = PromEvaluator.Aggregate((PromAggregation)PromQlParser.Parse("""count_values("version", x)"""), input);

        var three = Assert.Single(result, s => s.Labels["version"] == "3");
        var four = Assert.Single(result, s => s.Labels["version"] == "4");
        Assert.Empty(three.Labels.Keys.Where(k => k == "host"));
        Assert.Equal([(0L, 2.0), (60000L, 1.0)], three.Samples.Select(s => (s.TimestampMs, s.Value)));
        Assert.Equal([(0L, 1.0), (60000L, 1.0)], four.Samples.Select(s => (s.TimestampMs, s.Value)));
    }

    [Fact]
    public void CountValues_WithBy_KeepsTheGroupLabels()
    {
        var input = new List<PromSeries>
        {
            Series("job=x,host=a", (0, 1)),
            Series("job=x,host=b", (0, 1)),
            Series("job=y,host=c", (0, 1)),
        };

        var result = PromEvaluator.Aggregate((PromAggregation)PromQlParser.Parse("""count_values by (job) ("v", x)"""), input);

        Assert.Equal(2.0, Assert.Single(result, s => s.Labels["job"] == "x").Samples[0].Value);
        Assert.Equal(1.0, Assert.Single(result, s => s.Labels["job"] == "y").Samples[0].Value);
        Assert.All(result, s => Assert.Equal("1", s.Labels["v"]));
    }

    [Fact]
    public async Task Idelta_IsTheNewestPointsStep_FromTheLastDeltaAggregate()
    {
        var stub = GaugeStub(Point(0, 3), Point(1, -2), Point(2, 5));

        var result = await new PromEvaluator(stub).EvaluateAsync(
            PromQlParser.Parse("idelta(temp[3m])"), T0.AddMinutes(2), T0.AddMinutes(2), TimeSpan.FromMinutes(1), default);

        Assert.Equal(MetricGaugeAggregate.LastDelta, stub.LastQuery!.GaugeAggregate);
        Assert.Equal(5.0, Assert.Single(Assert.Single(result.Series).Samples).Value);
    }

    [Fact]
    public async Task Idelta_OverASubquery_IsTheLastTwoInnerSamples()
    {
        var result = await new PromEvaluator(GaugeStub(Point(0, 1), Point(1, 4), Point(2, 10))).EvaluateAsync(
            PromQlParser.Parse("idelta(temp[3m:1m])"), T0.AddMinutes(2), T0.AddMinutes(2), TimeSpan.FromMinutes(1), default);

        Assert.Equal(6.0, Assert.Single(Assert.Single(result.Series).Samples).Value);
    }

    [Fact]
    public void HoltWinters_FollowsPrometheusDoubleExponentialSmoothing()
    {
        // Worked by hand with sf = tf = 0.5: levels 3, 3.5, 4.875.
        Assert.Equal(4.875, PromWindowMath.HoltWinters([1, 3, 2, 5], 0.5, 0.5));
        Assert.Null(PromWindowMath.HoltWinters([1], 0.5, 0.5));
    }

    [Theory]
    [InlineData("holt_winters(temp[3m], 0.5, 0.5)")]
    [InlineData("double_exponential_smoothing(temp[3m], 0.5, 0.5)")]
    public async Task HoltWinters_SmoothsTheBucketAverages(string query)
    {
        var result = await new PromEvaluator(GaugeStub(Point(0, 1), Point(1, 3), Point(2, 2))).EvaluateAsync(
            PromQlParser.Parse(query), T0.AddMinutes(2), T0.AddMinutes(2), TimeSpan.FromMinutes(1), default);

        Assert.Equal(3.5, Assert.Single(Assert.Single(result.Series).Samples).Value);
    }

    [Fact]
    public async Task Timestamp_OfASelector_IsTheNewestPointsTime()
    {
        var stub = GaugeStub(Point(0, 7));

        var result = await new PromEvaluator(stub).EvaluateAsync(
            PromQlParser.Parse("timestamp(temp)"), T0, T0, TimeSpan.FromMinutes(1), default);

        Assert.Equal(MetricGaugeAggregate.LastTime, stub.LastQuery!.GaugeAggregate);
        var series = Assert.Single(result.Series);
        Assert.DoesNotContain("__name__", series.Labels.Keys);
        // The stub returns the value the SQL would: seconds.
        Assert.Equal(7.0, Assert.Single(series.Samples).Value);
    }

    [Fact]
    public async Task Timestamp_OfAnythingElse_IsTheStep()
    {
        var stub = GaugeStub(Point(0, 7), Point(1, 9));

        var result = await new PromEvaluator(stub).EvaluateAsync(
            PromQlParser.Parse("timestamp(temp * 2)"), T0, T0.AddMinutes(1), TimeSpan.FromMinutes(1), default);

        Assert.Equal(
            [(double)T0.ToUnixTimeSeconds(), T0.AddMinutes(1).ToUnixTimeSeconds()],
            Assert.Single(result.Series).Samples.Select(s => s.Value));
        Assert.Null(stub.LastQuery!.GaugeAggregate);
    }

    [Fact]
    public void HistogramFraction_InterpolatesInsideABucket()
    {
        var buckets = new SortedDictionary<(double Lower, double Upper), double>
        {
            [(0, 1)] = 2,
            [(1, 2)] = 2,
            [(2, 4)] = 4,
        };

        Assert.Equal(0.25, PromWindowMath.HistogramFraction(1, 2, buckets));
        // below(3) = 2 + 2 + 4 * 1/2 = 6; below(0.5) = 1.
        Assert.Equal(5.0 / 8, PromWindowMath.HistogramFraction(0.5, 3, buckets));
        Assert.Equal(1.0, PromWindowMath.HistogramFraction(double.NegativeInfinity, double.PositiveInfinity, buckets));
        Assert.Equal(0.0, PromWindowMath.HistogramFraction(3, 3, buckets));
        Assert.Null(PromWindowMath.HistogramFraction(0, 1, []));
    }

    [Fact]
    public async Task HistogramFraction_IsTheShareUnderABound_OfTheRateBuckets()
    {
        var stub = new StubMetrics(
            [new MetricNameInfo { MetricName = "d", ServiceName = "api", Type = MetricPointType.Histogram, Unit = "s", SeriesCount = 1 }],
            [new MetricSeries
            {
                ServiceName = "api",
                Attributes = new Dictionary<string, string>(),
                Points = [new MetricSeriesPoint { BucketStart = T0, BucketLowers = [0, 1], BucketUppers = [1, 2], BucketCounts = [30, 10] }],
            }]);

        var result = await new PromEvaluator(stub).EvaluateAsync(
            PromQlParser.Parse("histogram_fraction(0, 1, sum by (le) (rate(d_seconds_bucket[1m])))"), T0, T0, TimeSpan.FromMinutes(1), default);

        Assert.Equal(0.75, Assert.Single(Assert.Single(result.Series).Samples).Value);
    }

    private static StubMetrics NamedStub() =>
        new(
            [
                new MetricNameInfo { MetricName = "temp.a", ServiceName = "api", Type = MetricPointType.Gauge, SeriesCount = 1 },
                new MetricNameInfo { MetricName = "temp.b", ServiceName = "api", Type = MetricPointType.Gauge, SeriesCount = 1 },
                new MetricNameInfo { MetricName = "temp.jobs", ServiceName = "api", Type = MetricPointType.Sum, SeriesCount = 1 },
                new MetricNameInfo { MetricName = "other", ServiceName = "api", Type = MetricPointType.Gauge, SeriesCount = 1 },
            ],
            [new MetricSeries { ServiceName = "api", Attributes = new Dictionary<string, string>(), Points = [Point(0, 4)] }]);

    [Fact]
    public async Task NamePattern_EvaluatesEveryMatchingMetric_AndSkipsTheOnesItCannotRead()
    {
        var result = await new PromEvaluator(NamedStub()).EvaluateAsync(
            PromQlParser.Parse("""{__name__=~"temp_.*"}"""), T0, T0, TimeSpan.FromMinutes(1), default);

        Assert.Equal(["temp_a", "temp_b"], result.Series.Select(s => s.Labels["__name__"]).Order());
        Assert.Contains(result.Warnings, w => w.Contains("skipped 'temp_jobs_total'") && w.Contains("rate()"));
    }

    [Fact]
    public async Task NamePattern_WithANegativeMatcher_NarrowsTheMatches()
    {
        var result = await new PromEvaluator(NamedStub()).EvaluateAsync(
            PromQlParser.Parse("""{__name__=~"temp_.*", __name__!~".*_b|.*_total"}"""), T0, T0, TimeSpan.FromMinutes(1), default);

        Assert.Equal("temp_a", Assert.Single(result.Series).Labels["__name__"]);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task NamePattern_UnderRate_ReadsTheCounters_AndSkipsTheGauges()
    {
        var stub = new StubMetrics(
            [
                new MetricNameInfo { MetricName = "jobs.ok", ServiceName = "api", Type = MetricPointType.Sum, SeriesCount = 1 },
                new MetricNameInfo { MetricName = "jobs.failed", ServiceName = "api", Type = MetricPointType.Sum, SeriesCount = 1 },
            ],
            [new MetricSeries { ServiceName = "api", Attributes = new Dictionary<string, string>(), Points = [Point(0, 60)] }]);

        var result = await new PromEvaluator(stub).EvaluateAsync(
            PromQlParser.Parse("""sum(rate({__name__=~"jobs_.*"}[1m]))"""), T0, T0, TimeSpan.FromMinutes(1), default);

        // Two counters of 1/s each.
        Assert.Equal(2.0, Assert.Single(Assert.Single(result.Series).Samples).Value);
    }

    [Fact]
    public async Task NamePattern_MatchingNothing_IsEmpty_AndWhenEveryMatchFailsTheFirstErrorIsRaised()
    {
        var evaluator = new PromEvaluator(NamedStub());

        var none = await evaluator.EvaluateAsync(PromQlParser.Parse("""{__name__=~"zzz.*"}"""), T0, T0, TimeSpan.FromMinutes(1), default);
        var error = await Assert.ThrowsAsync<PromQlException>(() => evaluator.EvaluateAsync(
            PromQlParser.Parse("""{__name__=~".*_total"}"""), T0, T0, TimeSpan.FromMinutes(1), default));

        Assert.Empty(none.Series);
        Assert.Contains("rate()", error.Message);
    }

    [Fact]
    public async Task NamePattern_OnTheSeriesEndpoint_ListsEveryMatchingMetricsSeries()
    {
        var series = await new PromEvaluator(NamedStub()).SelectSeriesAsync(
            (PromSelector)PromQlParser.Parse("""{__name__=~"temp_.*"}"""), T0, T0.AddMinutes(5), [], default);

        Assert.Equal(["temp_a", "temp_b", "temp_jobs_total"], series.Select(s => s.Labels["__name__"]).Order());
    }

    [Fact]
    public async Task NamePattern_InHistogramQuantile_ExplainsItNeedsOneName()
    {
        var error = await Assert.ThrowsAsync<PromQlException>(() => new PromEvaluator(NamedStub()).EvaluateAsync(
            PromQlParser.Parse("""histogram_quantile(0.5, rate({__name__=~"d_.*"}[1m]))"""), T0, T0, TimeSpan.FromMinutes(1), default));

        Assert.Contains("single metric name", error.Message);
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
