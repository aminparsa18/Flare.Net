using Flare.Api.Model;
using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Query;

public class NPlusOneQueryBuilderTests
{
    private static readonly DateTimeOffset End = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Build_GroupsByTraceParentServiceAndStatement_ThenRollsUpPerServiceStatement()
    {
        var result = NPlusOneQueryBuilder.Build(new NPlusOneRequest(), 60, 10, End);

        Assert.Contains("GROUP BY TraceId, ParentSpanId, ServiceName, Statement", result.Sql);
        Assert.Contains("HAVING Statement != '' AND Repeats >= {minRepeats:UInt64}", result.Sql);
        Assert.Contains("GROUP BY ServiceName, Statement", result.Sql);
        Assert.Contains("ParentSpanId != ''", result.Sql);
        Assert.Contains($"{ServiceCallBreakdownQueryBuilder.DbSystemExpr} != ''", result.Sql);
        Assert.Contains("ORDER BY TotalDurationMs DESC", result.Sql);
        Assert.DoesNotContain("{service:String}", result.Sql);

        var parameters = result.Parameters.ToDictionary();
        Assert.Equal(10UL, parameters["minRepeats"]);
        Assert.Equal((uint)NPlusOneQueryBuilder.MaxRows, parameters["limit"]);
    }

    [Fact]
    public void Build_WithService_AddsEqualityFilter()
    {
        var result = NPlusOneQueryBuilder.Build(new NPlusOneRequest { Service = "orders-api" }, 60, 10, End);

        Assert.Contains("ServiceName = {service:String}", result.Sql);
        Assert.Equal("orders-api", result.Parameters.ToDictionary()["service"]);
    }

    [Fact]
    public void StatementExpr_StripsLiterals_AndFallsBackToOperationAndCollection()
    {
        Assert.Contains("replaceRegexpAll", NPlusOneQueryBuilder.StatementExpr);
        Assert.Contains("db.query.text", NPlusOneQueryBuilder.StatementExpr);
        Assert.Contains("db.collection.name", NPlusOneQueryBuilder.StatementExpr);
    }

    [Fact]
    public void SpanFilter_NPlusOneOnly_AddsGlobalTraceIdSubquery()
    {
        var off = SpanFilterSqlBuilder.Build(new SpanFilter(), End);
        Assert.DoesNotContain("n1MinRepeats", off.WhereSql);

        var on = SpanFilterSqlBuilder.Build(new SpanFilter { NPlusOneOnly = true }, End);
        Assert.Contains("TraceId GLOBAL IN (SELECT TraceId FROM spans WHERE", on.WhereSql);
        Assert.Contains("count() >= {n1MinRepeats:UInt64}", on.WhereSql);
        Assert.Equal((ulong)NPlusOneQueryBuilder.DefaultMinRepeats, on.Parameters.ToDictionary()["n1MinRepeats"]);
    }

    [Theory]
    [InlineData(null, 60)]
    [InlineData(0, 60)]
    [InlineData(1, 5)]
    [InlineData(99999, 1440)]
    public void ClampWindowMinutes_DefaultsAndClamps(int? requested, int expected) =>
        Assert.Equal(expected, NPlusOneQueryBuilder.ClampWindowMinutes(requested));

    [Theory]
    [InlineData(null, 10)]
    [InlineData(1, 2)]
    [InlineData(50, 50)]
    [InlineData(100000, 1000)]
    public void ClampMinRepeats_DefaultsAndClamps(int? requested, int expected) =>
        Assert.Equal(expected, NPlusOneQueryBuilder.ClampMinRepeats(requested));
}
