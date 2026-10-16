using Flare.Api.Loki;
using Xunit;

namespace Flare.Api.Tests.Loki;

public class LokiEvaluatorTests
{
    private const long Minute = 60_000;

    private sealed class FakeSource(params LokiBucketRow[] rows) : ILokiLeafSource
    {
        public List<LokiLeafRequest> Requests { get; } = [];

        public List<LokiUnwrapRow> UnwrapRows { get; init; } = [];

        public List<LokiQuantileRow> QuantileRows { get; init; } = [];

        public List<LokiUnwrapRequest> UnwrapRequests { get; } = [];

        public List<LokiQuantileRequest> QuantileRequests { get; } = [];

        public Task<IReadOnlyList<LokiUnwrapRow>> FetchUnwrapBucketsAsync(LokiUnwrapRequest request, CancellationToken cancellationToken)
        {
            UnwrapRequests.Add(request);
            return Task.FromResult<IReadOnlyList<LokiUnwrapRow>>(UnwrapRows.Where(r => r.BucketMs >= request.FromMs && r.BucketMs < request.ToMs).ToList());
        }

        public Task<IReadOnlyList<LokiQuantileRow>> FetchQuantilesAsync(LokiQuantileRequest request, CancellationToken cancellationToken)
        {
            QuantileRequests.Add(request);
            return Task.FromResult<IReadOnlyList<LokiQuantileRow>>(QuantileRows);
        }

        public IReadOnlyList<string> Streams { get; init; } = ["service_name"];

        public Task<IReadOnlyList<string>> StreamLabelNamesAsync(CancellationToken cancellationToken) => Task.FromResult(Streams);

        public Task<IReadOnlyList<LokiBucketRow>> FetchBucketsAsync(LokiLeafRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            // Return only the rows the request asked for, like ClickHouse would.
            return Task.FromResult<IReadOnlyList<LokiBucketRow>>(rows.Where(r => r.BucketMs >= request.FromMs && r.BucketMs < request.ToMs).ToList());
        }
    }

    private static LokiBucketRow Row(long bucketMs, double value, params string[] dims) => new(bucketMs, dims, value);

    private static Task<LokiMatrix> Run(ILokiLeafSource source, string query, long start, long end, long step, bool instant = false) =>
        new LokiEvaluator(source).EvaluateAsync(LokiQlParser.Parse(query), start, end, step, instant, CancellationToken.None);

    [Fact]
    public async Task CountOverTime_WindowEqualToStep_IsOneBucketPerStep()
    {
        var source = new FakeSource(Row(0, 3, "api"), Row(Minute, 5, "api"), Row(2 * Minute, 7, "api"));

        var result = await Run(source, """count_over_time({service_name="api"}[1m])""", Minute, 3 * Minute, Minute);

        var series = Assert.Single(result.Series);
        Assert.Equal("api", series.Labels["service_name"]);
        // The point at t counts the bucket that ended at t.
        Assert.Equal([3d, 5d, 7d], series.Values.Select(v => v!.Value));
        Assert.Equal(Minute, Assert.Single(source.Requests).BucketMs);
    }

    [Fact]
    public async Task CountOverTime_LongerWindowSlides()
    {
        var source = new FakeSource(Row(0, 1, "api"), Row(Minute, 2, "api"), Row(2 * Minute, 4, "api"));

        var result = await Run(source, """count_over_time({service_name="api"}[2m])""", 2 * Minute, 3 * Minute, Minute);

        // Window (t-2m, t]: at 2m buckets 0 and 1m (1+2); at 3m buckets 1m and 2m (2+4).
        Assert.Equal([3d, 6d], Assert.Single(result.Series).Values.Select(v => v!.Value));
    }

    [Fact]
    public async Task Rate_DividesByTheWindowSeconds()
    {
        var source = new FakeSource(Row(0, 120, "api"));

        var result = await Run(source, """rate({service_name="api"}[1m])""", Minute, Minute, Minute);

        Assert.Equal(2d, Assert.Single(result.Series).Values[0]);
    }

    [Fact]
    public async Task StepsWithoutData_HaveNoSample()
    {
        var source = new FakeSource(Row(Minute, 4, "api"));

        var result = await Run(source, """count_over_time({service_name="api"}[1m])""", Minute, 3 * Minute, Minute);

        Assert.Equal([null, 4d, null], Assert.Single(result.Series).Values);
    }

