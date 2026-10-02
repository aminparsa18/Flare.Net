using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Query;

public class TraceLevelQueryBuilderTests
{
    private const string TraceId = "0102030405060708090a0b0c0d0e0f10";

    [Fact]
    public void BuildRoots_IncludesParentlessAndOrphanedSpans()
    {
        var result = TraceLevelQueryBuilder.BuildRoots(TraceId);

        Assert.Contains("WHERE TraceId = {traceId:String}", result.Sql);
        Assert.Contains("ParentSpanId = ''", result.Sql);
        Assert.Contains("ParentSpanId GLOBAL NOT IN (SELECT SpanId FROM spans WHERE TraceId = {traceId:String})", result.Sql);
        Assert.Equal(TraceId, result.Parameters.ToDictionary()["traceId"]);
    }

    [Fact]
    public void BuildChildren_FiltersByParentSpanIds_WithinTheTrace()
    {
        var result = TraceLevelQueryBuilder.BuildChildren(TraceId, ["aa", "bb"]);

        Assert.Contains("WHERE TraceId = {traceId:String}", result.Sql);
        Assert.Contains("ParentSpanId IN {parents:Array(String)}", result.Sql);
        Assert.Equal(new[] { "aa", "bb" }, (string[])result.Parameters.ToDictionary()["parents"]);
    }

    [Fact]
    public void BuildChildren_OrdersByStartTime_AndCapsAtMaxSpans()
    {
        var result = TraceLevelQueryBuilder.BuildChildren(TraceId, ["aa"]);

        Assert.Contains("ORDER BY StartTime\n", result.Sql);
        Assert.Equal((ulong)TraceByIdQueryBuilder.MaxSpans, result.Parameters.ToDictionary()["limit"]);
    }
}
