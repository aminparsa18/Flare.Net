using Flare.Api.Model;
using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Query;

public class ExceptionCountConditionQueryBuilderTests
{
    private static readonly DateTimeOffset From = new(2026, 8, 10, 11, 55, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset To = new(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Build_WithNullFilter_DoesNotThrow()
    {
        // Same System.Text.Json init-only-property caveat MetricAlertConditionQueryBuilderTests guards against.
        var result = ExceptionCountConditionQueryBuilder.Build(
            new ExceptionCountCondition { ExceptionType = "System.NullReferenceException", Filter = null! }, From, To);

        Assert.Contains("SELECT count() FROM spans", result.Sql);
    }

    [Fact]
    public void Build_IncludesArrayJoinAndEventNameFilter()
    {
        var result = ExceptionCountConditionQueryBuilder.Build(
            new ExceptionCountCondition { ExceptionType = "System.NullReferenceException" }, From, To);

        Assert.Contains("ARRAY JOIN Events.TimeUnixNano AS EventTime, Events.Name AS EventName, Events.Attributes AS EventAttributes", result.Sql);
        Assert.Contains("EventName = {eventName:String}", result.Sql);
        Assert.Contains("EventAttributes['exception.type'] = {exceptionType:String}", result.Sql);
        Assert.DoesNotContain("exception.message", result.Sql);
    }

    [Fact]
    public void Build_BindsExceptionTypeAndWindow()
    {
        var result = ExceptionCountConditionQueryBuilder.Build(
            new ExceptionCountCondition { ExceptionType = "System.NullReferenceException" }, From, To);

        var parameters = result.Parameters.ToDictionary();
        Assert.Equal("System.NullReferenceException", parameters["exceptionType"]);
        Assert.Equal(From.UtcDateTime, parameters["from"]);
        Assert.Equal(To.UtcDateTime, parameters["to"]);
    }

    [Fact]
    public void Build_WithMessage_AddsExactMessageClause()
    {
        var result = ExceptionCountConditionQueryBuilder.Build(
            new ExceptionCountCondition { ExceptionType = "System.NullReferenceException", ExceptionMessage = "Object reference not set" },
            From,
            To);

        Assert.Contains("EventAttributes['exception.message'] = {exceptionMessage:String}", result.Sql);
        Assert.Equal("Object reference not set", result.Parameters.ToDictionary()["exceptionMessage"]);
    }

    [Fact]
    public void Build_ServiceFilter_AddsServiceNameClause()
    {
        var result = ExceptionCountConditionQueryBuilder.Build(
            new ExceptionCountCondition
            {
                ExceptionType = "System.NullReferenceException",
                Filter = new ExceptionFilter { Services = ["checkout-api"] },
            },
            From,
            To);

        Assert.Contains("ServiceName IN {services:Array(String)}", result.Sql);
    }
}
