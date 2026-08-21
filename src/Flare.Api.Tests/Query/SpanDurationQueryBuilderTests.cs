using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Query;

public class SpanDurationQueryBuilderTests
{
    [Fact]
    public void Build_SelectsDurationNano_FilteredByTraceIdAndSpanId()
    {
        var result = SpanDurationQueryBuilder.Build([("trace-a", "span-a"), ("trace-b", "span-b")]);

        Assert.Contains("SELECT TraceId, SpanId, DurationNano", result.Sql);
        Assert.Contains("FROM spans", result.Sql);
        Assert.Contains("WHERE TraceId IN {traceIds:Array(String)} AND SpanId IN {spanIds:Array(String)}", result.Sql);
    }

    [Fact]
    public void Build_BindsTraceIdsAndSpanIds_AsSeparateArrayParameters()
    {
        var result = SpanDurationQueryBuilder.Build([("trace-a", "span-a"), ("trace-b", "span-b")]);

        var parameters = result.Parameters.ToDictionary();
        Assert.Equal(new[] { "trace-a", "trace-b" }, parameters["traceIds"]);
        Assert.Equal(new[] { "span-a", "span-b" }, parameters["spanIds"]);
    }

    [Fact]
    public void Build_Deduplicates_RepeatedPairs()
    {
        var result = SpanDurationQueryBuilder.Build([("trace-a", "span-a"), ("trace-a", "span-a"), ("trace-b", "span-b")]);

        var parameters = result.Parameters.ToDictionary();
        Assert.Equal(new[] { "trace-a", "trace-b" }, parameters["traceIds"]);
        Assert.Equal(new[] { "span-a", "span-b" }, parameters["spanIds"]);
    }

    [Fact]
    public void Build_DedupesTraceIdsAndSpanIdsIndependently_WhenSharedAcrossDifferentPairs()
    {
        // trace-a appears in two different pairs, span-x is shared by two different traces -
        // each array should still only bind each value once.
        var result = SpanDurationQueryBuilder.Build([("trace-a", "span-x"), ("trace-a", "span-y"), ("trace-b", "span-x")]);

        var parameters = result.Parameters.ToDictionary();
        Assert.Equal(new[] { "trace-a", "trace-b" }, parameters["traceIds"]);
        Assert.Equal(new[] { "span-x", "span-y" }, parameters["spanIds"]);
    }
}
