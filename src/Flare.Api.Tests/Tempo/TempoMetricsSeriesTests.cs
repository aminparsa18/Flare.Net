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
}