    [Fact]
    public async Task BucketWidth_IsTheGcdOfWindowAndStep()
    {
        var source = new FakeSource();

        await Run(source, """count_over_time({service_name="api"}[1m])""", 10 * Minute, 20 * Minute, 15_000);

        Assert.Equal(15_000, Assert.Single(source.Requests).BucketMs);
        Assert.Equal(15_000, LokiEvaluator.ChooseBucketMs(60_000, 15_000, 0, 600_000));
        Assert.Equal(60_000, LokiEvaluator.ChooseBucketMs(300_000, 60_000, 0, 600_000));
        // Too many buckets for the span: fall back to the step.
        Assert.Equal(60_000, LokiEvaluator.ChooseBucketMs(60_000 * 60 * 24 * 7 + 1000, 60_000, 0, 60_000L * 60 * 24 * 30));
    }

    [Fact]
    public async Task SumBy_NarrowsTheLeafToTheGroupingLabels()
    {
        var source = new FakeSource(Row(0, 1, "info"), Row(0, 2, "error"), Row(Minute, 3, "error"));

        var result = await Run(source, """sum by (detected_level) (count_over_time({service_name="api"}[1m]))""", Minute, 2 * Minute, Minute);

        Assert.Equal(["detected_level"], Assert.Single(source.Requests).Dimensions);
        var error = result.Series.Single(s => s.Labels["detected_level"] == "error");
        Assert.Equal([2d, 3d], error.Values.Select(v => v!.Value));
    }

    [Fact]
    public async Task Sum_CombinesSeries_AndDropsLabels()
    {
        var source = new FakeSource(Row(0, 1, "a"), Row(0, 2, "b"), Row(0, 4, "c"))
        {
            Streams = ["service_name"],
        };

        var result = await Run(source, """sum(count_over_time({service_name=~".+"}[1m]))""", Minute, Minute, Minute);

        var series = Assert.Single(result.Series);
        Assert.Empty(series.Labels);
        Assert.Equal(7d, series.Values[0]);
        Assert.Empty(Assert.Single(source.Requests).Dimensions);
    }

    [Fact]
    public async Task BareRangeAggregation_UsesTheStreamLabels()
    {
        var source = new FakeSource(Row(0, 1, "a", "ns1")) { Streams = ["service_name", "k8s_namespace_name"] };

        var result = await Run(source, """count_over_time({service_name=~".+"}[1m])""", Minute, Minute, Minute);

        var labels = Assert.Single(result.Series).Labels;
        Assert.Equal("ns1", labels["k8s_namespace_name"]);
    }

    [Fact]
    public async Task EmptyDimensionValues_AreDroppedFromLabels()
    {
        var source = new FakeSource(Row(0, 1, "a", "")) { Streams = ["service_name", "k8s_pod_name"] };

        var result = await Run(source, """count_over_time({service_name=~".+"}[1m])""", Minute, Minute, Minute);

        Assert.Equal(["service_name"], Assert.Single(result.Series).Labels.Keys);
    }

    [Fact]
    public async Task AggregationFunctions()
    {
        var rows = new[] { Row(0, 2, "a"), Row(0, 6, "b") };

        async Task<double> Value(string function) =>
            (await Run(new FakeSource(rows), $$"""{{function}}(count_over_time({service_name=~".+"}[1m]))""", Minute, Minute, Minute)).Series.Single().Values[0]!.Value;

        Assert.Equal(8d, await Value("sum"));
        Assert.Equal(4d, await Value("avg"));
        Assert.Equal(2d, await Value("min"));
        Assert.Equal(6d, await Value("max"));
        Assert.Equal(2d, await Value("count"));
    }

    [Fact]
    public async Task TopK_KeepsTheLargestSeries()
    {
        var source = new FakeSource(Row(0, 1, "a"), Row(0, 9, "b"), Row(0, 5, "c"));

        var result = await Run(source, """topk(2, count_over_time({service_name=~".+"}[1m]))""", Minute, Minute, Minute);

        Assert.Equal(["b", "c"], result.Series.Select(s => s.Labels["service_name"]).Order());
    }

    [Fact]
    public async Task BottomK_KeepsTheSmallestSeries()
    {
        var source = new FakeSource(Row(0, 1, "a"), Row(0, 9, "b"), Row(0, 5, "c"));

        var result = await Run(source, """bottomk(1, count_over_time({service_name=~".+"}[1m]))""", Minute, Minute, Minute);

        Assert.Equal("a", Assert.Single(result.Series).Labels["service_name"]);
    }

