using Flare.Api.Model;
using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Query;

public class LogPatternQueryBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Build_WithNullFilter_DoesNotThrow()
    {
        // See LogAggregateQueryBuilderTests' equivalent test: System.Text.Json overwrites
        // request.Filter's default back to null when "filter" is absent from the body.
        var result = LogPatternQueryBuilder.Build(new LogPatternRequest { Filter = null! }, Now);

        Assert.Contains("WHERE Timestamp >=", result.Sql);
    }

    [Fact]
    public void Build_GroupsByPatternId_AndOrdersByCountDescending()
    {
        var result = LogPatternQueryBuilder.Build(new LogPatternRequest(), Now);

        Assert.Contains("GROUP BY PatternId", result.Sql);
        Assert.Contains("ORDER BY Count DESC", result.Sql);
    }

    [Fact]
    public void Build_ExcludesUnannotatedRows()
    {
        var result = LogPatternQueryBuilder.Build(new LogPatternRequest(), Now);

        Assert.Contains("AND PatternId != {emptyPattern:String}", result.Sql);
        Assert.Equal(string.Empty, result.Parameters.ToDictionary()["emptyPattern"]);
    }

    [Fact]
    public void Build_CountsErrorsAtOrAboveTheOTelErrorFloor()
    {
        var result = LogPatternQueryBuilder.Build(new LogPatternRequest(), Now);

        Assert.Contains("countIf(SeverityNumber >= {errorSeverityFloor:UInt8}) AS ErrorCount", result.Sql);
        Assert.Equal(17, result.Parameters.ToDictionary()["errorSeverityFloor"]);
    }

    [Fact]
    public void Build_DefaultsTopN_WhenNotProvided()
    {
        var result = LogPatternQueryBuilder.Build(new LogPatternRequest(), Now);

        Assert.Equal(LogPatternQueryBuilder.DefaultTopN, result.Parameters.ToDictionary()["topN"]);
    }

    [Theory]
    [InlineData(0, LogPatternQueryBuilder.DefaultTopN)]
    [InlineData(-5, LogPatternQueryBuilder.DefaultTopN)]
    [InlineData(50, 50)]
    [InlineData(5_000, 1_000)]
    public void Build_ClampsTopN_ToValidRange(int requested, int expected)
    {
        var result = LogPatternQueryBuilder.Build(new LogPatternRequest { TopN = requested }, Now);

        Assert.Equal(expected, result.Parameters.ToDictionary()["topN"]);
    }

    [Fact]
    public void Build_BindsPatternIdFilter_WhenProvided()
    {
        var result = LogPatternQueryBuilder.Build(
            new LogPatternRequest { Filter = new LogFilter { PatternId = "a82db7c88f594553" } }, Now);

        Assert.Contains("PatternId = {patternId:String}", result.Sql);
        Assert.Equal("a82db7c88f594553", result.Parameters.ToDictionary()["patternId"]);
    }
}
