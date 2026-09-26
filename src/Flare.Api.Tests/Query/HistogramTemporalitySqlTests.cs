using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Query;

public class HistogramTemporalitySqlTests
{
    private const string Delta = "AggregationTemporality = 'AGGREGATION_TEMPORALITY_DELTA'";

    [Fact]
    public void ExplicitAggregates_PassDeltaRowsThrough_AndZeroASeriesFirstCumulativeRow()
    {
        var sql = HistogramTemporalitySql.ExplicitAggregates;

        Assert.Contains($"sum(multiIf({Delta}, Count, SeriesRowNum = 1, toUInt64(0),", sql);
        Assert.Contains("SeriesRowNum = 1, arrayWithConstant(length(BucketCounts), toUInt64(0))", sql);
    }

    [Fact]
    public void ExplicitAggregates_TreatAStartTimeChange_ACountDrop_OrALayoutChange_AsAReset()
    {
        Assert.Contains(
            "(StartTime != PrevStartTime OR Count < PrevCount OR length(BucketCounts) != length(PrevBucketCounts)), Count, toUInt64(Count - PrevCount)",
            HistogramTemporalitySql.ExplicitAggregates);
    }

    [Fact]
    public void ExplicitAggregates_DiffBucketCountsElementWise_ForCumulativeRows()
    {
        Assert.Contains(
            "arrayMap((c, p) -> toUInt64(if(c >= p, c - p, 0)), BucketCounts, PrevBucketCounts)",
            HistogramTemporalitySql.ExplicitAggregates);
    }

    [Fact]
    public void ExplicitRankedCte_WindowsOverTheFullSeriesIdentity()
    {
        var sql = HistogramTemporalitySql.ExplicitRankedCte("metrics_histogram", "1 = 1", "Unit");

        Assert.StartsWith("WITH ranked AS (\n  SELECT Unit, ExplicitBounds,", sql);
        Assert.Contains("lagInFrame(BucketCounts) OVER w AS PrevBucketCounts", sql);
        Assert.Contains("WINDOW w AS (PARTITION BY ServiceName, toString(DataPointAttributes) ORDER BY Time)", sql);
    }

    [Fact]
    public void ExponentialContributionsCte_KeepsDeltaRows_AndEveryCumulativeRowButTheFirst()
    {
        var sql = HistogramTemporalitySql.ExponentialContributionsCte("metrics_exponential_histogram", "1 = 1", "Unit", "Unit");

        Assert.Contains($"SELECT Unit, toInt8(1) AS Sign,", sql);
        Assert.Contains($"WHERE {Delta} OR SeriesRowNum > 1", sql);
    }

    [Fact]
    public void ExponentialContributionsCte_SubtractsThePreviousPoint_AtItsOwnScale_UnlessReset()
    {
        var sql = HistogramTemporalitySql.ExponentialContributionsCte("metrics_exponential_histogram", "1 = 1", "Unit", "Unit");

        Assert.Contains("SELECT Unit, toInt8(-1), PrevCount, PrevSum, PrevScale, PrevZeroCount,", sql);
        Assert.Contains($"WHERE NOT ({Delta}) AND SeriesRowNum > 1 AND StartTime = PrevStartTime AND Count >= PrevCount", sql);
    }

    [Fact]
    public void ExponentialContributionsCte_DropsCumulativeMinMax()
    {
        var sql = HistogramTemporalitySql.ExponentialContributionsCte("metrics_exponential_histogram", "1 = 1", "Unit", "Unit");

        Assert.Contains($"if({Delta}, Max, CAST(NULL AS Nullable(Float64))) AS RowMax", sql);
    }

    [Fact]
    public void ExponentialAggregates_SignEveryCount()
    {
        var sql = HistogramTemporalitySql.ExponentialAggregates;

        Assert.Contains("sum(Sign * toInt64(Count)) AS CountTotal", sql);
        Assert.Contains("arrayMap(c -> Sign * toInt64(c), PositiveBucketCounts)", sql);
        Assert.Contains("arrayMap(c -> Sign * toInt64(c), NegativeBucketCounts)", sql);
    }
}
