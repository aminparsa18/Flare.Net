using Flare.Api.Model;
using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Query;

public class DbStatementQueryBuilderTests
{
    private static readonly DateTimeOffset End = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Build_GroupsByServiceSystemAndNormalizedStatement_WeightedBySampleWeight()
    {
        var result = DbStatementQueryBuilder.Build(new DbStatementRequest(), 60, "totalTime", End);

        Assert.Contains("GROUP BY ServiceName, DbSystem, Statement", result.Sql);
        Assert.Contains("HAVING Statement != ''", result.Sql);
        Assert.Contains(NPlusOneQueryBuilder.StatementExpr + " AS Statement", result.Sql);
        Assert.Contains("sum(SampleWeight) AS CallCount", result.Sql);
        Assert.Contains("quantileTDigestWeighted(0.95)(DurationNano, SampleWeight)", result.Sql);
        Assert.Contains("ORDER BY TotalNano DESC", result.Sql);
        Assert.DoesNotContain("ParentSpanId", result.Sql);
        Assert.DoesNotContain("{service:String}", result.Sql);
        Assert.DoesNotContain("{dbSystem:String}", result.Sql);

        var parameters = result.Parameters.ToDictionary();
        Assert.Equal((uint)DbStatementQueryBuilder.MaxRows, parameters["limit"]);
        Assert.Equal("STATUS_CODE_ERROR", parameters["errorStatus"]);
    }

    [Fact]
    public void Build_WithServiceAndSystem_AddsEqualityFilters()
    {
        var result = DbStatementQueryBuilder.Build(new DbStatementRequest { Service = "orders-api", DbSystem = "postgresql" }, 60, "calls", End);

        Assert.Contains("ServiceName = {service:String}", result.Sql);
        Assert.Contains("= {dbSystem:String}", result.Sql);
        Assert.Contains("ORDER BY CallCount DESC", result.Sql);
        var parameters = result.Parameters.ToDictionary();
        Assert.Equal("orders-api", parameters["service"]);
        Assert.Equal("postgresql", parameters["dbSystem"]);
    }

    [Theory]
    [InlineData(null, "totalTime")]
    [InlineData("", "totalTime")]
    [InlineData("p95", "p95")]
    [InlineData("errors", "errors")]
    [InlineData("TotalNano; DROP TABLE spans", "totalTime")]
    public void ResolveSort_AcceptsOnlyWhitelistedKeys(string? requested, string expected) =>
        Assert.Equal(expected, DbStatementQueryBuilder.ResolveSort(requested));

    [Theory]
    [InlineData(null, 60)]
    [InlineData(0, 60)]
    [InlineData(1, 5)]
    [InlineData(99999, 1440)]
    public void ClampWindowMinutes_DefaultsAndClamps(int? requested, int expected) =>
        Assert.Equal(expected, DbStatementQueryBuilder.ClampWindowMinutes(requested));
}
