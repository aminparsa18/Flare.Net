using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Query;

public class SpanCountQueryBuilderTests
{
    [Fact]
    public void Build_GroupsByTraceId_OverTheSpansTable()
    {
        var result = SpanCountQueryBuilder.Build(["trace-a", "trace-b"]);

        Assert.Contains("SELECT TraceId, count() AS SpanCount", result.Sql);
        Assert.Contains("FROM spans", result.Sql);
        Assert.Contains("WHERE TraceId IN {traceIds:Array(String)}", result.Sql);
        Assert.Contains("GROUP BY TraceId", result.Sql);
    }

    [Fact]
    public void Build_BindsTraceIds_AsAnArrayParameter()
    {
        var result = SpanCountQueryBuilder.Build(["trace-a", "trace-b"]);

        var parameters = result.Parameters.ToDictionary();
        Assert.Equal(new[] { "trace-a", "trace-b" }, parameters["traceIds"]);
    }

    [Fact]
    public void Build_Deduplicates_RepeatedTraceIds()
    {
        var result = SpanCountQueryBuilder.Build(["trace-a", "trace-a", "trace-b"]);

        var parameters = result.Parameters.ToDictionary();
        Assert.Equal(new[] { "trace-a", "trace-b" }, parameters["traceIds"]);
    }
}
