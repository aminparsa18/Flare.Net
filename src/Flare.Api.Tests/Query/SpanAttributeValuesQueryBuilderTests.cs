using Flare.Api.Model;
using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Query;

public class SpanAttributeValuesQueryBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);

    private static SpanAttributeValuesRequest Request(
        string key = "http.route",
        SpanAttributeBag bag = SpanAttributeBag.Span,
        string? prefix = null,
        int limit = 25,
        SpanFilter? filter = null) =>
        new() { Key = key, Bag = bag, Prefix = prefix, Limit = limit, Filter = filter ?? new SpanFilter() };

    [Fact]
    public void Build_WithNullFilter_DoesNotThrow()
    {
        var result = SpanAttributeValuesQueryBuilder.Build(Request(filter: null!), Now);

        Assert.Contains("WHERE StartTime >=", result.Sql);
    }

    [Fact]
    public void Build_SelectsFromSpansTable_AndGroupsByValue()
    {
        var result = SpanAttributeValuesQueryBuilder.Build(Request(key: "http.route", bag: SpanAttributeBag.Span), Now);

        Assert.Contains("SELECT SpanAttributes[{valuesKey:String}] AS Value", result.Sql);
        Assert.Contains("FROM spans", result.Sql);
        Assert.Contains("GROUP BY Value", result.Sql);
        Assert.Contains("ORDER BY Cnt DESC", result.Sql);
        Assert.Contains("LIMIT {valuesLimit:UInt32}", result.Sql);
        var parameters = result.Parameters.ToDictionary();
        Assert.Equal("http.route", parameters["valuesKey"]);
        Assert.Equal(25, parameters["valuesLimit"]);
    }

    [Theory]
    [InlineData(SpanAttributeBag.Resource, "ResourceAttributes")]
    [InlineData(SpanAttributeBag.Scope, "ScopeAttributes")]
    [InlineData(SpanAttributeBag.Span, "SpanAttributes")]
    public void Build_ResolvesColumnPerBag(SpanAttributeBag bag, string column)
    {
        var result = SpanAttributeValuesQueryBuilder.Build(Request(bag: bag), Now);

        Assert.Contains($"{column}[{{valuesKey:String}}] AS Value", result.Sql);
        Assert.Contains($"mapContains({column}, {{valuesKey:String}})", result.Sql);
    }

    [Fact]
    public void Build_WithPrefix_AddsIlikeClause_AndBindsWildcardedParameter()
    {
        var result = SpanAttributeValuesQueryBuilder.Build(Request(prefix: "GET"), Now);

        Assert.Contains("ILIKE {valuesPrefix:String}", result.Sql);
        var parameters = result.Parameters.ToDictionary();
        Assert.Equal("%GET%", parameters["valuesPrefix"]);
    }

    [Fact]
    public void Build_WithoutPrefix_OmitsIlikeClause()
    {
        var result = SpanAttributeValuesQueryBuilder.Build(Request(prefix: null), Now);

        Assert.DoesNotContain("ILIKE", result.Sql);
        Assert.DoesNotContain("valuesPrefix", result.Parameters.ToDictionary().Keys);
    }

    [Fact]
    public void Build_EmptyKey_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SpanAttributeValuesQueryBuilder.Build(Request(key: ""), Now));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Build_NonPositiveLimit_Throws(int limit)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SpanAttributeValuesQueryBuilder.Build(Request(limit: limit), Now));
    }

    [Theory]
    [InlineData(SpanValuesField.Service, "ServiceName")]
    [InlineData(SpanValuesField.Status, "toString(StatusCode)")]
    [InlineData(SpanValuesField.Kind, "toString(Kind)")]
    [InlineData(SpanValuesField.Name, "Name")]
    public void Build_BuiltInField_SelectsColumn_WithoutKeyOrMapContains(SpanValuesField field, string valueSql)
    {
        var result = SpanAttributeValuesQueryBuilder.Build(new SpanAttributeValuesRequest { Field = field }, Now);

        Assert.Contains($"SELECT {valueSql} AS Value", result.Sql);
        Assert.DoesNotContain("mapContains", result.Sql);
        Assert.DoesNotContain("valuesKey", result.Parameters.ToDictionary().Keys);
    }

    [Fact]
    public void Build_DurationBucket_MapsEachBoundToItsLowerBound_WithOpenEndedLastBucket()
    {
        var result = SpanAttributeValuesQueryBuilder.Build(new SpanAttributeValuesRequest { Field = SpanValuesField.DurationBucket }, Now);

        Assert.Contains(
            "SELECT toString(multiIf(DurationNano < 1000000, 0, DurationNano < 10000000, 1000000, DurationNano < 100000000, 10000000, " +
            "DurationNano < 500000000, 100000000, DurationNano < 1000000000, 500000000, DurationNano < 5000000000, 1000000000, 5000000000)) AS Value",
            result.Sql);
    }
}
