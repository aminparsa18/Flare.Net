using Flare.Api.Model;

namespace Flare.Api.Query;

/// <summary>
/// Applies a <see cref="MetricQueryRequest.PostProcessFunctions"/> chain to one series'
/// points, in list order - pure, no ClickHouse dependency, the same "post-process
/// already-fetched rows" shape as <see cref="HistogramQuantileEstimator"/>.
/// <see cref="MetricQueryService"/> is the only caller, and only for Gauge/Sum series -
/// see <see cref="MetricQueryRequest.PostProcessFunctions"/>'s remarks for why Histogram
/// is excluded entirely.
/// </summary>
/// <remarks>
/// ADR-0038, the metrics half of the roadmap's "Per-query post-processing functions
/// (metrics and logs)" item: point-wise clamp-min/max/absolute/log2/log10 plus a running
/// cumulative-sum. ADR-0039 added the two window-based smoothing functions
/// (<see cref="MetricPostProcessFunctionType.EwmaSmoothing"/>/<see cref="MetricPostProcessFunctionType.MedianSmoothing"/>).
/// Time-shift (re-running the query at an offset for week-over-week/day-over-day overlay)
/// remains a named, deliberately-unresolved follow-up - it needs a second query dispatch
/// and a result-alignment step, a structurally different feature from this single-pass
/// pipeline. Logs support is a separate follow-up too, per the roadmap item's own note that
/// SigNoz shipped metrics first, then extended time-shift to logs separately.
///
/// Log2/Log10 of a non-positive input is mathematically undefined - returns null for that
/// point rather than NaN/-Infinity, so a bad input doesn't propagate a non-finite value
/// into a later clamp/cumulative-sum step in the same chain or into a chart it feeds.
///
/// Cumulative-sum treats a null point (no data in that bucket) as "nothing new happened
/// this bucket" - the running total carries forward unchanged rather than the output point
/// itself staying null, so the result is a genuine running total end to end, never
/// punctured by gaps the way a plain per-point transform's null-in/null-out rule would
/// leave it.
///
/// EwmaSmoothing/MedianSmoothing (ADR-0039) share that same "carry forward, don't
/// re-puncture gaps" philosophy rather than the plain point-wise null-in/null-out rule:
/// gap-filling small holes is the actual value proposition of a smoothing function, so
/// both output the smoothed value derived from whatever real data has been seen so far
/// (EWMA) or falls within the trailing window (median) even when the current bucket itself
/// is null, and only emit null where no real data is available at all yet.
/// </remarks>
public static class MetricPostProcessor
{
    /// <summary>Runs every function in <paramref name="functions"/> in order, each one's output feeding the next. Returns <paramref name="points"/> unchanged (same reference) when <paramref name="functions"/> is empty.</summary>
    public static IReadOnlyList<MetricSeriesPoint> Apply(IReadOnlyList<MetricSeriesPoint> points, IReadOnlyList<MetricPostProcessFunction> functions)
    {
        if (functions.Count == 0)
        {
            return points;
        }

        var result = points;
        foreach (var function in functions)
        {
            result = ApplyOne(result, function);
        }

        return result;
    }

    private static IReadOnlyList<MetricSeriesPoint> ApplyOne(IReadOnlyList<MetricSeriesPoint> points, MetricPostProcessFunction function)
    {
        switch (function.Type)
        {
            case MetricPostProcessFunctionType.CumulativeSum:
                return ApplyCumulativeSum(points);
            case MetricPostProcessFunctionType.EwmaSmoothing:
                return ApplyEwmaSmoothing(points, RequireWindowSize(function));
            case MetricPostProcessFunctionType.MedianSmoothing:
                return ApplyMedianSmoothing(points, RequireWindowSize(function));
        }

        var transform = PointwiseTransform(function);
        var mapped = new MetricSeriesPoint[points.Count];
        for (var i = 0; i < points.Count; i++)
        {
            var value = points[i].Value;
            mapped[i] = points[i] with { Value = value.HasValue ? transform(value.Value) : null };
        }

        return mapped;
    }

    private static IReadOnlyList<MetricSeriesPoint> ApplyCumulativeSum(IReadOnlyList<MetricSeriesPoint> points)
    {
        var running = 0.0;
        var output = new MetricSeriesPoint[points.Count];
        for (var i = 0; i < points.Count; i++)
        {
            running += points[i].Value ?? 0;
            output[i] = points[i] with { Value = running };
        }

        return output;
    }

