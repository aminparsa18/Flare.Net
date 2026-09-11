using Flare.Api.Model;
using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Query;

public class LogFilterSqlBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Build_WithNoFilters_OnlyBoundsTimeRange_UsingDefaultLookback()
    {
        var result = LogFilterSqlBuilder.Build(new LogFilter(), Now);

        Assert.Equal("Timestamp >= {from:DateTime64(9)} AND Timestamp < {to:DateTime64(9)}", result.WhereSql);

        var parameters = result.Parameters.ToDictionary();
        Assert.Equal((Now - LogFilterSqlBuilder.DefaultLookback).UtcDateTime, parameters["from"]);
        Assert.Equal(Now.UtcDateTime, parameters["to"]);
    }

    [Fact]
    public void Build_WithExplicitFromTo_UsesThoseInsteadOfDefault()
    {
        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero);

        var result = LogFilterSqlBuilder.Build(new LogFilter { From = from, To = to }, Now);

        var parameters = result.Parameters.ToDictionary();
        Assert.Equal(from.UtcDateTime, parameters["from"]);
        Assert.Equal(to.UtcDateTime, parameters["to"]);
    }

    [Fact]
    public void Build_WithServices_AddsInClause_AndArrayParameter()
    {
        var result = LogFilterSqlBuilder.Build(new LogFilter { Services = ["flare-ingest", "payments-api"] }, Now);

        Assert.Contains("ServiceName IN {services:Array(String)}", result.WhereSql);
        Assert.Equal(["flare-ingest", "payments-api"], (string[])result.Parameters.ToDictionary()["services"]!);
    }

    [Fact]
    public void Build_WithSeverityNumbers_AddsInClause_AndArrayParameter()
    {
        var result = LogFilterSqlBuilder.Build(new LogFilter { SeverityNumbers = [13, 17, 21] }, Now);

        Assert.Contains("SeverityNumber IN {severities:Array(UInt8)}", result.WhereSql);
        Assert.Equal(new byte[] { 13, 17, 21 }, (byte[])result.Parameters.ToDictionary()["severities"]!);
    }

    [Fact]
    public void Build_WithTraceId_AddsEqualityClause()
    {
        var result = LogFilterSqlBuilder.Build(new LogFilter { TraceId = "0102030405060708090a0b0c0d0e0f10" }, Now);

        Assert.Contains("TraceId = {traceId:String}", result.WhereSql);
        Assert.Equal("0102030405060708090a0b0c0d0e0f10", result.Parameters.ToDictionary()["traceId"]);
    }

    [Fact]
    public void Build_WithSpanId_AddsEqualityClause()
    {
        var result = LogFilterSqlBuilder.Build(new LogFilter { SpanId = "0102030405060708" }, Now);

        Assert.Contains("SpanId = {spanId:String}", result.WhereSql);
        Assert.Equal("0102030405060708", result.Parameters.ToDictionary()["spanId"]);
    }

    [Fact]
    public void Build_WithPatternId_AddsEqualityClause()
    {
        var result = LogFilterSqlBuilder.Build(new LogFilter { PatternId = "a82db7c88f594553" }, Now);

        Assert.Contains("PatternId = {patternId:String}", result.WhereSql);
        Assert.Equal("a82db7c88f594553", result.Parameters.ToDictionary()["patternId"]);
    }

    [Fact]
    public void Build_WithSearch_WrapsTermInWildcards_ForIlike()
    {
        var result = LogFilterSqlBuilder.Build(new LogFilter { Search = "boom" }, Now);

        Assert.Contains("Body ILIKE {search:String}", result.WhereSql);
        Assert.Equal("%boom%", result.Parameters.ToDictionary()["search"]);
    }

    [Theory]
    [InlineData(AttributeBag.Log, "LogAttributes")]
    [InlineData(AttributeBag.Resource, "ResourceAttributes")]
    [InlineData(AttributeBag.Scope, "ScopeAttributes")]
    public void Build_WithAttributeFilter_UsesTheRightBagColumn(AttributeBag bag, string expectedColumn)
    {
        var result = LogFilterSqlBuilder.Build(
            new LogFilter { Attributes = [new AttributeFilter { Bag = bag, Key = "http.method", Value = "GET" }] },
            Now);

        Assert.Contains($"{expectedColumn}[{{attrKey0:String}}] = {{attrValue0:String}}", result.WhereSql);
        var parameters = result.Parameters.ToDictionary();
        Assert.Equal("http.method", parameters["attrKey0"]);
        Assert.Equal("GET", parameters["attrValue0"]);
    }

    [Fact]
    public void Build_WithMultipleAttributeFilters_UsesDistinctParameterNamesPerIndex()
    {
        var result = LogFilterSqlBuilder.Build(
            new LogFilter
            {
                Attributes =
                [
                    new AttributeFilter { Key = "http.method", Value = "GET" },
                    new AttributeFilter { Key = "http.status_code", Value = "500" },
                ],
            },
            Now);

        var parameters = result.Parameters.ToDictionary();
        Assert.Equal("http.method", parameters["attrKey0"]);
        Assert.Equal("http.status_code", parameters["attrKey1"]);
        Assert.Contains("LogAttributes[{attrKey0:String}] = {attrValue0:String}", result.WhereSql);
        Assert.Contains("LogAttributes[{attrKey1:String}] = {attrValue1:String}", result.WhereSql);
    }

    [Fact]
    public void Build_WithNotEqualsOperator_GuardsWithMapContains_AndBindsValue()
    {
        var result = LogFilterSqlBuilder.Build(
            new LogFilter { Attributes = [new AttributeFilter { Key = "http.method", Value = "GET", Operator = AttributeFilterOperator.NotEquals }] },
            Now);

        Assert.Contains(
            "NOT (mapContains(LogAttributes, {attrKey0:String}) AND LogAttributes[{attrKey0:String}] = {attrValue0:String})",
            result.WhereSql);
        var parameters = result.Parameters.ToDictionary();
        Assert.Equal("http.method", parameters["attrKey0"]);
        Assert.Equal("GET", parameters["attrValue0"]);
    }

    [Fact]
    public void Build_WithExistsOperator_UsesMapContains_AndDoesNotBindValue()
    {
        var result = LogFilterSqlBuilder.Build(
            new LogFilter { Attributes = [new AttributeFilter { Key = "tenant.id", Value = "", Operator = AttributeFilterOperator.Exists }] },
            Now);

        Assert.Contains("mapContains(LogAttributes, {attrKey0:String})", result.WhereSql);
        Assert.DoesNotContain("attrValue0", result.WhereSql);
        Assert.False(result.Parameters.ToDictionary().ContainsKey("attrValue0"));
    }

    [Fact]
    public void Build_WithAbsentOperator_NegatesMapContains_AndDoesNotBindValue()
    {
        var result = LogFilterSqlBuilder.Build(
            new LogFilter { Attributes = [new AttributeFilter { Key = "tenant.id", Value = "", Operator = AttributeFilterOperator.Absent }] },
            Now);

        Assert.Contains("NOT mapContains(LogAttributes, {attrKey0:String})", result.WhereSql);
        Assert.False(result.Parameters.ToDictionary().ContainsKey("attrValue0"));
    }

    [Fact]
    public void Build_WithRegexOperator_GuardsWithMapContains_AndUsesMatch()
    {
        var result = LogFilterSqlBuilder.Build(
            new LogFilter { Attributes = [new AttributeFilter { Key = "http.route", Value = "^/api/v[0-9]+/users", Operator = AttributeFilterOperator.Regex }] },
            Now);

        Assert.Contains(
            "(mapContains(LogAttributes, {attrKey0:String}) AND match(LogAttributes[{attrKey0:String}], {attrValue0:String}))",
            result.WhereSql);
        var parameters = result.Parameters.ToDictionary();
        Assert.Equal("http.route", parameters["attrKey0"]);
        Assert.Equal("^/api/v[0-9]+/users", parameters["attrValue0"]);
    }

    [Fact]
    public void Build_WithNotRegexOperator_GuardsWithMapContains_AndNegatesMatch()
    {
        var result = LogFilterSqlBuilder.Build(
            new LogFilter { Attributes = [new AttributeFilter { Key = "http.route", Value = "^/internal/", Operator = AttributeFilterOperator.NotRegex }] },
            Now);

        Assert.Contains(
            "NOT (mapContains(LogAttributes, {attrKey0:String}) AND match(LogAttributes[{attrKey0:String}], {attrValue0:String}))",
            result.WhereSql);
        var parameters = result.Parameters.ToDictionary();
        Assert.Equal("http.route", parameters["attrKey0"]);
        Assert.Equal("^/internal/", parameters["attrValue0"]);
    }

    [Fact]
    public void Build_WithInOperator_GuardsWithMapContains_AndBindsValuesArray()
    {
        var result = LogFilterSqlBuilder.Build(
            new LogFilter { Attributes = [new AttributeFilter { Key = "http.status_code", Value = "", Operator = AttributeFilterOperator.In, Values = ["500", "502", "503"] }] },
            Now);

        Assert.Contains(
            "(mapContains(LogAttributes, {attrKey0:String}) AND LogAttributes[{attrKey0:String}] IN {attrValues0:Array(String)})",
            result.WhereSql);
        var parameters = result.Parameters.ToDictionary();
        Assert.Equal("http.status_code", parameters["attrKey0"]);
        Assert.Equal(["500", "502", "503"], (string[])parameters["attrValues0"]!);
    }

    [Fact]
    public void Build_WithInOperator_AndNullValues_BindsEmptyArray()
    {
        var result = LogFilterSqlBuilder.Build(
            new LogFilter { Attributes = [new AttributeFilter { Key = "http.status_code", Value = "", Operator = AttributeFilterOperator.In }] },
            Now);

        Assert.Equal(Array.Empty<string>(), (string[])result.Parameters.ToDictionary()["attrValues0"]!);
    }

    [Fact]
    public void Build_WithNotInOperator_GuardsWithMapContains_AndNegatesInClause()
    {
        var result = LogFilterSqlBuilder.Build(
            new LogFilter { Attributes = [new AttributeFilter { Key = "http.status_code", Value = "", Operator = AttributeFilterOperator.NotIn, Values = ["200", "201"] }] },
            Now);

        Assert.Contains(
            "NOT (mapContains(LogAttributes, {attrKey0:String}) AND LogAttributes[{attrKey0:String}] IN {attrValues0:Array(String)})",
            result.WhereSql);
        var parameters = result.Parameters.ToDictionary();
        Assert.Equal("http.status_code", parameters["attrKey0"]);
        Assert.Equal(["200", "201"], (string[])parameters["attrValues0"]!);
    }
}
