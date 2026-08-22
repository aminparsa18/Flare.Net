using ClickHouse.Driver.Utility;
using Flare.Api.Model;
using Flare.Api.Query.LogQl;
using Xunit;

namespace Flare.Api.Tests.Query.LogQl;

public class LogQlQueryBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Build_CountWithNoGroupBy_DispatchesToCount_AndUsesPlainCountSql()
    {
        var built = LogQlQueryBuilder.Build(new LogQlQueryRequest { Query = "select count(*) from stream" }, Now);

        Assert.Equal(LogQlDispatchKind.Count, built.Kind);
        Assert.Contains("SELECT toFloat64(count()) FROM logs WHERE", built.Sql);
    }

    [Fact]
    public void Build_CountGroupedByTime_DispatchesToSeries_AndBucketsByInterval()
    {
        var built = LogQlQueryBuilder.Build(
            new LogQlQueryRequest { Query = "select count(*) from stream group by time(1h)" }, Now);

        Assert.Equal(LogQlDispatchKind.Series, built.Kind);
        Assert.False(built.HasGroupKey);
        Assert.Contains("toStartOfInterval", built.Sql);
        Assert.Equal(3600, built.Parameters.ToDictionary()["bucketWidth"]);
    }

    [Fact]
    public void Build_CountGroupedByTimeAndService_HasGroupKey()
    {
        var built = LogQlQueryBuilder.Build(
            new LogQlQueryRequest { Query = "select count(*) from stream group by time(1h), service" }, Now);

        Assert.True(built.HasGroupKey);
        Assert.Contains("ServiceName AS GroupKey", built.Sql);
    }

    [Fact]
    public void Build_SelectStar_DispatchesToRows_AndSelectsLogColumns()
    {
        var built = LogQlQueryBuilder.Build(new LogQlQueryRequest { Query = "select * from stream" }, Now);

        Assert.Equal(LogQlDispatchKind.Rows, built.Kind);
        Assert.Contains("SELECT EventId,", built.Sql);
        Assert.Contains($"LIMIT {{limit:UInt64}}", built.Sql);
        Assert.Equal((uint)(LogQlQueryBuilder.RawRowLimit + 1), built.Parameters.ToDictionary()["limit"]);
    }

    [Fact]
    public void Build_WhereClause_IsAndedOntoTheTimeBound()
    {
        var built = LogQlQueryBuilder.Build(
            new LogQlQueryRequest { Query = "select count(*) from stream where Service = 'checkout'" }, Now);

        Assert.Contains("Timestamp >= {from:DateTime64(9)}", built.Sql);
        Assert.Contains("AND ServiceName = {qlp0:String}", built.Sql);
        Assert.Equal("checkout", built.Parameters.ToDictionary()["qlp0"]);
    }

    [Fact]
    public void Build_FromTo_BindsExplicitTimeRange_NotTheDefaultLookback()
    {
        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero);

        var built = LogQlQueryBuilder.Build(
            new LogQlQueryRequest { Query = "select count(*) from stream", From = from, To = to }, Now);

        var parameters = built.Parameters.ToDictionary();
        Assert.Equal(from.UtcDateTime, parameters["from"]);
        Assert.Equal(to.UtcDateTime, parameters["to"]);
    }

    [Fact]
    public void Build_InvalidQuery_ThrowsLogQlParseException()
    {
        Assert.Throws<LogQlParseException>(() =>
            LogQlQueryBuilder.Build(new LogQlQueryRequest { Query = "not a query" }, Now));
    }

    [Fact]
    public void Build_AvgSeverityNumber_DispatchesToCount_WrappedInFloat64()
    {
        var built = LogQlQueryBuilder.Build(new LogQlQueryRequest { Query = "select avg(SeverityNumber) from stream" }, Now);

        Assert.Equal(LogQlDispatchKind.Count, built.Kind);
        Assert.Contains("SELECT toFloat64(avg(SeverityNumber)) FROM logs WHERE", built.Sql);
    }

    [Fact]
    public void Build_SumSeverityNumberGroupedByTime_DispatchesToSeries()
    {
        var built = LogQlQueryBuilder.Build(
            new LogQlQueryRequest { Query = "select sum(SeverityNumber) from stream group by time(1h)" }, Now);

        Assert.Equal(LogQlDispatchKind.Series, built.Kind);
        Assert.Contains("toFloat64(sum(SeverityNumber)) AS Count", built.Sql);
    }

    [Fact]
    public void Build_SelectColumnList_DispatchesToTable_WithDisplayNamesAndRealColumns()
    {
        var built = LogQlQueryBuilder.Build(new LogQlQueryRequest { Query = "select Service, Body from stream" }, Now);

        Assert.Equal(LogQlDispatchKind.Table, built.Kind);
        Assert.Equal(["Service", "Body"], built.Columns);
        Assert.Contains("SELECT ServiceName, Body\nFROM logs", built.Sql);
        Assert.Equal((uint)(LogQlQueryBuilder.RawRowLimit + 1), built.Parameters.ToDictionary()["limit"]);
    }
}
