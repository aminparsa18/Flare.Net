using Flare.Api.Model;
using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Query;

public class ExceptionOccurrenceQueryBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);

    private static ExceptionOccurrencesRequest Request(ExceptionFilter? filter = null) => new()
    {
        Filter = filter ?? new ExceptionFilter(),
        ExceptionType = "System.NullReferenceException",
        ExceptionMessage = "Object reference not set to an instance of an object.",
    };

    [Fact]
    public void Build_ArrayJoinsEvents_SelectsSampleColumns_OrderedMostRecentFirst()
    {
        var result = ExceptionOccurrenceQueryBuilder.Build(Request(), Now);

        Assert.Contains("ARRAY JOIN Events.TimeUnixNano AS EventTime, Events.Name AS EventName, Events.Attributes AS EventAttributes", result.Sql);
        Assert.Contains("TraceId", result.Sql);
        Assert.Contains("SpanId", result.Sql);
        Assert.Contains("ServiceName", result.Sql);
        Assert.Contains("Name AS SpanName", result.Sql);
        Assert.Contains("EventTime AS Timestamp", result.Sql);
        Assert.Contains("EventAttributes['exception.stacktrace'] AS Stacktrace", result.Sql);
        Assert.Contains("FROM spans", result.Sql);
        Assert.Contains("ORDER BY EventTime DESC", result.Sql);
    }

    [Fact]
    public void Build_FiltersToExactTypeAndMessage_AsBoundParameters()
    {
        var result = ExceptionOccurrenceQueryBuilder.Build(Request(), Now);

        Assert.Contains("EventAttributes['exception.type'] = {exceptionType:String}", result.Sql);
        Assert.Contains("EventAttributes['exception.message'] = {exceptionMessage:String}", result.Sql);

        var parameters = result.Parameters.ToDictionary();
        Assert.Equal("System.NullReferenceException", parameters["exceptionType"]);
        Assert.Equal("Object reference not set to an instance of an object.", parameters["exceptionMessage"]);
    }

    [Fact]
    public void Build_LimitsToMaxOccurrences()
    {
        var result = ExceptionOccurrenceQueryBuilder.Build(Request(), Now);

        Assert.Contains("LIMIT {limit:UInt32}", result.Sql);
        Assert.Equal((uint)ExceptionOccurrenceQueryBuilder.MaxOccurrences, result.Parameters.ToDictionary()["limit"]);
    }
}
