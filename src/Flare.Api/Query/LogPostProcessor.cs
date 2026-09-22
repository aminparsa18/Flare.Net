using Flare.Api.Model;

namespace Flare.Api.Query;

/// <summary>
/// Applies a <see cref="LogAggregateRequest.PostProcessFunctions"/> chain to an
/// <c>/api/logs/aggregate</c> response's buckets - pure, no ClickHouse dependency, the
/// Logs-explorer counterpart to <see cref="MetricPostProcessor"/>. <see cref="LogQueryService"/>
/// is the only caller.
/// </summary>
/// <remarks>
/// ADR-0041, the Logs half of the roadmap's "Per-query post-processing functions (metrics and
/// logs)" item - ADR-0038/0039 already shipped the metrics half (point-wise
/// clamp-min/max/absolute/log2/log10, running cumulative-sum, and the two window-based
/// smoothing functions). Time-shift (re-running a query at an offset for a week-over-week/
/// day-over-day overlay, ADR-0040 for metrics) remains a named, still-open follow-up for Logs -
/// per that roadmap item's own note, SigNoz shipped metrics first and extended time-shift to
/// logs separately.
///
/// <para>
/// <b>Grouped series are processed independently.</b> Unlike <see cref="MetricSeries"/>, a
/// <see cref="LogAggregateResponse"/> has no per-series wrapper - <see cref="LogAggregateResponse.Buckets"/>
/// is one flat, possibly-interleaved list disambiguated only by <see cref="LogAggregateBucket.GroupKey"/>
/// when <see cref="LogAggregateRequest.GroupBy"/> is set. <see cref="Apply"/> partitions by
/// <see cref="LogAggregateBucket.GroupKey"/> first (each distinct key, including the ungrouped
/// null key, is its own independent time series), runs the full function chain over each
/// partition separately, then reassembles the result in the original list order - so, e.g., a
/// cumulative-sum or EWMA never blends one service's counts into another's running total.
/// </para>
///
/// <para>
/// <b>No null semantics to speak of, unlike <see cref="MetricPostProcessor"/>.</b>
/// <see cref="LogAggregateBucket.Count"/> is a non-nullable <c>double</c> - a bucket only ever
/// exists in the response because at least one row matched it (a <c>GROUP BY</c> never emits an
/// empty bucket), so there's no per-point "gap" to carry a running total or smoothed value
/// through the way <see cref="MetricPostProcessor"/>'s doc comment describes for a
/// pre-aligned, possibly-sparse metric series. Every function here operates directly on the
/// plain <c>double</c> sequence.
/// </para>
///
/// <para>
/// <b>Log2/Log10 of a non-positive input returns 0, not null</b> - the one real divergence from
/// <see cref="MetricPostProcessor"/>, forced by <see cref="LogAggregateBucket.Count"/> having no
/// nullable slot to return into. 0 is the same "no signal" floor <see cref="LogAggregateBucket.Count"/>
/// itself already uses for "nothing happened" elsewhere in this codebase, and - same reasoning
/// <see cref="MetricPostProcessor"/> gives for avoiding <c>NaN</c>/<c>-Infinity</c> - keeps a
/// mathematically-undefined input from propagating a non-finite value into a later step in the
/// same chain or into the chart it feeds.
/// </para>
/// </remarks>
public static class LogPostProcessor
{
    /// <summary>Runs every function in <paramref name="functions"/> in order, each one's output feeding the next, independently per <see cref="LogAggregateBucket.GroupKey"/>. Returns <paramref name="buckets"/> unchanged (same reference) when <paramref name="functions"/> is empty.</summary>
    public static IReadOnlyList<LogAggregateBucket> Apply(IReadOnlyList<LogAggregateBucket> buckets, IReadOnlyList<LogPostProcessFunction> functions)
    {
        if (functions.Count == 0)
        {
            return buckets;
        }

        // Keyed by GroupKey with the null (ungrouped) case folded into the empty string - Dictionary's
        // TKey : notnull constraint doesn't accept `string?`, and GroupKey is only ever null when
        // LogAggregateRequest.GroupBy is None, in which case every bucket shares that one null key
        // anyway (see LogAggregateBucket.GroupKey's own remarks) - no collision risk with a real,
        // non-empty group value.
        var groupIndices = new Dictionary<string, List<int>>();
        for (var i = 0; i < buckets.Count; i++)
        {
            var key = buckets[i].GroupKey ?? "";
            if (!groupIndices.TryGetValue(key, out var indices))
            {
                indices = [];
                groupIndices[key] = indices;
            }

            indices.Add(i);
        }

        var output = new LogAggregateBucket[buckets.Count];
        foreach (var indices in groupIndices.Values)
        {
            IReadOnlyList<LogAggregateBucket> series = indices.ConvertAll(i => buckets[i]);
            foreach (var function in functions)
            {
                series = ApplyOne(series, function);
            }

            for (var j = 0; j < indices.Count; j++)
            {
                output[indices[j]] = series[j];
            }
        }

        return output;
    }

