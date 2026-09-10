using Flare.Api.Model;
using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Query;

public class ExceptionGroupQueryBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Build_ArrayJoinsEvents_GroupsByTypeAndMessage_OrderedByOccurrenceCount()
    {
        var result = ExceptionGroupQueryBuilder.Build(new ExceptionGroupsRequest(), Now);

        Assert.Contains("ARRAY JOIN Events.TimeUnixNano AS EventTime, Events.Name AS EventName, Events.Attributes AS EventAttributes", result.Sql);
        Assert.Contains("EventAttributes['exception.type'] AS ExceptionType", result.Sql);
        Assert.Contains("EventAttributes['exception.message'] AS ExceptionMessage", result.Sql);
        Assert.Contains("count() AS OccurrenceCount", result.Sql);
        Assert.Contains("min(EventTime) AS FirstSeen", result.Sql);
        Assert.Contains("max(EventTime) AS LastSeen", result.Sql);
        Assert.Contains("groupUniqArray(ServiceName) AS AffectedServices", result.Sql);
        Assert.Contains("FROM spans", result.Sql);
        Assert.Contains("GROUP BY ExceptionType, ExceptionMessage", result.Sql);
        Assert.Contains("ORDER BY OccurrenceCount DESC", result.Sql);
    }

    [Fact]
    public void Build_ExcludesEventsWithNoExceptionType()
    {
        var result = ExceptionGroupQueryBuilder.Build(new ExceptionGroupsRequest(), Now);

        Assert.Contains("AND EventAttributes['exception.type'] != ''", result.Sql);
    }

    [Fact]
    public void Build_NullFilter_DefaultsToEmptyExceptionFilter()
    {
        var result = ExceptionGroupQueryBuilder.Build(new ExceptionGroupsRequest { Filter = null! }, Now);

        var parameters = result.Parameters.ToDictionary();
        Assert.Equal("exception", parameters["eventName"]);
        Assert.Equal((Now - ExceptionFilterSqlBuilder.DefaultLookback).UtcDateTime, parameters["from"]);
    }

    [Theory]
    [InlineData(null, ExceptionGroupQueryBuilder.DefaultTopN)]
    [InlineData(0, ExceptionGroupQueryBuilder.DefaultTopN)]
    [InlineData(-5, ExceptionGroupQueryBuilder.DefaultTopN)]
    [InlineData(50, 50)]
    [InlineData(5000, 1000)]
    public void Build_ClampsTopN(int? requested, int expected)
    {
        var result = ExceptionGroupQueryBuilder.Build(new ExceptionGroupsRequest { TopN = requested }, Now);

        Assert.Equal((uint)expected, result.Parameters.ToDictionary()["topN"]);
    }
}
