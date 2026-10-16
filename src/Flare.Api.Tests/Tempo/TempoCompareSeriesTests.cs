using System.Text.Json;
using Flare.Api.Query;
using Flare.Api.Tempo;
using Xunit;

namespace Flare.Api.Tests.Tempo;

public class TempoCompareSeriesTests
{
    // Two 60 s steps from 1_790_000_040.
    private static readonly TempoMetricsWindow Window = new(1_790_000_040, 1_790_000_160, 60);

    private static TempoMetricsPlan Plan(int topN = 10) => TempoMetricsPlanner.Plan(TraceQlParser.ParseMetrics($"{{ }} | compare({{ status = error }}, {topN})"));

    private static SpanCompareRow Row(long bucket, bool selection, string attribute, string value, double weight) => new(bucket, selection, attribute, value, weight);

    private static string Type(TempoMetricSeries s) => s.Labels.SingleOrDefault(l => l.Key == TempoCompareSeries.MetaType)?.Text ?? "";

    [Fact]
    public void EachValueIsASeriesPerGroup_WithATotalPerGroup()
    {
        var rows = new[]
        {
            Row(0, false, "span.route", "/a", 3), Row(1, false, "span.route", "/a", 1), Row(1, false, "span.route", "/b", 4),
            Row(0, true, "span.route", "/a", 2),
        };

        var result = TempoCompareSeries.Build(Plan(), rows, Window);

        Assert.Equal(new[] { "baseline", "baseline", "selection", "baseline_total", "selection_total" }, result.Series.Select(Type).ToArray());
        var a = result.Series[0];
        Assert.Equal(new TempoLabel("span.route", "/a"), a.Labels[1]);
        Assert.Equal(new[] { 3.0, 1.0 }, a.Samples.Select(s => s.Value));
        Assert.Equal(new[] { 0.0, 4.0 }, result.Series[1].Samples.Select(s => s.Value));
        Assert.Equal(new[] { 1_790_000_040_000L, 1_790_000_100_000L }, a.Samples.Select(s => s.TimestampMs));
    }

    [Fact]
    public void TheTotalCountsEveryValueIncludingThoseNotKept()
    {
        // "" is the weight of the values outside the first pass's list: it is in the total and in no value series.
        var rows = new[] { Row(0, false, "name", "x", 5), Row(0, false, "name", "", 7), Row(1, false, "name", "", 1) };

        var result = TempoCompareSeries.Build(Plan(), rows, Window);

        var total = Assert.Single(result.Series, s => Type(s) == "baseline_total");
        Assert.Equal(new[] { 12.0, 1.0 }, total.Samples.Select(s => s.Value));
        Assert.Single(result.Series, s => Type(s) == "baseline");
    }

    [Fact]
    public void TotalsAndTheErrorMarker_HaveANilAttributeValue()
    {
        var rows = new[] { Row(0, true, "a", "1", 1), Row(0, true, "a", "2", 1), Row(0, true, "a", "3", 1) };

        var result = TempoCompareSeries.Build(Plan(topN: 2), rows, Window);

        var total = Assert.Single(result.Series, s => s.Labels[0].Text == "selection_total");
        Assert.Equal(TempoLabel.Nil("a"), total.Labels[1]);
        var error = result.Series[^1];
        Assert.Equal(new TempoLabel(TempoCompareSeries.MetaError, TempoCompareSeries.TooManyValues), error.Labels[0]);
        Assert.Equal(TempoLabel.Nil("a"), error.Labels[1]);
        Assert.Empty(error.Samples);
    }

    [Fact]
    public void MoreValuesThanTopN_KeepsTheMostCommonAndFlagsTheAttribute()
    {
        var rows = new[] { Row(0, false, "a", "rare", 1), Row(0, false, "a", "common", 9), Row(0, false, "a", "mid", 4) };

        var result = TempoCompareSeries.Build(Plan(topN: 2), rows, Window);

        var kept = result.Series.Where(s => Type(s) == "baseline").Select(s => s.Labels[1].Text);
        Assert.Equal(new[] { "common", "mid" }, kept.ToArray());
        Assert.Contains(result.Series, s => s.Labels[0].Key == TempoCompareSeries.MetaError);
    }

    [Fact]
    public void NotTooMany_HasNoErrorMarker()
    {
        var rows = new[] { Row(0, false, "a", "x", 1), Row(0, true, "a", "y", 1) };

        Assert.DoesNotContain(TempoCompareSeries.Build(Plan(), rows, Window).Series, s => s.Labels[0].Key == TempoCompareSeries.MetaError);
    }

    [Fact]
    public void AValueOnlyInTheSelection_HasNoBaselineSeries()
    {
        var result = TempoCompareSeries.Build(Plan(), [Row(0, true, "span.cluster", "dev", 2)], Window);

        Assert.Equal(new[] { "selection", "selection_total" }, result.Series.Select(Type).ToArray());
    }

    [Fact]
    public void AttributesAreOrderedByName_AndNoRowsMeansNoSeries()
    {
        var rows = new[] { Row(0, false, "span.b", "1", 1), Row(0, false, "name", "x", 1), Row(0, false, "span.a", "1", 1) };

        var keys = TempoCompareSeries.Build(Plan(), rows, Window).Series.Where(s => Type(s) == "baseline").Select(s => s.Labels[1].Key);

        Assert.Equal(new[] { "name", "span.a", "span.b" }, keys.ToArray());
        Assert.Empty(TempoCompareSeries.Build(Plan(), [], Window).Series);
    }

    [Fact]
    public void ANilLabel_IsWrittenAsAnEmptyValue()
    {
        var result = TempoCompareSeries.Build(Plan(), [Row(0, false, "name", "x", 1)], Window);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            TempoMetricsSeries.WriteRange(writer, result);
        }

        var series = JsonDocument.Parse(stream.ToArray()).RootElement.GetProperty("series");
        var total = series.EnumerateArray().Single(s => s.GetProperty("labels")[0].GetProperty("value").GetProperty("stringValue").GetString() == "baseline_total");
        var attribute = total.GetProperty("labels")[1];
        Assert.Equal("name", attribute.GetProperty("key").GetString());
        Assert.Empty(attribute.GetProperty("value").EnumerateObject());
    }
}
