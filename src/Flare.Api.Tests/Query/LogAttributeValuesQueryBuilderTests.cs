using Flare.Api.Model;
using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Query;

public class LogAttributeValuesQueryBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);

    private static LogAttributeValuesRequest Request(
        string key = "http.route",
        AttributeBag bag = AttributeBag.Log,
        string? prefix = null,
        int limit = 25,
        LogFilter? filter = null) =>
        new() { Key = key, Bag = bag, Prefix = prefix, Limit = limit, Filter = filter ?? new LogFilter() };

    [Fact]
    public void Build_WithNullFilter_DoesNotThrow()
    {
        // See LogSearchQueryBuilderTests' equivalent test: System.Text.Json overwrites
        // request.Filter's default back to null when "filter" is absent from the body.
        var result = LogAttributeValuesQueryBuilder.Build(Request(filter: null!), Now);

        Assert.Contains("WHERE Timestamp >=", result.Sql);
    }

    [Fact]
    public void Build_SelectsFromTheRequestedBagsColumn_AndGroupsByValue()
    {
        var result = LogAttributeValuesQueryBuilder.Build(Request(key: "http.route", bag: AttributeBag.Log), Now);

        Assert.Contains("SELECT LogAttributes[{valuesKey:String}] AS Value", result.Sql);
        Assert.Contains("GROUP BY Value", result.Sql);
        Assert.Contains("ORDER BY Cnt DESC", result.Sql);
        Assert.Contains("LIMIT {valuesLimit:UInt32}", result.Sql);
        var parameters = result.Parameters.ToDictionary();
        Assert.Equal("http.route", parameters["valuesKey"]);
        Assert.Equal(25, parameters["valuesLimit"]);
    }

    [Theory]
    [InlineData(AttributeBag.Resource, "ResourceAttributes")]
    [InlineData(AttributeBag.Scope, "ScopeAttributes")]
    [InlineData(AttributeBag.Log, "LogAttributes")]
    public void Build_ResolvesColumnPerBag(AttributeBag bag, string column)
    {
        var result = LogAttributeValuesQueryBuilder.Build(Request(bag: bag), Now);

        Assert.Contains($"{column}[{{valuesKey:String}}] AS Value", result.Sql);
        Assert.Contains($"mapContains({column}, {{valuesKey:String}})", result.Sql);
    }

    [Fact]
    public void Build_RequiresKeyToBePresent_ViaMapContains()
    {
        var result = LogAttributeValuesQueryBuilder.Build(Request(), Now);

        Assert.Contains("mapContains(LogAttributes, {valuesKey:String})", result.Sql);
    }

    [Fact]
    public void Build_WithPrefix_AddsIlikeClause_AndBindsWildcardedParameter()
    {
        var result = LogAttributeValuesQueryBuilder.Build(Request(prefix: "GET"), Now);

        Assert.Contains("ILIKE {valuesPrefix:String}", result.Sql);
        var parameters = result.Parameters.ToDictionary();
        Assert.Equal("%GET%", parameters["valuesPrefix"]);
    }

    [Fact]
    public void Build_WithoutPrefix_OmitsIlikeClause()
    {
        var result = LogAttributeValuesQueryBuilder.Build(Request(prefix: null), Now);

        Assert.DoesNotContain("ILIKE", result.Sql);
        Assert.DoesNotContain("valuesPrefix", result.Parameters.ToDictionary().Keys);
    }

    [Fact]
    public void Build_EmptyKey_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LogAttributeValuesQueryBuilder.Build(Request(key: ""), Now));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Build_NonPositiveLimit_Throws(int limit)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LogAttributeValuesQueryBuilder.Build(Request(limit: limit), Now));
    }
}
