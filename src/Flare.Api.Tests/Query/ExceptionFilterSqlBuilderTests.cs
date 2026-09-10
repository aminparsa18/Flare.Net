using Flare.Api.Model;
using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Query;

public class ExceptionFilterSqlBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Build_WithNoFilters_BoundsTimeRange_UsingDefaultLookback_AndFiltersToExceptionEvents()
    {
        var result = ExceptionFilterSqlBuilder.Build(new ExceptionFilter(), Now);

        Assert.Equal(
            "EventName = {eventName:String} AND StartTime >= {from:DateTime64(9)} AND StartTime < {to:DateTime64(9)}",
            result.WhereSql);

        var parameters = result.Parameters.ToDictionary();
        Assert.Equal("exception", parameters["eventName"]);
        Assert.Equal((Now - ExceptionFilterSqlBuilder.DefaultLookback).UtcDateTime, parameters["from"]);
        Assert.Equal(Now.UtcDateTime, parameters["to"]);
    }

    [Fact]
    public void Build_WithExplicitFromTo_UsesThoseInsteadOfDefault()
    {
        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero);

        var result = ExceptionFilterSqlBuilder.Build(new ExceptionFilter { From = from, To = to }, Now);

        var parameters = result.Parameters.ToDictionary();
        Assert.Equal(from.UtcDateTime, parameters["from"]);
        Assert.Equal(to.UtcDateTime, parameters["to"]);
    }

    [Fact]
    public void Build_WithServices_AddsInClause_AndArrayParameter()
    {
        var result = ExceptionFilterSqlBuilder.Build(new ExceptionFilter { Services = ["payments-api", "checkout-api"] }, Now);

        Assert.Contains("ServiceName IN {services:Array(String)}", result.WhereSql);
        Assert.Equal(["payments-api", "checkout-api"], (string[])result.Parameters.ToDictionary()["services"]!);
    }

    [Fact]
    public void Build_WithNoServices_OmitsInClause()
    {
        var result = ExceptionFilterSqlBuilder.Build(new ExceptionFilter(), Now);

        Assert.DoesNotContain("ServiceName", result.WhereSql);
    }
}