    /// <summary>
    /// N-period EWMA: <c>alpha = 2 / (windowSize + 1)</c>, the standard conversion from a
    /// "period count" to a decay factor. A null bucket doesn't update the running average
    /// (no new data to weigh in) but still emits whatever average has accumulated so far,
    /// so a single missing bucket doesn't punch a hole in an otherwise-smooth line - see
    /// this class' remarks. Null only before the first real value is seen.
    /// </summary>
    private static IReadOnlyList<MetricSeriesPoint> ApplyEwmaSmoothing(IReadOnlyList<MetricSeriesPoint> points, int windowSize)
    {
        var alpha = 2.0 / (windowSize + 1);
        double? ewma = null;
        var output = new MetricSeriesPoint[points.Count];
        for (var i = 0; i < points.Count; i++)
        {
            var value = points[i].Value;
            if (value.HasValue)
            {
                ewma = ewma.HasValue ? (alpha * value.Value) + ((1 - alpha) * ewma.Value) : value.Value;
            }

            output[i] = points[i] with { Value = ewma };
        }

        return output;
    }

    /// <summary>
    /// Median of the non-null values in the trailing window of up to <paramref name="windowSize"/>
    /// buckets ending at (and including) the current one - null only when that window has
    /// no real data at all. Causal/trailing rather than centered so it never looks ahead of
    /// the current bucket, same reasoning cumulative-sum only ever runs forward.
    /// </summary>
    private static IReadOnlyList<MetricSeriesPoint> ApplyMedianSmoothing(IReadOnlyList<MetricSeriesPoint> points, int windowSize)
    {
        var output = new MetricSeriesPoint[points.Count];
        var window = new List<double>(windowSize);
        for (var i = 0; i < points.Count; i++)
        {
            window.Clear();
            var start = Math.Max(0, i - windowSize + 1);
            for (var j = start; j <= i; j++)
            {
                if (points[j].Value is { } v)
                {
                    window.Add(v);
                }
            }

            output[i] = points[i] with { Value = window.Count == 0 ? null : Median(window) };
        }

        return output;
    }

    private static double Median(List<double> values)
    {
        values.Sort();
        var mid = values.Count / 2;
        return values.Count % 2 == 0 ? (values[mid - 1] + values[mid]) / 2.0 : values[mid];
    }

    private static Func<double, double?> PointwiseTransform(MetricPostProcessFunction function) => function.Type switch
    {
        MetricPostProcessFunctionType.ClampMin => v => Math.Max(v, RequireValue(function)),
        MetricPostProcessFunctionType.ClampMax => v => Math.Min(v, RequireValue(function)),
        MetricPostProcessFunctionType.Absolute => v => Math.Abs(v),
        MetricPostProcessFunctionType.Log2 => v => v > 0 ? Math.Log2(v) : null,
        MetricPostProcessFunctionType.Log10 => v => v > 0 ? Math.Log10(v) : null,
        _ => throw new ArgumentOutOfRangeException(nameof(function), function.Type, $"Unrecognized {nameof(MetricPostProcessFunctionType)}."),
    };

    /// <summary>Surfaced as a caught <see cref="ArgumentOutOfRangeException"/> -&gt; 400 at <c>MetricsEndpoints.HandleQueryAsync</c>, same convention every other malformed-request path in that handler already uses.</summary>
    private static double RequireValue(MetricPostProcessFunction function) =>
        function.Value ?? throw new ArgumentOutOfRangeException(nameof(function), function.Type, $"{function.Type} requires {nameof(MetricPostProcessFunction.Value)}.");

    /// <summary>Same convention as <see cref="RequireValue"/>, for <see cref="MetricPostProcessFunction.WindowSize"/> - null or non-positive both reject, since a zero/negative lookback has no meaningful window.</summary>
    private static int RequireWindowSize(MetricPostProcessFunction function) =>
        function.WindowSize is { } windowSize && windowSize >= 1
            ? windowSize
            : throw new ArgumentOutOfRangeException(nameof(function), function.WindowSize, $"{function.Type} requires {nameof(MetricPostProcessFunction.WindowSize)} >= 1.");
}