    [Fact]
    public async Task ErrorRatio_DividesMatchingSeries()
    {
        var source = new FakeSource(Row(0, 30, "a"));

        // Both sides read the same fake rows, so the ratio of a series with itself is 1.
        var result = await Run(source, """sum by (service_name) (rate({service_name=~".+"}[1m])) / sum by (service_name) (rate({service_name=~".+"}[1m]))""", Minute, Minute, Minute);

        Assert.Equal(1d, Assert.Single(result.Series).Values[0]);
    }

    [Fact]
    public async Task VectorLiterals_AddUp_AndScalarsStayScalar()
    {
        var health = await Run(new FakeSource(), "vector(1)+vector(1)", Minute, Minute, Minute, instant: true);
        var scalar = await Run(new FakeSource(), "1+2*3", Minute, Minute, Minute, instant: true);

        Assert.Equal(2d, Assert.Single(health.Series).Values[0]);
        Assert.False(health.IsScalar);
        Assert.Equal(7d, Assert.Single(scalar.Series).Values[0]);
        Assert.True(scalar.IsScalar);
    }

    [Fact]
    public async Task Division_ByZero_IsInfinity()
    {
        var result = await Run(new FakeSource(), "vector(1) / 0", Minute, Minute, Minute, instant: true);

        Assert.True(double.IsPositiveInfinity(Assert.Single(result.Series).Values[0]!.Value));
    }

    [Fact]
    public async Task VectorVectorOperators_DropUnmatchedSeries()
    {
        var source = new FakeSource(Row(0, 4, "a"), Row(0, 6, "b"));

        var result = await Run(source, """sum by (service_name) (count_over_time({service_name=~".+"}[1m])) * 2""", Minute, Minute, Minute);

        Assert.Equal([8d, 12d], result.Series.Select(s => s.Values[0]!.Value).Order());
    }

    [Fact]
    public async Task InstantQuery_ReadsOneBucketOverTheWindow()
    {
        var source = new FakeSource(Row(7 * Minute - 5 * Minute, 50, "api"));

        var result = await Run(source, """rate({service_name="api"}[5m])""", 7 * Minute, 7 * Minute, 1000, instant: true);

        var request = Assert.Single(source.Requests);
        Assert.Equal((2 * Minute, 7 * Minute, 0L), (request.FromMs, request.ToMs, request.BucketMs));
        Assert.Equal(50d / 300, Assert.Single(result.Series).Values[0]!.Value, 6);
    }

    [Fact]
    public async Task TooManySeries_IsAnError()
    {
        var rows = Enumerable.Range(0, LokiEvaluator.MaxSeries + 1).Select(i => Row(0, 1, $"svc{i}")).ToArray();

        var ex = await Assert.ThrowsAsync<LokiException>(() => Run(new FakeSource(rows), """count_over_time({service_name=~".+"}[1m])""", Minute, Minute, Minute));

        Assert.Contains("maximum of series", ex.Message);
    }

    [Fact]
    public async Task TooManyPoints_IsAnError()
    {
        var ex = await Assert.ThrowsAsync<LokiException>(() => Run(new FakeSource(), "vector(1)", 0, 1_000_000 * 1000L, 1000));

        Assert.Contains("maximum resolution", ex.Message);
    }

    [Fact]
    public async Task LogQuery_CannotBeEvaluatedAsAMetric() =>
        await Assert.ThrowsAsync<LokiException>(() => Run(new FakeSource(), """{service_name="api"}""", Minute, Minute, Minute));

    // ---- unwrapped range functions (ADR-0237) ----

    /// <summary>Statistics of the given samples, as ClickHouse would aggregate them for one bucket.</summary>
    private static LokiBucketStats Stats(params double[] values)
    {
        var mean = values.Average();
        return new LokiBucketStats(
            values.Length, values.Sum(), values.Min(), values.Max(), values[0], values[^1], mean, values.Sum(v => (v - mean) * (v - mean)));
    }

    private static LokiUnwrapRow Unwrapped(long bucketMs, LokiBucketStats stats, params string[] dims) => new(bucketMs, dims, stats);

    /// <summary>Two one-minute buckets, [1, 3] then [5], in a two-minute window ending at 2m.</summary>
    private static FakeSource TwoBuckets() => new()
    {
        UnwrapRows = [Unwrapped(0, Stats(1, 3), "api"), Unwrapped(Minute, Stats(5), "api")],
    };

