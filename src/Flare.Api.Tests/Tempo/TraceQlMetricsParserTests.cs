using Flare.Api.Tempo;
using Xunit;

namespace Flare.Api.Tests.Tempo;

public class TraceQlMetricsParserTests
{
    [Fact]
    public void Rate_WithoutGrouping()
    {
        var query = TraceQlParser.ParseMetrics("""{ resource.service.name = "api" && status = error } | rate()""");

        Assert.Equal(TraceQlMetricsFunction.Rate, query.Function);
        Assert.Equal("rate", query.FunctionName);
        Assert.Equal(2, query.Spanset.Predicates.Count);
        Assert.Empty(query.By);
        Assert.Empty(query.Quantiles);
    }

    [Theory]
    [InlineData("count_over_time()", TraceQlMetricsFunction.CountOverTime)]
    [InlineData("avg_over_time(duration)", TraceQlMetricsFunction.AvgOverTime)]
    [InlineData("min_over_time(duration)", TraceQlMetricsFunction.MinOverTime)]
    [InlineData("max_over_time(duration)", TraceQlMetricsFunction.MaxOverTime)]
    [InlineData("sum_over_time(duration)", TraceQlMetricsFunction.SumOverTime)]
    public void EachFunction_IsRecognised(string stage, TraceQlMetricsFunction expected)
    {
        var query = TraceQlParser.ParseMetrics("{ } | " + stage);

        Assert.Equal(expected, query.Function);
        Assert.StartsWith(query.FunctionName, stage, StringComparison.Ordinal);
    }

    [Fact]
    public void QuantileOverTime_ReadsTheQuantiles()
    {
        var query = TraceQlParser.ParseMetrics("{ } | quantile_over_time(duration, .5, 0.95, 1)");

        Assert.Equal(TraceQlMetricsFunction.QuantileOverTime, query.Function);
        Assert.Equal(new[] { 0.5, 0.95, 1 }, query.Quantiles);
    }

    [Fact]
    public void By_ReadsEveryFieldInOrder()
    {
        var query = TraceQlParser.ParseMetrics("{ } | rate() by (resource.service.name, span.http.route, name, status, .k8s.namespace.name)");

        Assert.Equal(new[] { "resource.service.name", "span.http.route", "name", "status", ".k8s.namespace.name" }, query.By);
    }

    [Fact]
    public void ExemplarsHint_IsAcceptedAndIgnored()
    {
        var query = TraceQlParser.ParseMetrics("{ } | rate() by (name) with(exemplars=true)");

        Assert.Equal(new[] { "name" }, query.By);
    }

    [Theory]
    [InlineData("{ } | histogram_over_time(duration)", "histogram_over_time")]
    [InlineData("{ } | compare({ status = error })", "compare")]
    [InlineData("{ } | rate() | topk(3)", "second pipeline stage")]
    [InlineData("{ } | count() > 2", "'count'")]
    [InlineData("{ } | quantile_over_time(duration)", "1 to 10 quantiles")]
    [InlineData("{ } | quantile_over_time(duration, 1.5)", "not a quantile")]
    [InlineData("{ } | quantile_over_time(span.size, .5)", "only duration")]
    [InlineData("{ } | avg_over_time(span.size)", "only duration")]
    [InlineData("{ } | avg_over_time()", "needs the field")]
    [InlineData("{ } | rate() by (duration)", "cannot be grouped by")]
    [InlineData("{ } | rate() by (span.)", "cannot be grouped by")]
    [InlineData("{ } | rate() by (name, name, name, name, name, name)", "at most 5")]
    [InlineData("{ } | rate() with(sample=true)", "hint")]
    [InlineData("{ }", "needs a function")]
    [InlineData("{ } | rate() extra", "unexpected")]
    [InlineData("{ a = \"1\" } && { b = \"2\" } | rate()", "one spanset")]
    [InlineData("{ } >> { } | rate()", "one spanset")]
    [InlineData("| rate()", "pipeline")]
    [InlineData("", "empty")]
    public void Unsupported_IsRejectedNamingTheConstruct(string traceQl, string mentions)
    {
        var ex = Assert.Throws<TraceQlException>(() => TraceQlParser.ParseMetrics(traceQl));

        Assert.Contains(mentions, ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Search_StillRejectsAPipeline()
    {
        Assert.Throws<TraceQlException>(() => TraceQlParser.Parse("{ } | rate()"));
    }
}
