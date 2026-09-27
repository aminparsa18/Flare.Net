using Flare.Api.Model;
using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Query;

public class MetricCatalogQueryBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);

    private static readonly MetricCatalogDetailRequest Detail = new() { MetricName = "http.server.request.duration", Type = MetricPointType.Histogram };

    [Theory]
    [InlineData(null, MetricCatalogQueryBuilder.DefaultWindowMinutes)]
    [InlineData(0, MetricCatalogQueryBuilder.DefaultWindowMinutes)]
    [InlineData(-1, MetricCatalogQueryBuilder.DefaultWindowMinutes)]
    [InlineData(1, MetricCatalogQueryBuilder.MinWindowMinutes)]
    [InlineData(360, 360)]
    [InlineData(100_000, MetricCatalogQueryBuilder.MaxWindowMinutes)]
    public void ClampWindowMinutes_DefaultsAndClamps(int? requested, int expected)
    {
        Assert.Equal(expected, MetricCatalogQueryBuilder.ClampWindowMinutes(requested));
    }

    [Fact]
    public void BuildCatalog_UnionsAllFourTables_OneRowPerMetricName()
    {
        var result = MetricCatalogQueryBuilder.BuildCatalog(new MetricCatalogRequest(), 60, Now);

        Assert.Contains("FROM metrics_gauge", result.Sql);
        Assert.Contains("FROM metrics_sum", result.Sql);
        Assert.Contains("FROM metrics_histogram", result.Sql);
        Assert.Contains("FROM metrics_exponential_histogram", result.Sql);
        Assert.Equal(4, CountOf(result.Sql, "GROUP BY MetricName\n"));
        Assert.Equal(3, CountOf(result.Sql, "UNION ALL"));
    }

    [Fact]
    public void BuildCatalog_CountsSeriesAsServiceAndAttributeSet_WithUniq()
    {
        var result = MetricCatalogQueryBuilder.BuildCatalog(new MetricCatalogRequest(), 60, Now);

        Assert.Contains("uniq(ServiceName, toString(DataPointAttributes)) AS SeriesCount", result.Sql);
        Assert.Contains("uniq(ServiceName) AS ServiceCount", result.Sql);
        Assert.Contains("count() AS SampleCount", result.Sql);
        Assert.Contains("max(Time) AS LastReceived", result.Sql);
    }

    [Fact]
    public void BuildCatalog_OrdersByCardinalityThenCutsOneOverTheCap()
    {
        var result = MetricCatalogQueryBuilder.BuildCatalog(new MetricCatalogRequest(), 60, Now);
        var parameters = result.Parameters.ToDictionary();

        Assert.Contains("ORDER BY SeriesCount DESC, MetricName\nLIMIT {metricLimit:UInt32}", result.Sql);
        Assert.Equal((uint)(MetricCatalogQueryBuilder.MaxMetrics + 1), parameters["metricLimit"]);
    }

    [Fact]
    public void BuildCatalog_WindowEndsAtNow()
    {
        var result = MetricCatalogQueryBuilder.BuildCatalog(new MetricCatalogRequest(), 90, Now);
        var parameters = result.Parameters.ToDictionary();

        Assert.Equal(Now.AddMinutes(-90).UtcDateTime, parameters["from"]);
        Assert.Equal(Now.UtcDateTime, parameters["to"]);
    }

    [Fact]
    public void BuildCatalog_Search_IsATrimmedCaseInsensitiveSubstringMatch_InEveryBranch()
    {
        var result = MetricCatalogQueryBuilder.BuildCatalog(new MetricCatalogRequest { Search = "  http " }, 60, Now);

        Assert.Equal(4, CountOf(result.Sql, "positionCaseInsensitiveUTF8(MetricName, {search:String}) > 0"));
        Assert.Equal("http", result.Parameters.ToDictionary()["search"]);
    }

    [Fact]
    public void BuildCatalog_NoSearch_NoSearchParameter()
    {
        var result = MetricCatalogQueryBuilder.BuildCatalog(new MetricCatalogRequest { Search = "  " }, 60, Now);

        Assert.DoesNotContain("{search:String}", result.Sql);
        Assert.False(result.Parameters.ToDictionary().ContainsKey("search"));
    }

    [Theory]
    [InlineData(MetricPointType.Gauge, "metrics_gauge")]
    [InlineData(MetricPointType.Sum, "metrics_sum")]
    [InlineData(MetricPointType.Histogram, "metrics_histogram")]
    [InlineData(MetricPointType.ExponentialHistogram, "metrics_exponential_histogram")]
    public void DetailQueries_ReadOnlyTheMetricsOwnTable(MetricPointType type, string table)
    {
        var request = Detail with { Type = type };

        Assert.Contains($"FROM {table}\n", MetricCatalogQueryBuilder.BuildServices(request, 60, Now).Sql);
        Assert.Contains($"FROM {table}\n", MetricCatalogQueryBuilder.BuildAttributes(request, 60, Now).Sql);
    }

    [Fact]
    public void BuildServices_GroupsByService_ScopedToTheMetric()
    {
        var result = MetricCatalogQueryBuilder.BuildServices(Detail, 60, Now);
        var parameters = result.Parameters.ToDictionary();

        Assert.Contains("WHERE MetricName = {metricName:String} AND Time >= {from:DateTime64(9)}", result.Sql);
        Assert.Contains("uniq(toString(DataPointAttributes)) AS SeriesCount", result.Sql);
        Assert.Contains("GROUP BY ServiceName", result.Sql);
        Assert.Equal("http.server.request.duration", parameters["metricName"]);
        Assert.Equal((uint)MetricCatalogQueryBuilder.MaxServices, parameters["serviceLimit"]);
    }

    [Fact]
    public void BuildAttributes_ExpandsKeys_WithDistinctValuesCoverageAndTopValues()
    {
        var result = MetricCatalogQueryBuilder.BuildAttributes(Detail, 60, Now);

        Assert.Contains("arrayJoin(mapKeys(DataPointAttributes)) AS Key, DataPointAttributes[Key] AS Value", result.Sql);
        Assert.Contains("uniq(Value) AS DistinctValueCount", result.Sql);
        Assert.Contains("count() AS SampleCount", result.Sql);
        Assert.Contains($"topK({MetricCatalogQueryBuilder.SampleValueCount})(Value) AS SampleValues", result.Sql);
        Assert.Contains("ORDER BY DistinctValueCount DESC, Key\nLIMIT {keyLimit:UInt32}", result.Sql);
    }

    [Fact]
    public void BuildRelatedCandidates_CollectsServiceAndKeySets_FromAllFourTables()
    {
        var result = MetricCatalogQueryBuilder.BuildRelatedCandidates(60, Now);

        Assert.Equal(4, CountOf(result.Sql, "groupUniqArrayArray(200)(mapKeys(DataPointAttributes)) AS AttributeKeys"));
        Assert.Equal(4, CountOf(result.Sql, "groupUniqArray(200)(ServiceName) AS Services"));
        Assert.Contains("FROM metrics_exponential_histogram", result.Sql);
        Assert.Equal((uint)MetricCatalogQueryBuilder.MaxRelatedCandidates, result.Parameters.ToDictionary()["candidateLimit"]);
    }

    private static int CountOf(string haystack, string needle)
    {
        var count = 0;
        for (var i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0; i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
