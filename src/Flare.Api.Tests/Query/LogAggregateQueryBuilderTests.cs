using Flare.Api.Model;
using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Query;

public class LogAggregateQueryBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Build_WithNullFilter_DoesNotThrow()
    {
        // See LogSearchQueryBuilderTests' equivalent test: System.Text.Json overwrites
        // request.Filter's default back to null when "filter" is absent from the body.
        var result = LogAggregateQueryBuilder.Build(
            new LogAggregateRequest { BucketWidthSeconds = 60, Filter = null! }, Now);

        Assert.Contains("WHERE Timestamp >=", result.Sql);
    }

    [Fact]
    public void Build_NoGroupBy_OmitsGroupKeyColumn_AndGroupsByBucketOnly()
    {
        var result = LogAggregateQueryBuilder.Build(
            new LogAggregateRequest { BucketWidthSeconds = 60 }, Now);

        Assert.False(result.HasGroupKey);
        Assert.DoesNotContain("AS GroupKey", result.Sql);
        Assert.Contains("GROUP BY BucketStart", result.Sql);
        Assert.DoesNotContain("GROUP BY BucketStart,", result.Sql);
    }

    [Fact]
    public void Build_GroupByService_SelectsServiceNameAsGroupKey()
    {
        var result = LogAggregateQueryBuilder.Build(
            new LogAggregateRequest { BucketWidthSeconds = 60, GroupBy = LogAggregateGroupBy.Service }, Now);

        Assert.True(result.HasGroupKey);
        Assert.Contains("ServiceName AS GroupKey", result.Sql);
        Assert.Contains("GROUP BY BucketStart, ServiceName", result.Sql);
    }

    [Fact]
    public void Build_GroupByLevel_SelectsSeverityTextAsGroupKey()
    {
        var result = LogAggregateQueryBuilder.Build(
            new LogAggregateRequest { BucketWidthSeconds = 60, GroupBy = LogAggregateGroupBy.Level }, Now);

        Assert.True(result.HasGroupKey);
        Assert.Contains("SeverityText AS GroupKey", result.Sql);
        Assert.Contains("GROUP BY BucketStart, SeverityText", result.Sql);
    }

    [Theory]
    [InlineData(AttributeBag.Log, "LogAttributes")]
    [InlineData(AttributeBag.Resource, "ResourceAttributes")]
    [InlineData(AttributeBag.Scope, "ScopeAttributes")]
    public void Build_GroupByAttribute_GroupsByBoundKeyInTheRightBag(AttributeBag bag, string column)
    {
        var result = LogAggregateQueryBuilder.Build(
            new LogAggregateRequest
            {
                BucketWidthSeconds = 60,
                GroupBy = LogAggregateGroupBy.Attribute,
                GroupByAttributeBag = bag,
                GroupByAttributeKey = "http.route",
            }, Now);

        Assert.True(result.HasGroupKey);
        Assert.Contains($"if(has(TopGroupValues, {column}[{{groupByKey:String}}]), {column}[{{groupByKey:String}}], NULL) AS GroupKey", result.Sql);
        Assert.Contains("GROUP BY BucketStart, GroupKey", result.Sql);
        Assert.Equal("http.route", result.Parameters.ToDictionary()["groupByKey"]);
        Assert.DoesNotContain("http.route", result.Sql);
    }

    [Fact]
    public void Build_GroupByAttribute_CapsSeriesToTopValuesUnderTheSameFilter()
    {
        var result = LogAggregateQueryBuilder.Build(
            new LogAggregateRequest
            {
                Filter = new LogFilter { Services = ["checkout"] },
                BucketWidthSeconds = 60,
                GroupBy = LogAggregateGroupBy.Attribute,
                GroupByAttributeKey = "http.route",
            }, Now);

        Assert.Contains($"LIMIT {LogAggregateQueryBuilder.AttributeGroupLimit}", result.Sql);
        Assert.Contains("ORDER BY count() DESC", result.Sql);
        // The top-values subquery must see the same WHERE as the bucketed query, or the
        // "top" values would be picked from a different population than the one charted.
        var whereStart = result.Sql.IndexOf("WHERE ", StringComparison.Ordinal);
        var whereLine = result.Sql[whereStart..result.Sql.IndexOf('\n', whereStart)].Trim();
        Assert.Equal(2, CountOccurrences(result.Sql, whereLine));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Build_GroupByAttribute_WithoutKey_Throws(string? key)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            LogAggregateQueryBuilder.Build(
                new LogAggregateRequest { BucketWidthSeconds = 60, GroupBy = LogAggregateGroupBy.Attribute, GroupByAttributeKey = key }, Now));
    }

    [Fact]
    public void Build_NonAttributeGroupBy_IgnoresAttributeKey()
    {
        var result = LogAggregateQueryBuilder.Build(
            new LogAggregateRequest { BucketWidthSeconds = 60, GroupBy = LogAggregateGroupBy.Service, GroupByAttributeKey = "http.route" }, Now);

        Assert.DoesNotContain("TopGroupValues", result.Sql);
        Assert.False(result.Parameters.ToDictionary().ContainsKey("groupByKey"));
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        for (var i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0; i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    [Fact]
    public void Build_BindsBucketWidthAsParameter()
    {
        var result = LogAggregateQueryBuilder.Build(
            new LogAggregateRequest { BucketWidthSeconds = 300 }, Now);

        Assert.Contains("toStartOfInterval(Timestamp, INTERVAL {bucketWidth:UInt32} SECOND)", result.Sql);
        Assert.Equal(300, result.Parameters.ToDictionary()["bucketWidth"]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Build_NonPositiveBucketWidth_Throws(int bucketWidth)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            LogAggregateQueryBuilder.Build(new LogAggregateRequest { BucketWidthSeconds = bucketWidth }, Now));
    }
}