    [Theory]
    [InlineData("sum_over_time", 9d)]
    [InlineData("avg_over_time", 3d)]
    [InlineData("min_over_time", 1d)]
    [InlineData("max_over_time", 5d)]
    [InlineData("first_over_time", 1d)]
    [InlineData("last_over_time", 5d)]
    [InlineData("stdvar_over_time", 8d / 3)]
    public async Task Unwrapped_FunctionsMergeTheBucketsOfTheWindow(string function, double expected)
    {
        var source = TwoBuckets();

        var result = await Run(source, $$"""{{function}}({service_name="api"} | json | unwrap latency [2m])""", 2 * Minute, 2 * Minute, Minute);

        Assert.Equal(expected, Assert.Single(result.Series).Values[0]!.Value, precision: 9);
        Assert.Equal("api", Assert.Single(result.Series).Labels["service_name"]);
    }

    [Fact]
    public async Task Unwrapped_StddevIsTheSquareRootOfTheVariance()
    {
        var result = await Run(TwoBuckets(), """stddev_over_time({service_name="api"} | json | unwrap latency [2m])""", 2 * Minute, 2 * Minute, Minute);

        Assert.Equal(Math.Sqrt(8d / 3), Assert.Single(result.Series).Values[0]!.Value, precision: 9);
    }

    [Fact]
    public async Task Unwrapped_SlidingWindowDropsOldBuckets()
    {
        var source = TwoBuckets();

        var result = await Run(source, """max_over_time({service_name="api"} | json | unwrap latency [1m])""", Minute, 2 * Minute, Minute);

        // Window [t-1m, t): the first bucket is [1, 3] (max 3), the second [5].
        Assert.Equal([3d, 5d], Assert.Single(result.Series).Values.Select(v => v!.Value));
    }

    [Fact]
    public async Task Unwrapped_StepsWithoutSamplesHaveNoValue()
    {
        var source = new FakeSource { UnwrapRows = [Unwrapped(Minute, Stats(4), "api")] };

        var result = await Run(source, """avg_over_time({service_name="api"} | json | unwrap latency [1m])""", Minute, 3 * Minute, Minute);

        Assert.Equal([null, 4d, null], Assert.Single(result.Series).Values);
    }

    [Fact]
    public async Task Unwrapped_OwnGroupingBecomesTheLeafDimensions()
    {
        var source = TwoBuckets();

        await Run(source, """avg_over_time({service_name="api"} | json | unwrap latency [2m]) by (region)""", 2 * Minute, 2 * Minute, Minute);

        // The samples are grouped by region before the function runs, so the read groups by region alone.
        Assert.Equal(["region"], Assert.Single(source.UnwrapRequests).Dimensions);
    }

    [Fact]
    public async Task Unwrapped_WithoutGroupingDropsTheNamedLabelsFromTheStream()
    {
        var source = TwoBuckets();
        var withStreams = new FakeSource { Streams = ["service_name", "k8s_pod_name"], UnwrapRows = source.UnwrapRows };

        await Run(withStreams, """avg_over_time({service_name="api"} | json | unwrap latency [2m]) without (k8s_pod_name)""", 2 * Minute, 2 * Minute, Minute);

        Assert.Equal(["service_name"], Assert.Single(withStreams.UnwrapRequests).Dimensions);
    }

    [Fact]
    public async Task Unwrapped_SumByOverSumOverTimeIsPushedDown_ButOtherFunctionsAreNot()
    {
        var additive = TwoBuckets();
        await Run(additive, """sum by (region) (sum_over_time({service_name="api"} | json | unwrap latency [2m]))""", 2 * Minute, 2 * Minute, Minute);
        Assert.Equal(["region"], Assert.Single(additive.UnwrapRequests).Dimensions);

        var average = TwoBuckets();
        await Run(average, """sum by (region) (avg_over_time({service_name="api"} | json | unwrap latency [2m]))""", 2 * Minute, 2 * Minute, Minute);
        // An average of averages is not the average, so whole streams are read and grouped afterwards.
        Assert.Equal(["service_name", "region"], Assert.Single(average.UnwrapRequests).Dimensions);
    }

    [Fact]
    public async Task Unwrapped_VectorAggregationsFoldTheFunctionsResult()
    {
        var source = new FakeSource
        {
            Streams = ["service_name"],
            UnwrapRows = [Unwrapped(0, Stats(10), "api"), Unwrapped(0, Stats(30), "web")],
        };

        var result = await Run(source, """max(avg_over_time({service_name=~".+"} | json | unwrap latency [1m]))""", Minute, Minute, Minute);

        Assert.Equal(30d, Assert.Single(result.Series).Values[0]);
    }

