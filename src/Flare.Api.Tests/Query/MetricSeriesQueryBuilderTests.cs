using Flare.Api.Model;
using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Query;

public class MetricSeriesQueryBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Build_WithNullFilter_DoesNotThrow()
    {
        // Same System.Text.Json init-only-property caveat LogAggregateQueryBuilderTests
        // guards against.
        var result = MetricSeriesQueryBuilder.Build(
            new MetricQueryRequest { MetricName = "process.threads", Type = MetricPointType.Gauge, BucketWidthSeconds = 60, Filter = null! }, Now);

        Assert.Contains("WHERE MetricName = {metricName:String} AND Time >=", result.Sql);
    }

    [Fact]
    public void Build_Gauge_SelectsFromGaugeTable_WithAverage()
    {
        var result = MetricSeriesQueryBuilder.Build(
            new MetricQueryRequest { MetricName = "process.threads", Type = MetricPointType.Gauge, BucketWidthSeconds = 60 }, Now);

        Assert.Contains("FROM metrics_gauge", result.Sql);
        Assert.Contains("avg(Value) AS Value", result.Sql);
        Assert.Equal(MetricPointType.Gauge, result.Type);
    }

    [Fact]
    public void Build_ExponentialHistogram_GroupsByScale_AndOrdersScaleLast()
    {
        var result = MetricSeriesQueryBuilder.Build(
            new MetricQueryRequest { MetricName = "http.server.request.duration", Type = MetricPointType.ExponentialHistogram, BucketWidthSeconds = 60 }, Now);

        Assert.Contains("FROM metrics_exponential_histogram", result.Sql);
        Assert.Contains(HistogramTemporalitySql.ExponentialAggregates, result.Sql);
        Assert.Contains("FROM contributions\nGROUP BY BucketStart, ServiceName, SeriesKey, Scale", result.Sql);
        // The windowed previous-row values are per full series identity, not per SeriesKey.
        Assert.Contains("WINDOW w AS (PARTITION BY ServiceName, toString(DataPointAttributes) ORDER BY Time)", result.Sql);
        // Scale last, so MetricQueryService sees one bucket's per-scale rows back to back.
        Assert.EndsWith("ORDER BY ServiceName, SeriesKey, BucketStart, Scale", result.Sql);
        // Same whole-window magnitude as explicit-bucket Histogram for the top-N ranking.
        Assert.Contains("sum(Count) AS RankValue", result.Sql);
        Assert.Equal(MetricPointType.ExponentialHistogram, result.Type);
    }

    [Fact]
    public void Build_Sum_SelectsFromSumTable_WithWindowedIncrease()
    {
        var result = MetricSeriesQueryBuilder.Build(
            new MetricQueryRequest { MetricName = "http.server.request.count", Type = MetricPointType.Sum, BucketWidthSeconds = 60 }, Now);

        Assert.Contains("FROM metrics_sum", result.Sql);
        // The CTE-based per-bucket increase() shape (ADR-0035), not the old flat
        // max(Value) - min(Value) GROUP BY.
        Assert.Contains("WITH ranked AS (", result.Sql);
        Assert.Contains("row_number() OVER (PARTITION BY ServiceName, toString(DataPointAttributes) ORDER BY Time) AS SeriesRowNum", result.Sql);
        Assert.Contains("Value - lagInFrame(Value) OVER (PARTITION BY ServiceName, toString(DataPointAttributes) ORDER BY Time) AS RawDelta", result.Sql);
        Assert.Contains("sum(multiIf(", result.Sql);
        Assert.Contains("AggregationTemporality = 'AGGREGATION_TEMPORALITY_DELTA', Value", result.Sql);
        Assert.Contains("SeriesRowNum = 1, 0", result.Sql);
        Assert.Contains("IsMonotonic = 0, RawDelta", result.Sql);
        Assert.Contains("RawDelta < 0, Value", result.Sql);
        Assert.Contains(")) AS Value, count() AS Count", result.Sql);
    }

    [Fact]
    public void Build_Histogram_SumsTemporalityAwareContributions_PerBucket()
    {
        var result = MetricSeriesQueryBuilder.Build(
            new MetricQueryRequest { MetricName = "http.server.request.duration", Type = MetricPointType.Histogram, BucketWidthSeconds = 60 }, Now);

        Assert.Contains("FROM metrics_histogram", result.Sql);
        Assert.Contains(HistogramTemporalitySql.ExplicitAggregates, result.Sql);
        Assert.Contains("FROM ranked\nGROUP BY BucketStart, ServiceName, SeriesKey", result.Sql);
        Assert.Contains("WINDOW w AS (PARTITION BY ServiceName, toString(DataPointAttributes) ORDER BY Time)", result.Sql);
        // Summing raw cumulative Count/BucketCounts over-counted (ADR-0060).
        Assert.DoesNotContain("sumForEach(BucketCounts) AS", result.Sql);
    }

    [Fact]
    public void Build_GroupsByBucketStartServiceNameAndSeriesKey_OrderedByServiceThenSeriesThenBucket()
    {
        var result = MetricSeriesQueryBuilder.Build(
            new MetricQueryRequest { MetricName = "process.threads", Type = MetricPointType.Gauge, BucketWidthSeconds = 60 }, Now);

        Assert.Contains("toString(DataPointAttributes) AS SeriesKey", result.Sql);
        Assert.Contains("any(DataPointAttributes) AS SeriesAttributes", result.Sql);
        // ServiceName is part of the group key, not just the series key - see the
        // builder's remarks: two services sharing the same (or no) DataPointAttributes
        // must not merge into one series.
        Assert.Contains("GROUP BY BucketStart, ServiceName, SeriesKey", result.Sql);
        Assert.Contains("ORDER BY ServiceName, SeriesKey, BucketStart", result.Sql);
    }

    [Fact]
    public void Build_BindsMetricNameAndBucketWidth()
    {
        var result = MetricSeriesQueryBuilder.Build(
            new MetricQueryRequest { MetricName = "process.threads", Type = MetricPointType.Gauge, BucketWidthSeconds = 300 }, Now);

        Assert.Contains("toStartOfInterval(Time, INTERVAL {bucketWidth:UInt32} SECOND)", result.Sql);
        var parameters = result.Parameters.ToDictionary();
        Assert.Equal("process.threads", parameters["metricName"]);
        Assert.Equal(300, parameters["bucketWidth"]);
    }

    [Fact]
    public void Build_AppliesFilter_ServicesAndAttributes()
    {
        var result = MetricSeriesQueryBuilder.Build(
            new MetricQueryRequest
            {
                MetricName = "process.threads",
                Type = MetricPointType.Gauge,
                BucketWidthSeconds = 60,
                Filter = new MetricFilter
                {
                    Services = ["payments-api"],
                    Attributes = [new MetricAttributeFilter { Key = "state", Value = "active" }],
                },
            },
            Now);

        Assert.Contains("ServiceName IN {services:Array(String)}", result.Sql);
        Assert.Contains("DataPointAttributes[{attrKey0:String}] = {attrValue0:String}", result.Sql);
    }

    [Fact]
    public void Build_WithoutGroupByAttributeKey_UsesFullAttributeMapAsSeriesKey()
    {
        var result = MetricSeriesQueryBuilder.Build(
            new MetricQueryRequest { MetricName = "process.threads", Type = MetricPointType.Gauge, BucketWidthSeconds = 60 }, Now);

        Assert.Contains("toString(DataPointAttributes) AS SeriesKey", result.Sql);
        Assert.Contains("any(DataPointAttributes) AS SeriesAttributes", result.Sql);
        Assert.DoesNotContain("groupByKey", result.Sql);
        Assert.False(result.Parameters.ToDictionary().ContainsKey("groupByKey"));
    }

    [Fact]
    public void Build_WithGroupByAttributeKey_UsesAttributeValueAsSeriesKey()
    {
        var result = MetricSeriesQueryBuilder.Build(
            new MetricQueryRequest
            {
                MetricName = "dotnet.exceptions",
                Type = MetricPointType.Sum,
                BucketWidthSeconds = 60,
                GroupByAttributeKey = "error.type",
            },
            Now);

        Assert.Contains("DataPointAttributes[{groupByKey:String}] AS SeriesKey", result.Sql);
        Assert.Contains("map({groupByKey:String}, any(DataPointAttributes[{groupByKey:String}])) AS SeriesAttributes", result.Sql);
        // Grouping mode collapses the *output* SeriesKey, but the window functions computing
        // each counter's per-row delta must still partition by the full, ungrouped attribute
        // map - windowing over the collapsed key would diff two unrelated counters against
        // each other (see the builder's "Sum query shape" remarks).
        Assert.Contains("row_number() OVER (PARTITION BY ServiceName, toString(DataPointAttributes) ORDER BY Time) AS SeriesRowNum", result.Sql);
        Assert.Contains("GROUP BY BucketStart, ServiceName, SeriesKey", result.Sql);
        Assert.Contains("ORDER BY ServiceName, SeriesKey, BucketStart", result.Sql);
    }

    [Fact]
    public void Build_WithGroupByAttributeKey_BindsGroupByKeyParameter()
    {
        var result = MetricSeriesQueryBuilder.Build(
            new MetricQueryRequest
            {
                MetricName = "dotnet.exceptions",
                Type = MetricPointType.Sum,
                BucketWidthSeconds = 60,
                GroupByAttributeKey = "error.type",
            },
            Now);

        var parameters = result.Parameters.ToDictionary();
        Assert.Equal("error.type", parameters["groupByKey"]);
    }

    [Fact]
    public void Build_WithEmptyStringGroupByAttributeKey_TreatedAsUngrouped()
    {
        var result = MetricSeriesQueryBuilder.Build(
            new MetricQueryRequest
            {
                MetricName = "process.threads",
                Type = MetricPointType.Gauge,
                BucketWidthSeconds = 60,
                GroupByAttributeKey = "",
            },
            Now);

        Assert.Contains("toString(DataPointAttributes) AS SeriesKey", result.Sql);
        Assert.Contains("any(DataPointAttributes) AS SeriesAttributes", result.Sql);
        Assert.False(result.Parameters.ToDictionary().ContainsKey("groupByKey"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Build_NonPositiveBucketWidth_Throws(int bucketWidth)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MetricSeriesQueryBuilder.Build(
                new MetricQueryRequest { MetricName = "process.threads", Type = MetricPointType.Gauge, BucketWidthSeconds = bucketWidth }, Now));
    }

    [Fact]
    public void Build_WithoutTopN_CapsSeriesAtDefault()
    {
        var result = MetricSeriesQueryBuilder.Build(
            new MetricQueryRequest { MetricName = "process.threads", Type = MetricPointType.Gauge, BucketWidthSeconds = 60 }, Now);

        Assert.Contains("AND (ServiceName, toString(DataPointAttributes)) IN (", result.Sql);
        Assert.Contains("LIMIT {topN:UInt32}", result.Sql);
        Assert.Equal((uint)MetricSeriesQueryBuilder.DefaultTopN, result.Parameters.ToDictionary()["topN"]);
    }

    [Theory]
    [InlineData(5, 5)]
    [InlineData(0, MetricSeriesQueryBuilder.DefaultTopN)]
    [InlineData(-1, MetricSeriesQueryBuilder.DefaultTopN)]
    [InlineData(10_000, 200)]
    public void Build_ClampsTopN(int? requested, int expected)
    {
        var result = MetricSeriesQueryBuilder.Build(
            new MetricQueryRequest { MetricName = "process.threads", Type = MetricPointType.Gauge, BucketWidthSeconds = 60, TopN = requested }, Now);

        Assert.Equal((uint)expected, result.Parameters.ToDictionary()["topN"]);
    }

    [Fact]
    public void Build_RanksSeriesByPerTypeMagnitude_Gauge()
    {
        var result = MetricSeriesQueryBuilder.Build(
            new MetricQueryRequest { MetricName = "process.threads", Type = MetricPointType.Gauge, BucketWidthSeconds = 60 }, Now);

        Assert.Contains("avg(Value) AS RankValue", result.Sql);
        Assert.Contains("ORDER BY RankValue DESC", result.Sql);
    }

    [Fact]
    public void Build_RanksSeriesByPerTypeMagnitude_Sum()
    {
        var result = MetricSeriesQueryBuilder.Build(
            new MetricQueryRequest { MetricName = "http.server.request.count", Type = MetricPointType.Sum, BucketWidthSeconds = 60 }, Now);

        Assert.Contains("max(Value) - min(Value) AS RankValue", result.Sql);
    }

    [Fact]
    public void Build_RanksSeriesByPerTypeMagnitude_Histogram()
    {
        var result = MetricSeriesQueryBuilder.Build(
            new MetricQueryRequest { MetricName = "http.server.request.duration", Type = MetricPointType.Histogram, BucketWidthSeconds = 60 }, Now);

        Assert.Contains("sum(Count) AS RankValue", result.Sql);
    }

    [Fact]
    public void Build_WithGroupByAttributeKey_RanksByGroupedSeriesKey()
    {
        var result = MetricSeriesQueryBuilder.Build(
            new MetricQueryRequest
            {
                MetricName = "dotnet.exceptions",
                Type = MetricPointType.Sum,
                BucketWidthSeconds = 60,
                GroupByAttributeKey = "error.type",
            },
            Now);

        Assert.Contains("AND (ServiceName, DataPointAttributes[{groupByKey:String}]) IN (", result.Sql);
        Assert.Contains("DataPointAttributes[{groupByKey:String}] AS SeriesKey, max(Value) - min(Value) AS RankValue", result.Sql);
    }

    [Fact]
    public void Build_WithoutHavingOperator_OmitsHavingClause()
    {
        var result = MetricSeriesQueryBuilder.Build(
            new MetricQueryRequest { MetricName = "process.threads", Type = MetricPointType.Gauge, BucketWidthSeconds = 60 }, Now);

        Assert.DoesNotContain("HAVING", result.Sql);
        Assert.False(result.Parameters.ToDictionary().ContainsKey("havingValue"));
    }

    [Theory]
    [InlineData(MetricHavingOperator.GreaterThan, ">")]
    [InlineData(MetricHavingOperator.GreaterThanOrEqual, ">=")]
    [InlineData(MetricHavingOperator.LessThan, "<")]
    [InlineData(MetricHavingOperator.LessThanOrEqual, "<=")]
    [InlineData(MetricHavingOperator.Equal, "=")]
    [InlineData(MetricHavingOperator.NotEqual, "!=")]
    public void Build_WithHavingOperator_AddsHavingClauseToRankingSubquery(MetricHavingOperator op, string sqlOp)
    {
        var result = MetricSeriesQueryBuilder.Build(
            new MetricQueryRequest
            {
                MetricName = "process.threads",
                Type = MetricPointType.Gauge,
                BucketWidthSeconds = 60,
                HavingOperator = op,
                HavingValue = 42.5,
            },
            Now);

        Assert.Contains($"GROUP BY ServiceName, SeriesKey\n  HAVING RankValue {sqlOp} {{havingValue:Float64}}\n  ORDER BY RankValue DESC", result.Sql);
        Assert.Equal(42.5, result.Parameters.ToDictionary()["havingValue"]);
    }

    [Fact]
    public void Build_WithHavingValueButNoOperator_OmitsHavingClause()
    {
        var result = MetricSeriesQueryBuilder.Build(
            new MetricQueryRequest { MetricName = "process.threads", Type = MetricPointType.Gauge, BucketWidthSeconds = 60, HavingValue = 10 }, Now);

        Assert.DoesNotContain("HAVING", result.Sql);
        Assert.False(result.Parameters.ToDictionary().ContainsKey("havingValue"));
    }

    [Fact]
    public void Build_WithHavingOperatorButNoValue_OmitsHavingClause()
    {
        var result = MetricSeriesQueryBuilder.Build(
            new MetricQueryRequest { MetricName = "process.threads", Type = MetricPointType.Gauge, BucketWidthSeconds = 60, HavingOperator = MetricHavingOperator.GreaterThan }, Now);

        Assert.DoesNotContain("HAVING", result.Sql);
        Assert.False(result.Parameters.ToDictionary().ContainsKey("havingValue"));
    }

    [Fact]
    public void Build_WithHavingOperator_AppliesToSumsWindowedRankingSubqueryToo()
    {
        var result = MetricSeriesQueryBuilder.Build(
            new MetricQueryRequest
            {
                MetricName = "http.server.request.count",
                Type = MetricPointType.Sum,
                BucketWidthSeconds = 60,
                HavingOperator = MetricHavingOperator.GreaterThanOrEqual,
                HavingValue = 100,
            },
            Now);

        Assert.Contains("HAVING RankValue >= {havingValue:Float64}", result.Sql);
        Assert.Equal(100d, result.Parameters.ToDictionary()["havingValue"]);
    }
}
