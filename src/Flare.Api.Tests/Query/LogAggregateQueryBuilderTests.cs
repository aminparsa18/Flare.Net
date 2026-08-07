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