    [Fact]
    public async Task Unwrapped_InstantQueryReadsOneWindow()
    {
        var source = new FakeSource { UnwrapRows = [Unwrapped(5 * Minute - 2 * Minute, Stats(2, 4, 6), "api")] };

        var result = await Run(source, """avg_over_time({service_name="api"} | json | unwrap latency [2m])""", 5 * Minute, 5 * Minute, Minute, instant: true);

        var request = Assert.Single(source.UnwrapRequests);
        Assert.Equal(0, request.BucketMs);
        Assert.Equal((3 * Minute, 5 * Minute), (request.FromMs, request.ToMs));
        Assert.Equal(4d, Assert.Single(result.Series).Values[0]);
    }

    [Fact]
    public async Task Quantile_AsksClickHouseForOneValuePerStep()
    {
        var source = new FakeSource
        {
            QuantileRows = [new LokiQuantileRow(0, ["api"], 120), new LokiQuantileRow(2, ["api"], 340), new LokiQuantileRow(1, ["web"], 7)],
        };

        var result = await Run(source, """quantile_over_time(0.95, {service_name=~".+"} | json | unwrap latency [5m])""", 10 * Minute, 12 * Minute, Minute);

        var request = Assert.Single(source.QuantileRequests);
        Assert.Equal((0.95, 10 * Minute, Minute, 3, 5 * Minute), (request.Quantile, request.FirstStepMs, request.StepMs, request.StepCount, request.RangeMs));
        var api = result.Series.Single(s => s.Labels["service_name"] == "api");
        Assert.Equal([120d, null, 340d], api.Values);
        var web = result.Series.Single(s => s.Labels["service_name"] == "web");
        Assert.Equal([null, 7d, null], web.Values);
    }

    [Fact]
    public async Task Quantile_InstantQueryIsOneWindowAsWideAsTheRange()
    {
        var source = new FakeSource { QuantileRows = [new LokiQuantileRow(0, ["api"], 5)] };

        await Run(source, """quantile_over_time(0.5, {service_name="api"} | json | unwrap latency [5m])""", 10 * Minute, 10 * Minute, Minute, instant: true);

        var request = Assert.Single(source.QuantileRequests);
        Assert.Equal((10 * Minute, 5 * Minute, 1), (request.FirstStepMs, request.StepMs, request.StepCount));
    }

    [Fact]
    public async Task Quantile_RefusesAWindowThatIsTooManyStepsWide()
    {
        var ex = await Assert.ThrowsAsync<LokiException>(() =>
            Run(new FakeSource(), """quantile_over_time(0.5, {service_name="api"} | json | unwrap latency [1h])""", 2 * 60 * Minute, 3 * 60 * Minute, 15_000));

        Assert.Contains("increase the step", ex.Message);
    }

    [Fact]
    public async Task Unwrapped_RefusesAnEvaluationThatWouldMergeTooManyBuckets()
    {
        var ex = await Assert.ThrowsAsync<LokiException>(() =>
            Run(new FakeSource(), """avg_over_time({service_name="api"} | json | unwrap latency [6h])""", 24 * 60 * Minute, 24 * 60 * Minute + 10_000_000, 1_000));

        Assert.Contains("too many buckets", ex.Message);
    }

    [Fact]
    public async Task BytesFunctions_RefuseALineRewrittenByLineFormat()
    {
        var ex = await Assert.ThrowsAsync<LokiException>(() =>
            Run(new FakeSource(), """bytes_over_time({service_name="api"} | line_format "x" [1m])""", Minute, Minute, Minute));

        Assert.Contains("line_format", ex.Message);
    }

    [Fact]
    public async Task CountOverTime_IgnoresLineFormat()
    {
        var source = new FakeSource(Row(0, 3, "api"));

        var result = await Run(source, """count_over_time({service_name="api"} | line_format "x" [1m])""", Minute, Minute, Minute);

        Assert.Equal(3d, Assert.Single(result.Series).Values[0]);
    }

    [Fact]
    public void BucketStats_MergeIsExactForCountSumMinMaxAndVariance()
    {
        var merged = Stats(1, 3).Then(Stats(5)).Then(Stats(7, 9));
        var all = Stats(1, 3, 5, 7, 9);

        Assert.Equal(all.Count, merged.Count);
        Assert.Equal(all.Sum, merged.Sum);
        Assert.Equal((all.Min, all.Max, all.First, all.Last), (merged.Min, merged.Max, merged.First, merged.Last));
        Assert.Equal(all.Mean, merged.Mean, precision: 12);
        Assert.Equal(all.M2, merged.M2, precision: 12);
    }
}
