using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Query;

public class TraceByIdQueryBuilderTests
{
    [Fact]
    public void Build_FiltersByExactTraceId()
    {
        var result = TraceByIdQueryBuilder.Build("0102030405060708090a0b0c0d0e0f10");

        Assert.Contains("WHERE TraceId = {traceId:String}", result.Sql);
        Assert.Equal("0102030405060708090a0b0c0d0e0f10", result.Parameters.ToDictionary()["traceId"]);
    }

    [Fact]
    public void Build_OrdersByStartTime_Ascending_ForWaterfallRendering()
    {
        var result = TraceByIdQueryBuilder.Build("0102030405060708090a0b0c0d0e0f10");

        Assert.Contains("ORDER BY StartTime\n", result.Sql);
        Assert.DoesNotContain("ORDER BY StartTime DESC", result.Sql);
    }

    [Fact]
    public void Build_AppliesTheMaxSpansSafetyCap()
    {
        var result = TraceByIdQueryBuilder.Build("0102030405060708090a0b0c0d0e0f10");

        Assert.Contains("LIMIT {limit:UInt64}", result.Sql);
        Assert.Equal(TraceByIdQueryBuilder.MaxSpans, result.Parameters.ToDictionary()["limit"]);
    }

    [Fact]
    public void Build_SelectsEverySpanColumn_FromSpansTable()
    {
        var result = TraceByIdQueryBuilder.Build("0102030405060708090a0b0c0d0e0f10");

        Assert.Contains("SELECT TraceId, SpanId, ParentSpanId", result.Sql);
        Assert.Contains("FROM spans", result.Sql);
    }
}