    private static IReadOnlyList<LogAggregateBucket> ApplyOne(IReadOnlyList<LogAggregateBucket> buckets, LogPostProcessFunction function)
    {
        switch (function.Type)
        {
            case LogPostProcessFunctionType.CumulativeSum:
                return ApplyCumulativeSum(buckets);
            case LogPostProcessFunctionType.EwmaSmoothing:
                return ApplyEwmaSmoothing(buckets, RequireWindowSize(function));
            case LogPostProcessFunctionType.MedianSmoothing:
                return ApplyMedianSmoothing(buckets, RequireWindowSize(function));
        }

        var transform = PointwiseTransform(function);
        var mapped = new LogAggregateBucket[buckets.Count];
        for (var i = 0; i < buckets.Count; i++)
        {
            mapped[i] = buckets[i] with { Count = transform(buckets[i].Count) };
        }

        return mapped;
    }

    private static IReadOnlyList<LogAggregateBucket> ApplyCumulativeSum(IReadOnlyList<LogAggregateBucket> buckets)
    {
        var running = 0.0;
        var output = new LogAggregateBucket[buckets.Count];
        for (var i = 0; i < buckets.Count; i++)
        {
            running += buckets[i].Count;
            output[i] = buckets[i] with { Count = running };
        }

        return output;
    }

    /// <summary>N-period EWMA: <c>alpha = 2 / (windowSize + 1)</c>, same conversion <see cref="MetricPostProcessor"/> uses - no null-carry-forward needed here, see this class' remarks.</summary>
    private static IReadOnlyList<LogAggregateBucket> ApplyEwmaSmoothing(IReadOnlyList<LogAggregateBucket> buckets, int windowSize)
    {
        var alpha = 2.0 / (windowSize + 1);
        var ewma = 0.0;
        var output = new LogAggregateBucket[buckets.Count];
        for (var i = 0; i < buckets.Count; i++)
        {
            ewma = i == 0 ? buckets[i].Count : (alpha * buckets[i].Count) + ((1 - alpha) * ewma);
            output[i] = buckets[i] with { Count = ewma };
        }

        return output;
    }

    /// <summary>Median of the trailing window of up to <paramref name="windowSize"/> buckets ending at (and including) the current one - causal/trailing rather than centered, same reasoning cumulative-sum only ever runs forward.</summary>
    private static IReadOnlyList<LogAggregateBucket> ApplyMedianSmoothing(IReadOnlyList<LogAggregateBucket> buckets, int windowSize)
    {
        var output = new LogAggregateBucket[buckets.Count];
        var window = new List<double>(windowSize);
        for (var i = 0; i < buckets.Count; i++)
        {
            window.Clear();
            var start = Math.Max(0, i - windowSize + 1);
            for (var j = start; j <= i; j++)
            {
                window.Add(buckets[j].Count);
            }

            output[i] = buckets[i] with { Count = Median(window) };
        }

        return output;
    }

    private static double Median(List<double> values)
    {
        values.Sort();
        var mid = values.Count / 2;
        return values.Count % 2 == 0 ? (values[mid - 1] + values[mid]) / 2.0 : values[mid];
    }

    private static Func<double, double> PointwiseTransform(LogPostProcessFunction function) => function.Type switch
    {
        LogPostProcessFunctionType.ClampMin => v => Math.Max(v, RequireValue(function)),
        LogPostProcessFunctionType.ClampMax => v => Math.Min(v, RequireValue(function)),
        LogPostProcessFunctionType.Absolute => v => Math.Abs(v),
        LogPostProcessFunctionType.Log2 => v => v > 0 ? Math.Log2(v) : 0,
        LogPostProcessFunctionType.Log10 => v => v > 0 ? Math.Log10(v) : 0,
        _ => throw new ArgumentOutOfRangeException(nameof(function), function.Type, $"Unrecognized {nameof(LogPostProcessFunctionType)}."),
    };

    /// <summary>Surfaced as a caught <see cref="ArgumentOutOfRangeException"/> -&gt; 400 at <c>LogsEndpoints.HandleAggregateAsync</c>, same convention <see cref="LogAggregateQueryBuilder"/>'s own non-positive-bucket-width check already uses.</summary>
    private static double RequireValue(LogPostProcessFunction function) =>
        function.Value ?? throw new ArgumentOutOfRangeException(nameof(function), function.Type, $"{function.Type} requires {nameof(LogPostProcessFunction.Value)}.");

    /// <summary>Same convention as <see cref="RequireValue"/>, for <see cref="LogPostProcessFunction.WindowSize"/> - null or non-positive both reject, since a zero/negative lookback has no meaningful window.</summary>
    private static int RequireWindowSize(LogPostProcessFunction function) =>
        function.WindowSize is { } windowSize && windowSize >= 1
            ? windowSize
            : throw new ArgumentOutOfRangeException(nameof(function), function.WindowSize, $"{function.Type} requires {nameof(LogPostProcessFunction.WindowSize)} >= 1.");
}
