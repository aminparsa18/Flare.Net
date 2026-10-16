using System.Text.Json;
using Flare.Api.Query;
using Flare.Api.Tempo;
using Xunit;

namespace Flare.Api.Tests.Tempo;

public class TempoMetricsSeriesTests
{
    // Four 60 s steps from 1_790_000_040.
    private static readonly TempoMetricsWindow Window = new(1_790_000_040, 1_790_000_280, 60);

    private static TempoMetricsPlan Plan(string traceQl) => TempoMetricsPlanner.Plan(TraceQlParser.ParseMetrics(traceQl));

    private static SpanMetricRow Row(long bucket, double weight, double[] values, params string[] group) => new(bucket, group, weight, values);

    private static JsonElement Json(Action<Utf8JsonWriter> write)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            write(writer);
        }

        return JsonDocument.Parse(stream.ToArray()).RootElement.Clone();
    }

    [Fact]
    public void Rate_IsPerSecondAndZeroInStepsWithoutSpans()
    {
        var result = TempoMetricsSeries.Build(Plan("{ } | rate()"), [Row(0, 120, [120]), Row(2, 30, [30])], Window);

        var series = Assert.Single(result.Series);
        Assert.Equal(new TempoLabel("__name__", "rate"), Assert.Single(series.Labels));
        Assert.Equal(new[] { 2.0, 0, 0.5, 0 }, series.Samples.Select(s => s.Value));
        Assert.Equal(new[] { 1_790_000_040_000L, 1_790_000_100_000, 1_790_000_160_000, 1_790_000_220_000 }, series.Samples.Select(s => s.TimestampMs));
        Assert.Null(result.PartialMessage);
    }

    [Fact]
    public void AttributeValues_AreReportedAsTheyAreNotAsSeconds()
    {
        var result = TempoMetricsSeries.Build(Plan("{ } | avg_over_time(span.size)"), [Row(1, 10, [2048]), Row(3, 5, [512.5])], Window);

        Assert.Equal(new[] { 2048.0, 512.5 }, Assert.Single(result.Series).Samples.Select(s => s.Value));
    }

    [Fact]
    public void Histogram_IsOneSeriesPerBucketLabelledWithItsUpperBoundInSeconds()
    {
        var result = TempoMetricsSeries.Build(
            Plan("{ } | histogram_over_time(duration)"),
            [Row(0, 3, [20], "x"), Row(1, 4, [21], "x"), Row(2, 5, [20], "x")],
            Window);

        Assert.Equal(2, result.Series.Count);
        Assert.Equal(new TempoLabel("__bucket", null, 1048576 / 1e9), Assert.Single(result.Series[0].Labels));
        Assert.Equal(new[] { 3.0, 0, 5, 0 }, result.Series[0].Samples.Select(s => s.Value));
        Assert.Equal(2097152 / 1e9, result.Series[1].Labels[0].Number);
        Assert.Equal(new[] { 0.0, 4, 0, 0 }, result.Series[1].Samples.Select(s => s.Value));
    }

    [Fact]
    public void Histogram_OfAnAttribute_LabelsTheRawBound()
    {
        var result = TempoMetricsSeries.Build(Plan("{ } | histogram_over_time(span.size) by (name)"), [Row(0, 3, [10], "GET")], Window);

        var series = Assert.Single(result.Series);
        Assert.Equal(new[] { new TempoLabel("name", "GET"), new TempoLabel("__bucket", null, 1024) }, series.Labels);
    }

    [Fact]
    public void Histogram_KeepsFewerGroupsAndSaysSo()
    {
        var rows = Enumerable.Range(0, TraceQlMetricsQueryBuilder.MaxHistogramGroups + 1).Select(i => Row(0, 100 - i, [10], $"g{i:00}")).ToArray();

        var result = TempoMetricsSeries.Build(Plan("{ } | histogram_over_time(duration) by (name)"), rows, Window);

        Assert.Equal(TraceQlMetricsQueryBuilder.MaxHistogramGroups, result.Series.Count);
        Assert.Contains($"{TraceQlMetricsQueryBuilder.MaxHistogramGroups} groups", result.PartialMessage);
    }

    [Fact]
    public void Exemplars_GoOnTheSeriesOfTheirGroup()
    {
        var result = TempoMetricsSeries.Build(
            Plan("{ } | rate() by (name)"),
            [Row(0, 60, [60], "a"), Row(0, 30, [30], "b")],
            Window,
            [new SpanExemplarRow(0, ["b"], "trace-b", "span-b", 2.5e9, 1_790_000_050_000)]);

        Assert.Null(result.Series[0].Exemplars);
        var exemplar = Assert.Single(result.Series[1].Exemplars!);
        Assert.Equal(2.5, exemplar.Value);
        Assert.Equal(1_790_000_050_000, exemplar.TimestampMs);
        Assert.Equal(new[] { new TempoLabel("trace:id", "trace-b"), new TempoLabel("span:id", "span-b") }, exemplar.Labels);
    }

    [Fact]
    public void Exemplars_OfAHistogram_LandInTheBucketOfTheirValue()
    {
        var result = TempoMetricsSeries.Build(
            Plan("{ } | histogram_over_time(duration)"),
            [Row(0, 3, [20]), Row(0, 3, [22])],
            Window,
            [new SpanExemplarRow(0, [], "t", "s", 3_000_000, 1_790_000_050_000)]);

        // 3ms is between 2^21 ns and 2^22 ns, so it belongs to the 2^22 ns bucket
        Assert.Null(result.Series[0].Exemplars);
        Assert.Single(result.Series[1].Exemplars!);
    }

    [Fact]
    public void Exemplars_OfQuantiles_GoOnTheLargestQuantile()
    {
        var result = TempoMetricsSeries.Build(
            Plan("{ } | quantile_over_time(duration, .99, .5)"),
            [Row(0, 3, [3e9, 1e9])],
            Window,
            [new SpanExemplarRow(0, [], "t", "s", 3e9, 1_790_000_050_000)]);

        Assert.Single(result.Series[0].Exemplars!);
        Assert.Null(result.Series[1].Exemplars);
    }

    [Fact]
    public void TopK_KeepsTheLargestSeriesAtEachStepAndDropsTheOthers()
    {
        // a is largest in the first step, b in the second; c is never in the top
        var rows = new[]
        {
            Row(0, 60, [6e9], "a"), Row(1, 6, [6e8], "a"),
            Row(0, 30, [3e9], "b"), Row(1, 90, [9e9], "b"),
            Row(0, 6, [6e8], "c"), Row(1, 6, [6e8], "c"),
        };

        var result = TempoMetricsSeries.Build(Plan("{ } | avg_over_time(duration) by (name) | topk(1)"), rows, Window);

        Assert.Equal(2, result.Series.Count);
        // series are ordered by their spans, b having the most
        Assert.Equal("b", result.Series[0].Labels[0].Text);
        Assert.Equal(new[] { 1_790_000_100_000L }, result.Series[0].Samples.Select(s => s.TimestampMs));
        Assert.Equal("a", result.Series[1].Labels[0].Text);
        Assert.Equal(new[] { 1_790_000_040_000L }, result.Series[1].Samples.Select(s => s.TimestampMs));
    }

    [Fact]
    public void TopK_KeepsAnExemplarOnlyWhereItsSeriesKeptTheStep()
    {
        var rows = new[] { Row(0, 60, [6e9], "a"), Row(1, 6, [6e8], "a"), Row(0, 30, [3e9], "b"), Row(1, 90, [9e9], "b") };
        var exemplars = new[]
        {
            new SpanExemplarRow(0, ["a"], "kept", "s", 6e9, 1_790_000_050_000),
            new SpanExemplarRow(1, ["a"], "dropped", "s", 6e8, 1_790_000_110_000),
        };

        var result = TempoMetricsSeries.Build(Plan("{ } | avg_over_time(duration) by (name) | topk(1)"), rows, Window, exemplars);

        var a = Assert.Single(result.Series, s => s.Labels[0].Text == "a");
        Assert.Equal("kept", Assert.Single(a.Exemplars!).Labels[0].Text);
    }

    [Fact]
    public void BottomK_KeepsTheSmallest()
    {
        var rows = new[] { Row(0, 60, [6e9], "a"), Row(0, 30, [3e9], "b"), Row(0, 6, [1e9], "c") };

        var result = TempoMetricsSeries.Build(Plan("{ } | avg_over_time(duration) by (name) | bottomk(2)"), rows, Window);

        Assert.Equal(new[] { "b", "c" }, result.Series.Select(s => s.Labels[0].Text).OrderBy(x => x));
    }

    [Fact]
    public void Comparison_DropsTheSamplesThatFailAndTheSeriesLeftEmpty()
    {
        var rows = new[]
        {
            Row(0, 120, [120], "a"), Row(1, 600, [600], "a"),   // a: 2/s then 10/s
            Row(0, 60, [60], "b"), Row(1, 60, [60], "b"),       // b: 1/s, 1/s
        };

        var result = TempoMetricsSeries.Build(Plan("{ } | rate() by (name) > 5"), rows, Window);

        var a = Assert.Single(result.Series);
        Assert.Equal("a", a.Labels[0].Text);
        Assert.Equal(new[] { 1_790_000_100_000L }, a.Samples.Select(s => s.TimestampMs));
        Assert.Equal(10.0, Assert.Single(a.Samples).Value);
    }

    [Theory]
    [InlineData("> 2")]
    [InlineData(">= 2")]
    [InlineData("< 2")]
    [InlineData("<= 0")]
    [InlineData("= 2")]
    [InlineData("!= 0")]
    public void Comparison_UsesEachOperator(string stage)
    {
        // rate is 2, 0, 0.5, 0 per second over the four steps
        var rows = new[] { Row(0, 120, [120]), Row(2, 30, [30]) };

        var result = TempoMetricsSeries.Build(Plan("{ } | rate() " + stage), rows, Window);

        var expected = stage switch
        {
            "> 2" => Array.Empty<double>(),
            ">= 2" => [2.0],
            "< 2" => [0, 0.5, 0],
            "<= 0" => [0, 0],
            "= 2" => [2.0],
            _ => new[] { 2.0, 0.5 },
        };
        Assert.Equal(expected, result.Series.SelectMany(s => s.Samples).Select(s => s.Value));
    }

    [Fact]
    public void Comparison_ThatNothingSatisfies_GivesNoSeries()
    {
        var result = TempoMetricsSeries.Build(Plan("{ } | rate() > 1000"), [Row(0, 120, [120])], Window);

        Assert.Empty(result.Series);
    }

    [Fact]
    public void Comparison_FiltersEachQuantileSeriesOnItsOwn()
    {
        // p50 = 1 s, p99 = 5 s in the one step
        var result = TempoMetricsSeries.Build(Plan("{ } | quantile_over_time(duration, .5, .99) > 2"), [Row(0, 10, [1e9, 5e9])], Window);

        var series = Assert.Single(result.Series);
        Assert.Equal(0.99, series.Labels.Single(l => l.Key == "p").Number);
    }

    [Fact]
    public void Comparison_ThenTopK_RanksOnlyTheSamplesThatPassed()
    {
        // step 0: a = 10/s, b = 2/s; step 1: a = 1/s, b = 3/s. Dropping < 2 first leaves a only in step 0, b in both.
        var rows = new[] { Row(0, 600, [600], "a"), Row(1, 60, [60], "a"), Row(0, 120, [120], "b"), Row(1, 180, [180], "b") };

        var result = TempoMetricsSeries.Build(Plan("{ } | rate() by (name) >= 2 | topk(1)"), rows, Window);

        var a = Assert.Single(result.Series, s => s.Labels[0].Text == "a");
        var b = Assert.Single(result.Series, s => s.Labels[0].Text == "b");
        Assert.Equal(new[] { 1_790_000_040_000L }, a.Samples.Select(s => s.TimestampMs));
        Assert.Equal(new[] { 1_790_000_100_000L }, b.Samples.Select(s => s.TimestampMs));
    }

    [Fact]
    public void TopK_ThenComparison_FiltersTheWinners()
    {
        // topk(1) keeps a at step 0 (10/s) and b at step 1 (3/s); > 5 then leaves only a's sample
        var rows = new[] { Row(0, 600, [600], "a"), Row(1, 60, [60], "a"), Row(0, 120, [120], "b"), Row(1, 180, [180], "b") };

        var result = TempoMetricsSeries.Build(Plan("{ } | rate() by (name) | topk(1) > 5"), rows, Window);

        var a = Assert.Single(result.Series);
        Assert.Equal("a", a.Labels[0].Text);
        Assert.Equal(new[] { 1_790_000_040_000L }, a.Samples.Select(s => s.TimestampMs));
    }

    [Fact]
    public void Comparison_KeepsAnExemplarOnlyForAStepItsSeriesKept()
    {
        var rows = new[] { Row(0, 600, [600]), Row(1, 60, [60]) };
        var exemplars = new[]
        {
            new SpanExemplarRow(0, [], "kept", "s", 1e9, 1_790_000_050_000),
            new SpanExemplarRow(1, [], "dropped", "s", 1e9, 1_790_000_110_000),
        };

        var result = TempoMetricsSeries.Build(Plan("{ } | rate() > 5"), rows, Window, exemplars);

        Assert.Equal("kept", Assert.Single(Assert.Single(result.Series).Exemplars!).Labels[0].Text);
    }

    [Fact]
    public void Comparison_AppliesToTheInstantValue()
    {
        var kept = TempoMetricsSeries.Build(Plan("{ } | rate() > 1"), [Row(0, 120, [120])], new TempoMetricsWindow(1_790_000_040, 1_790_000_100, 60));
        var dropped = TempoMetricsSeries.Build(Plan("{ } | rate() > 5"), [Row(0, 120, [120])], new TempoMetricsWindow(1_790_000_040, 1_790_000_100, 60));

        Assert.Single(Json(w => TempoMetricsSeries.WriteInstant(w, kept)).GetProperty("series").EnumerateArray());
        Assert.Empty(Json(w => TempoMetricsSeries.WriteInstant(w, dropped)).GetProperty("series").EnumerateArray());
    }

    [Fact]
    public void ExemplarsAreWrittenOnTheSeries()
    {
        var result = TempoMetricsSeries.Build(
            Plan("{ } | rate()"),
            [Row(0, 60, [60])],
            Window,
            [new SpanExemplarRow(0, [], "abc", "def", 1e9, 1_790_000_050_000)]);

        var json = Json(w => TempoMetricsSeries.WriteRange(w, result));

        var exemplar = json.GetProperty("series")[0].GetProperty("exemplars")[0];
        Assert.Equal("abc", exemplar.GetProperty("labels")[0].GetProperty("value").GetProperty("stringValue").GetString());
        Assert.Equal("trace:id", exemplar.GetProperty("labels")[0].GetProperty("key").GetString());
        Assert.Equal(1.0, exemplar.GetProperty("value").GetDouble());
        Assert.Equal("1790000050000", exemplar.GetProperty("timestampMs").GetString());
    }

    [Fact]
    public void CountOverTime_IsTheWeightPerStep()
    {
        var result = TempoMetricsSeries.Build(Plan("{ } | count_over_time()"), [Row(1, 7, [7])], Window);

        Assert.Equal(new[] { 0.0, 7, 0, 0 }, Assert.Single(result.Series).Samples.Select(s => s.Value));
    }

    [Fact]
    public void Quantiles_AreOneSeriesEachInSecondsAndSkipEmptySteps()
    {
        var result = TempoMetricsSeries.Build(Plan("{ } | quantile_over_time(duration, .5, .95)"), [Row(1, 10, [2e9, 4e9]), Row(3, 5, [1e9, 3e9])], Window);

        Assert.Equal(2, result.Series.Count);
        Assert.Equal(new TempoLabel("p", null, 0.5), Assert.Single(result.Series[0].Labels));
        Assert.Equal(new[] { 2.0, 1 }, result.Series[0].Samples.Select(s => s.Value));
        Assert.Equal(new[] { 1_790_000_100_000L, 1_790_000_220_000 }, result.Series[0].Samples.Select(s => s.TimestampMs));
        Assert.Equal(new[] { 4.0, 3 }, result.Series[1].Samples.Select(s => s.Value));
    }

    [Fact]
    public void Grouping_LabelsEachSeriesWithTheFieldsAsWritten()
    {
        var plan = Plan("{ } | count_over_time() by (resource.service.name, status, kind)");
        var rows = new[]
        {
            Row(0, 1, [1], "api", "STATUS_CODE_ERROR", "2"),
            Row(0, 9, [9], "shop", "STATUS_CODE_OK", "3"),
        };

        var result = TempoMetricsSeries.Build(plan, rows, Window);

        // Most spans first.
        Assert.Equal(
            new[]
            {
                new TempoLabel("resource.service.name", "shop"), new TempoLabel("status", "ok"), new TempoLabel("kind", "client"),
            },
            result.Series[0].Labels);
        Assert.Equal(
            new[]
            {
                new TempoLabel("resource.service.name", "api"), new TempoLabel("status", "error"), new TempoLabel("kind", "server"),
            },
            result.Series[1].Labels);
    }

    [Fact]
    public void QuantileWithGrouping_AddsPAfterTheGroupLabels()
    {
        var result = TempoMetricsSeries.Build(Plan("{ } | quantile_over_time(duration, .9) by (name)"), [Row(0, 1, [1e9], "GET /")], Window);

        Assert.Equal(new[] { new TempoLabel("name", "GET /"), new TempoLabel("p", null, 0.9) }, Assert.Single(result.Series).Labels);
    }

    [Fact]
    public void MoreSeriesThanTheCap_KeepsTheLargestAndSaysSo()
    {
        var rows = Enumerable.Range(0, TraceQlMetricsQueryBuilder.MaxSeries + 1).Select(i => Row(0, i + 1, [i + 1], $"s{i}")).ToArray();

        var result = TempoMetricsSeries.Build(Plan("{ } | count_over_time() by (name)"), rows, Window);

        Assert.Equal(TraceQlMetricsQueryBuilder.MaxSeries, result.Series.Count);
        Assert.DoesNotContain(result.Series, s => s.Labels[0].Text == "s0");
        Assert.Contains("most spans", result.PartialMessage);
    }

    [Fact]
    public void NoRows_IsNoSeries()
    {
        Assert.Empty(TempoMetricsSeries.Build(Plan("{ } | rate()"), [], Window).Series);
    }

    [Fact]
    public void WriteRange_UsesTempoFieldNames()
    {
        var result = TempoMetricsSeries.Build(Plan("{ } | quantile_over_time(duration, .5) by (name)"), [Row(0, 1, [1.5e9], "GET /")], Window);

        var json = Json(w => TempoMetricsSeries.WriteRange(w, result));

        var series = Assert.Single(json.GetProperty("series").EnumerateArray());
        var labels = series.GetProperty("labels").EnumerateArray().ToArray();
        Assert.Equal("name", labels[0].GetProperty("key").GetString());
        Assert.Equal("GET /", labels[0].GetProperty("value").GetProperty("stringValue").GetString());
        Assert.Equal("p", labels[1].GetProperty("key").GetString());
        Assert.Equal(0.5, labels[1].GetProperty("value").GetProperty("doubleValue").GetDouble());
        var sample = Assert.Single(series.GetProperty("samples").EnumerateArray());
        Assert.Equal("1790000040000", sample.GetProperty("timestampMs").GetString());
        Assert.Equal(1.5, sample.GetProperty("value").GetDouble());
        Assert.Equal(JsonValueKind.Object, json.GetProperty("metrics").ValueKind);
        Assert.False(json.TryGetProperty("status", out _));
    }

    [Fact]
    public void WriteRange_WritesIntegerAndBooleanLabels()
    {
        var series = new TempoMetricSeries(
            [TempoLabel.Attribute("span.http.status_code", "503"), TempoLabel.Attribute("span.cache.hit", "true"), TempoLabel.Attribute("span.id", "0123")],
            []);

        var json = Json(w => TempoMetricsSeries.WriteRange(w, new TempoMetricsResult([series], null)));

        var labels = json.GetProperty("series")[0].GetProperty("labels").EnumerateArray().Select(l => l.GetProperty("value").ToString().Replace(" ", "")).ToArray();
        Assert.Equal(["""{"intValue":"503"}""", """{"boolValue":true}""", """{"stringValue":"0123"}"""], labels);
    }

    [Fact]
    public void WriteRange_ReportsAPartialResult()
    {
        var json = Json(w => TempoMetricsSeries.WriteRange(w, new TempoMetricsResult([], "cut")));

        Assert.Equal("PARTIAL", json.GetProperty("status").GetString());
        Assert.Equal("cut", json.GetProperty("message").GetString());
    }

    [Fact]
    public void WriteInstant_IsOneValuePerSeries()
    {
        var window = new TempoMetricsWindow(1_790_000_000, 1_790_000_600, 600);
        var result = TempoMetricsSeries.Build(Plan("{ } | rate() by (name)"), [Row(0, 300, [300], "a"), Row(0, 60, [60], "b")], window);

        var json = Json(w => TempoMetricsSeries.WriteInstant(w, result));

        var values = json.GetProperty("series").EnumerateArray().Select(s => s.GetProperty("value").GetDouble()).ToArray();
        Assert.Equal(new[] { 0.5, 0.1 }, values);
        Assert.False(json.GetProperty("series")[0].TryGetProperty("samples", out _));
    }

    [Fact]
    public void GroupingByAnAttribute_LabelsTheValuesTypedAndANameStaysText()
    {
        var plan = Plan("{ } | count_over_time() by (span.http.status_code, resource.cluster.ok, .tq.ratio, span.tq.code, name)");
        var rows = new[]
        {
            Row(0, 9, [9], "500", "true", "0.25", "0123", "GET /"),
            Row(0, 1, [1], "7", "false", "1E+21", "42", "200"),
        };

        var result = TempoMetricsSeries.Build(plan, rows, Window);

        Assert.Equal(
            new[]
            {
                new TempoLabel("span.http.status_code", null, Integer: 500), new TempoLabel("resource.cluster.ok", null, Boolean: true),
                new TempoLabel(".tq.ratio", null, 0.25), new TempoLabel("span.tq.code", "0123"), new TempoLabel("name", "GET /"),
            },
            result.Series[0].Labels);
        // A name that looks like a number is still a name.
        Assert.Equal(new TempoLabel("name", "200"), result.Series[1].Labels[4]);
        Assert.Equal(new TempoLabel("span.tq.code", null, Integer: 42), result.Series[1].Labels[3]);
        Assert.Equal(new TempoLabel(".tq.ratio", null, 1e21), result.Series[1].Labels[2]);
    }

    [Fact]
    public void GroupingByAnAttribute_WritesTheTypedMemberAndKeepsTheRankByWeight()
    {
        var plan = Plan("{ } | count_over_time() by (span.http.status_code)");
        var rows = new[] { Row(0, 1, [1], "200"), Row(0, 5, [5], "1000"), Row(0, 5, [5], "500"), Row(0, 2, [2], "") };

        var result = TempoMetricsSeries.Build(plan, rows, Window);

        // Weight first, then the stored text: "1000" before "500" although 500 < 1000 as numbers.
        Assert.Equal(new[] { 1000L, 500, 200 }, result.Series.Take(2).Concat(result.Series.Skip(3)).Select(s => s.Labels[0].Integer!.Value));
        Assert.Equal("", result.Series[2].Labels[0].Text);
        var json = Json(w => TempoMetricsSeries.WriteRange(w, result));
        Assert.Equal("1000", json.GetProperty("series")[0].GetProperty("labels")[0].GetProperty("value").GetProperty("intValue").GetString());
        Assert.Equal("", json.GetProperty("series")[2].GetProperty("labels")[0].GetProperty("value").GetProperty("stringValue").GetString());
    }

    [Fact]
    public void GroupingByAnAttribute_LeavesTheExemplarLabelsText()
    {
        var plan = Plan("{ } | histogram_over_time(duration) by (span.http.status_code)");
        var rows = new[] { Row(0, 3, [20], "500") };
        var exemplars = new[] { new SpanExemplarRow(0, ["500"], "0123456789abcdef0123456789abcdef", "1234567890123456", 1e9, 1_790_000_050_000) };

        var result = TempoMetricsSeries.Build(plan, rows, Window, exemplars);

        var series = Assert.Single(result.Series);
        Assert.Equal(500L, series.Labels[0].Integer);
        var exemplar = Assert.Single(series.Exemplars!);
        Assert.Equal(new[] { new TempoLabel("trace:id", "0123456789abcdef0123456789abcdef"), new TempoLabel("span:id", "1234567890123456") }, exemplar.Labels);
    }
}
