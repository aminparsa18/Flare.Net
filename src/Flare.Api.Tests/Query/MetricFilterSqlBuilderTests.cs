using Flare.Api.Model;
using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Query;

public class MetricFilterSqlBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Build_WithNoFilters_OnlyBoundsTimeRange_UsingDefaultLookback()
    {
        var result = MetricFilterSqlBuilder.Build(new MetricFilter(), Now);

        Assert.Equal("Time >= {from:DateTime64(9)} AND Time < {to:DateTime64(9)}", result.WhereSql);

        var parameters = result.Parameters.ToDictionary();
        Assert.Equal((Now - MetricFilterSqlBuilder.DefaultLookback).UtcDateTime, parameters["from"]);
        Assert.Equal(Now.UtcDateTime, parameters["to"]);
    }

    [Fact]
    public void Build_WithExplicitFromTo_UsesThoseInsteadOfDefault()
    {
        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero);

        var result = MetricFilterSqlBuilder.Build(new MetricFilter { From = from, To = to }, Now);

        var parameters = result.Parameters.ToDictionary();
        Assert.Equal(from.UtcDateTime, parameters["from"]);
        Assert.Equal(to.UtcDateTime, parameters["to"]);
    }

    [Fact]
    public void Build_WithServices_AddsInClause_AndArrayParameter()
    {
        var result = MetricFilterSqlBuilder.Build(new MetricFilter { Services = ["payments-api", "checkout-api"] }, Now);

        Assert.Contains("ServiceName IN {services:Array(String)}", result.WhereSql);
        Assert.Equal(["payments-api", "checkout-api"], (string[])result.Parameters.ToDictionary()["services"]!);
    }

    [Fact]
    public void Build_WithAttributeFilter_UsesDataPointAttributesColumn()
    {
        var result = MetricFilterSqlBuilder.Build(
            new MetricFilter { Attributes = [new MetricAttributeFilter { Key = "http.route", Value = "/checkout" }] },
            Now);

        Assert.Contains("DataPointAttributes[{attrKey0:String}] = {attrValue0:String}", result.WhereSql);
        var parameters = result.Parameters.ToDictionary();
        Assert.Equal("http.route", parameters["attrKey0"]);
        Assert.Equal("/checkout", parameters["attrValue0"]);
    }

    [Fact]
    public void Build_WithMultipleAttributeFilters_UsesDistinctParameterNamesPerIndex()
    {
        var result = MetricFilterSqlBuilder.Build(
            new MetricFilter
            {
                Attributes =
                [
                    new MetricAttributeFilter { Key = "http.route", Value = "/checkout" },
                    new MetricAttributeFilter { Key = "http.method", Value = "POST" },
                ],
            },
            Now);

        var parameters = result.Parameters.ToDictionary();
        Assert.Equal("http.route", parameters["attrKey0"]);
        Assert.Equal("http.method", parameters["attrKey1"]);
        Assert.Contains("DataPointAttributes[{attrKey0:String}] = {attrValue0:String}", result.WhereSql);
        Assert.Contains("DataPointAttributes[{attrKey1:String}] = {attrValue1:String}", result.WhereSql);
    }
}
