using Flare.Api.Model;
using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Query;

public class SpanFilterSqlBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Build_WithNoFilters_OnlyBoundsTimeRange_UsingDefaultLookback()
    {
        var result = SpanFilterSqlBuilder.Build(new SpanFilter(), Now);

        Assert.Equal("StartTime >= {from:DateTime64(9)} AND StartTime < {to:DateTime64(9)}", result.WhereSql);

        var parameters = result.Parameters.ToDictionary();
        Assert.Equal((Now - SpanFilterSqlBuilder.DefaultLookback).UtcDateTime, parameters["from"]);
        Assert.Equal(Now.UtcDateTime, parameters["to"]);
    }

    [Fact]
    public void Build_WithExplicitFromTo_UsesThoseInsteadOfDefault()
    {
        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero);

        var result = SpanFilterSqlBuilder.Build(new SpanFilter { From = from, To = to }, Now);

        var parameters = result.Parameters.ToDictionary();
        Assert.Equal(from.UtcDateTime, parameters["from"]);
        Assert.Equal(to.UtcDateTime, parameters["to"]);
    }

    [Fact]
    public void Build_WithServices_AddsInClause_AndArrayParameter()
    {
        var result = SpanFilterSqlBuilder.Build(new SpanFilter { Services = ["payments-api", "checkout-api"] }, Now);

        Assert.Contains("ServiceName IN {services:Array(String)}", result.WhereSql);
        Assert.Equal(["payments-api", "checkout-api"], (string[])result.Parameters.ToDictionary()["services"]!);
    }

    [Fact]
    public void Build_WithKinds_AddsInClause_AndArrayParameter()
    {
        var result = SpanFilterSqlBuilder.Build(new SpanFilter { Kinds = [2, 3] }, Now);

        Assert.Contains("Kind IN {kinds:Array(UInt8)}", result.WhereSql);
        Assert.Equal(new byte[] { 2, 3 }, (byte[])result.Parameters.ToDictionary()["kinds"]!);
    }

    [Fact]
    public void Build_WithStatusCodes_AddsInClause_AndArrayParameter_OfLabelStrings()
    {
        var result = SpanFilterSqlBuilder.Build(new SpanFilter { StatusCodes = ["STATUS_CODE_ERROR"] }, Now);

        Assert.Contains("StatusCode IN {statusCodes:Array(String)}", result.WhereSql);
        Assert.Equal(["STATUS_CODE_ERROR"], (string[])result.Parameters.ToDictionary()["statusCodes"]!);
    }

    [Fact]
    public void Build_WithTraceId_AddsEqualityClause()
    {
        var result = SpanFilterSqlBuilder.Build(new SpanFilter { TraceId = "0102030405060708090a0b0c0d0e0f10" }, Now);

        Assert.Contains("TraceId = {traceId:String}", result.WhereSql);
        Assert.Equal("0102030405060708090a0b0c0d0e0f10", result.Parameters.ToDictionary()["traceId"]);
    }

    [Fact]
    public void Build_WithRootSpansOnly_AddsParentSpanIdEmptyClause()
    {
        var result = SpanFilterSqlBuilder.Build(new SpanFilter { RootSpansOnly = true }, Now);

        Assert.Contains("ParentSpanId = ''", result.WhereSql);
    }

    [Fact]
    public void Build_WithoutRootSpansOnly_OmitsParentSpanIdClause()
    {
        var result = SpanFilterSqlBuilder.Build(new SpanFilter(), Now);

        Assert.DoesNotContain("ParentSpanId", result.WhereSql);
    }

    [Fact]
    public void Build_WithDurationRange_AddsBothBoundsAsSeparateClauses()
    {
        var result = SpanFilterSqlBuilder.Build(new SpanFilter { MinDurationNano = 1_000_000, MaxDurationNano = 500_000_000 }, Now);

        Assert.Contains("DurationNano >= {minDuration:UInt64}", result.WhereSql);
        Assert.Contains("DurationNano <= {maxDuration:UInt64}", result.WhereSql);
        var parameters = result.Parameters.ToDictionary();
        Assert.Equal(1_000_000UL, parameters["minDuration"]);
        Assert.Equal(500_000_000UL, parameters["maxDuration"]);
    }

    [Theory]
    [InlineData(SpanAttributeBag.Span, "SpanAttributes")]
    [InlineData(SpanAttributeBag.Resource, "ResourceAttributes")]
    [InlineData(SpanAttributeBag.Scope, "ScopeAttributes")]
    public void Build_WithAttributeFilter_UsesTheRightBagColumn(SpanAttributeBag bag, string expectedColumn)
    {
        var result = SpanFilterSqlBuilder.Build(
            new SpanFilter { Attributes = [new SpanAttributeFilter { Bag = bag, Key = "http.method", Value = "POST" }] },
            Now);

        Assert.Contains($"{expectedColumn}[{{attrKey0:String}}] = {{attrValue0:String}}", result.WhereSql);
        var parameters = result.Parameters.ToDictionary();
        Assert.Equal("http.method", parameters["attrKey0"]);
        Assert.Equal("POST", parameters["attrValue0"]);
    }

    [Fact]
    public void Build_WithMultipleAttributeFilters_UsesDistinctParameterNamesPerIndex()
    {
        var result = SpanFilterSqlBuilder.Build(
            new SpanFilter
            {
                Attributes =
                [
                    new SpanAttributeFilter { Key = "http.method", Value = "POST" },
                    new SpanAttributeFilter { Key = "http.status_code", Value = "500" },
                ],
            },
            Now);

        var parameters = result.Parameters.ToDictionary();
        Assert.Equal("http.method", parameters["attrKey0"]);
        Assert.Equal("http.status_code", parameters["attrKey1"]);
        Assert.Contains("SpanAttributes[{attrKey0:String}] = {attrValue0:String}", result.WhereSql);
        Assert.Contains("SpanAttributes[{attrKey1:String}] = {attrValue1:String}", result.WhereSql);
    }
}
