namespace Flare.Api.Query;

/// <summary>
/// SQL for merging a rollup table's percentile states when ingest-side sampling (ADR-0122)
/// may be on. Migration 0051 added weighted t-digest states next to the original ones plus a
/// <c>SampledCount</c> column; a window with no weighted (sampled) spans reads the original
/// states, which also cover history from before the migration, and one with any reads the
/// weighted states so the kept error/slow traces don't skew the percentile.
/// </summary>
internal static class SampledQuantileSql
{
    public static string Merge(double quantile, string state, string weightedState)
    {
        var p = quantile.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return $"if(sum(SampledCount) = 0, quantileMerge({p})({state}), quantileTDigestWeightedMerge({p})({weightedState}))";
    }

    /// <summary><paramref name="quantiles"/> is the literal list, e.g. <c>"0.5, 0.95, 0.99"</c>.</summary>
    public static string MergeMany(string quantiles, string state, string weightedState) =>
        $"if(sum(SampledCount) = 0, quantilesMerge({quantiles})({state}), quantilesTDigestWeightedMerge({quantiles})({weightedState}))";
}
