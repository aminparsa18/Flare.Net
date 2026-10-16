namespace Flare.Api.Loki;

/// <summary>What a range aggregation measures in each bucket.</summary>
internal enum LokiMeasure
{
    Count,
    Bytes,
}

/// <summary>
/// One bucketed read for a range aggregation over <c>[FromMs, ToMs)</c>. <see cref="BucketMs"/> of 0 asks
/// for a single bucket (an instant query); otherwise buckets are <see cref="BucketMs"/> wide and start at
/// <see cref="FromMs"/>, so they line up with the evaluation steps whatever the epoch.
/// </summary>
internal sealed record LokiLeafRequest(
    LokiLogSelector Selector,
    IReadOnlyList<string> Dimensions,
    LokiMeasure Measure,
    long FromMs,
    long ToMs,
    long BucketMs);

/// <summary>One row of a bucketed read: the bucket start (unix ms), the value of each requested dimension, and the measure.</summary>
internal sealed record LokiBucketRow(long BucketMs, string[] DimensionValues, double Value);

/// <summary>What every mergeable unwrapped function (sum, avg, min, max, first, last, stddev, stdvar) is computed from, for the samples of one bucket.</summary>
/// <param name="Count">Number of samples.</param>
/// <param name="Sum">Sum of the values.</param>
/// <param name="Min">Smallest value.</param>
/// <param name="Max">Largest value.</param>
/// <param name="First">Value of the earliest sample.</param>
/// <param name="Last">Value of the latest sample.</param>
/// <param name="Mean">Mean value.</param>
/// <param name="M2">Sum of squared deviations from the mean, so variances can be merged exactly (Chan et al.).</param>
internal readonly record struct LokiBucketStats(double Count, double Sum, double Min, double Max, double First, double Last, double Mean, double M2)
{
    /// <summary>The statistics of this bucket's samples followed (in time) by <paramref name="next"/>'s.</summary>
    public LokiBucketStats Then(LokiBucketStats next)
    {
        var count = Count + next.Count;
        var delta = next.Mean - Mean;
        return new LokiBucketStats(
            count,
            Sum + next.Sum,
            Math.Min(Min, next.Min),
            Math.Max(Max, next.Max),
            First,
            next.Last,
            Mean + delta * next.Count / count,
            M2 + next.M2 + delta * delta * Count * next.Count / count);
    }
}

/// <summary>A bucketed read of an unwrapped range function's samples; the grid is that of <see cref="LokiLeafRequest"/>.</summary>
internal sealed record LokiUnwrapRequest(LokiLogSelector Selector, IReadOnlyList<string> Dimensions, long FromMs, long ToMs, long BucketMs);

internal sealed record LokiUnwrapRow(long BucketMs, string[] DimensionValues, LokiBucketStats Stats);

/// <summary>
/// An exact quantile per evaluation step: the samples of the window <c>[step - RangeMs, step)</c> for each of
/// <see cref="StepCount"/> steps starting at <see cref="FirstStepMs"/>.
/// </summary>
internal sealed record LokiQuantileRequest(
    LokiLogSelector Selector, IReadOnlyList<string> Dimensions, double Quantile, long FirstStepMs, long StepMs, int StepCount, long RangeMs);

internal sealed record LokiQuantileRow(int StepIndex, string[] DimensionValues, double Value);

/// <summary>Where <see cref="LokiEvaluator"/> gets its data: <see cref="LokiQueryService"/> in production, a fake in tests.</summary>
internal interface ILokiLeafSource
{
    /// <summary>The label names a stream carries when an aggregation does not narrow them.</summary>
    Task<IReadOnlyList<string>> StreamLabelNamesAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<LokiBucketRow>> FetchBucketsAsync(LokiLeafRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<LokiUnwrapRow>> FetchUnwrapBucketsAsync(LokiUnwrapRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<LokiQuantileRow>> FetchQuantilesAsync(LokiQuantileRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// The labels a leaf must group by. <see cref="Exact"/> is set only for <c>sum by (...)</c> directly over a
/// range aggregation, where grouping in ClickHouse is equivalent to grouping afterwards; otherwise the leaf
/// returns whole streams plus the <see cref="Extra"/> labels that ancestors group on.
/// </summary>
internal sealed record LokiNeed(IReadOnlyList<string>? Exact, IReadOnlyList<string> Extra)
{
    public static readonly LokiNeed Streams = new(null, []);
}

/// <summary>One labelled series; <see cref="Values"/> has one slot per evaluation step, null where the series has no sample.</summary>
internal sealed record LokiSeries(IReadOnlyDictionary<string, string> Labels, double?[] Values);

/// <summary>The result of evaluating a metric query: the evaluation timestamps (unix ms) and the series. <see cref="IsScalar"/> marks a bare number.</summary>
internal sealed record LokiMatrix(long[] Steps, IReadOnlyList<LokiSeries> Series, bool IsScalar = false);

/// <summary>
/// Evaluates the metric side of the LogQL subset (ADR-0236) over a <see cref="ILokiLeafSource"/>.
/// A range aggregation is one bucketed ClickHouse read followed by a sliding window over the buckets,
/// so the window and step are honoured without a query per step. Vector aggregations, binary operators
/// and <c>topk</c> are folded here, in memory, over the series the leaves return.
/// </summary>
internal sealed class LokiEvaluator(ILokiLeafSource source)
{
    /// <summary>Loki's <c>max_query_series</c>: a leaf that groups into more series than this is an error rather than a truncation.</summary>
    public const int MaxSeries = 500;

    /// <summary>Loki's cap on points per series; a wider range needs a larger step.</summary>
    public const int MaxPoints = 11_000;

    /// <summary>Upper bound on buckets read per leaf. The bucket width is the gcd of window and step while that fits, the step otherwise.</summary>
    public const int MaxBuckets = 20_000;

    /// <summary>Most (steps x buckets per window) bucket merges an unwrapped function may need; past it the step must grow.</summary>
    public const long MaxWindowMerges = 30_000_000;

    /// <summary>Most evaluation windows a single sample may fall into for <c>quantile_over_time</c> (range divided by step).</summary>
    public const int MaxQuantileWindows = 120;

    /// <summary>Evaluates at <c>start, start+step, ... &lt;= end</c>. <paramref name="instant"/> reads exactly the window ending at <c>end</c>.</summary>
    public async Task<LokiMatrix> EvaluateAsync(LokiExpr expr, long startMs, long endMs, long stepMs, bool instant, CancellationToken cancellationToken)
    {
        if (endMs < startMs)
        {
            throw new LokiException("end timestamp must not be before start time.");
        }

        if (stepMs <= 0)
        {
            throw new LokiException("zero or negative query resolution step widths are not accepted. Try a positive integer.");
        }

        var count = (endMs - startMs) / stepMs + 1;
        if (count > MaxPoints)
        {
            throw new LokiException($"exceeded maximum resolution of {MaxPoints} points per time series. Try increasing the value of the step parameter.");
        }

        var steps = new long[count];
        for (var i = 0; i < count; i++)
        {
            steps[i] = startMs + i * stepMs;
        }

        return await EvalAsync(expr, steps, stepMs, instant, LokiNeed.Streams, cancellationToken);
    }

    private async Task<LokiMatrix> EvalAsync(LokiExpr expr, long[] steps, long stepMs, bool instant, LokiNeed needed, CancellationToken ct)
    {
        switch (expr)
        {
            case LokiScalar scalar:
                return Constant(steps, scalar.Value, isScalar: true);
            case LokiVectorLiteral literal:
                return Constant(steps, literal.Value, isScalar: false);
            case LokiLogSelector:
                throw new LokiException("a log query cannot be evaluated as a metric; wrap it in rate(), count_over_time(), bytes_rate() or bytes_over_time().");
            case LokiRangeAggregation range:
                return await RangeAsync(range, steps, stepMs, instant, needed, ct);
            case LokiVectorAggregation aggregation:
                return await AggregateAsync(aggregation, steps, stepMs, instant, needed, ct);
            case LokiBinary binary:
            {
                var left = await EvalAsync(binary.Left, steps, stepMs, instant, needed, ct);
                var right = await EvalAsync(binary.Right, steps, stepMs, instant, needed, ct);
                return Combine(binary.Op, left, right);
            }

            default:
                throw new LokiException("unsupported expression.");
        }
    }

    private static LokiMatrix Constant(long[] steps, double value, bool isScalar)
    {
        var values = new double?[steps.Length];
        Array.Fill(values, value);
        return new LokiMatrix(steps, [new LokiSeries(new Dictionary<string, string>(), values)], isScalar);
    }

    private async Task<LokiMatrix> RangeAsync(LokiRangeAggregation range, long[] steps, long stepMs, bool instant, LokiNeed needed, CancellationToken ct)
    {
        if (range.IsUnwrapped)
        {
            return await UnwrappedRangeAsync(range, steps, stepMs, instant, needed, ct);
        }

        var measure = range.Function.StartsWith("bytes", StringComparison.Ordinal) ? LokiMeasure.Bytes : LokiMeasure.Count;
        if (measure == LokiMeasure.Bytes && range.Selector.Stages.OfType<LokiLineFormatStage>().Any())
        {
            // line_format only rewrites the line that is returned, so the size of the rewritten line is not known to the query.
            throw new LokiException($"{range.Function}() cannot measure a line rewritten by line_format; drop the line_format stage.");
        }

        var dimensions = needed.Exact ?? (await source.StreamLabelNamesAsync(ct)).Union(needed.Extra).ToList();
        var rangeMs = (long)range.Range.TotalMilliseconds;
        var rangeSeconds = range.Range.TotalSeconds;
        var perSecond = range.Function is "rate" or "bytes_rate";

        if (instant)
        {
            var end = steps[^1];
            var rows = await source.FetchBucketsAsync(new LokiLeafRequest(range.Selector, dimensions, measure, end - rangeMs, end, BucketMs: 0), ct);
            var grouped = GroupRows(rows, dimensions, r => r.DimensionValues);
            var series = grouped.Select(g => new LokiSeries(g.Key, [perSecond ? g.Value.Sum(r => r.Value) / rangeSeconds : g.Value.Sum(r => r.Value)])).ToList();
            return new LokiMatrix(steps, series);
        }

        var grid = BucketGrid.For(rangeMs, stepMs, steps);
        var request = new LokiLeafRequest(range.Selector, dimensions, measure, grid.FirstBucket, grid.LastBucketEnd, grid.BucketMs);
        var fetched = GroupRows(await source.FetchBucketsAsync(request, ct), dimensions, r => r.DimensionValues);

        var result = new List<LokiSeries>(fetched.Count);
        foreach (var (labels, rows) in fetched)
        {
            // Prefix sums over a dense bucket array: each step's window is two subtractions.
            var sums = new double[grid.BucketCount + 1];
            var present = new int[grid.BucketCount + 1];
            foreach (var row in rows)
            {
                var index = grid.IndexOf(row.BucketMs);
                if (index >= 0 && index < grid.BucketCount)
                {
                    sums[index + 1] += row.Value;
                    present[index + 1]++;
                }
            }

            for (var i = 1; i <= grid.BucketCount; i++)
            {
                sums[i] += sums[i - 1];
                present[i] += present[i - 1];
            }

            var values = new double?[steps.Length];
            for (var s = 0; s < steps.Length; s++)
            {
                var (lo, hi) = grid.Window(steps[s]);
                if (hi <= lo || present[hi] - present[lo] == 0)
                {
                    continue;
                }

                var total = sums[hi] - sums[lo];
                values[s] = perSecond ? total / rangeSeconds : total;
            }

            result.Add(new LokiSeries(labels, values));
        }

        return new LokiMatrix(steps, result);
    }

    /// <summary>
    /// <c>sum|avg|min|max|first|last|stddev|stdvar|quantile_over_time</c> over an <c>| unwrap</c>ped label.
    /// The function's own <c>by</c>/<c>without</c> clause groups the samples before it is applied. The
    /// mergeable functions read per-bucket statistics and merge the buckets of each window; the quantile
    /// is computed per step in ClickHouse. A line whose value does not parse as a number is not a sample.
    /// </summary>
    private async Task<LokiMatrix> UnwrappedRangeAsync(LokiRangeAggregation range, long[] steps, long stepMs, bool instant, LokiNeed needed, CancellationToken ct)
    {
        var rangeMs = (long)range.Range.TotalMilliseconds;
        var dimensions = range.Grouping switch
        {
            { Without: false } by => by.Labels,
            { } without => (await source.StreamLabelNamesAsync(ct)).Except(without.Labels).ToList(),
            // sum_over_time is additive, so a sum by (...) around it is pushed down; the others group whole streams.
            null => needed.Exact ?? (await source.StreamLabelNamesAsync(ct)).Union(needed.Extra).ToList(),
        };

        if (range.Function == "quantile_over_time")
        {
            var quantileStep = instant ? rangeMs : stepMs;
            var windows = (rangeMs + quantileStep - 1) / quantileStep;
            if (windows > MaxQuantileWindows)
            {
                throw new LokiException($"quantile_over_time needs a step of at least 1/{MaxQuantileWindows} of its range ({range.Range}); increase the step parameter or shorten the range.");
            }

            var rows = await source.FetchQuantilesAsync(
                new LokiQuantileRequest(range.Selector, dimensions, range.Quantile ?? 0.5, steps[0], quantileStep, steps.Length, rangeMs), ct);
            var byLabels = GroupRows(rows, dimensions, r => r.DimensionValues);
            var quantiles = new List<LokiSeries>(byLabels.Count);
            foreach (var (labels, members) in byLabels)
            {
                var values = new double?[steps.Length];
                foreach (var row in members.Where(m => m.StepIndex >= 0 && m.StepIndex < steps.Length))
                {
                    values[row.StepIndex] = row.Value;
                }

                quantiles.Add(new LokiSeries(labels, values));
            }

            return new LokiMatrix(steps, quantiles);
        }

        if (instant)
        {
            var end = steps[^1];
            var rows = await source.FetchUnwrapBucketsAsync(new LokiUnwrapRequest(range.Selector, dimensions, end - rangeMs, end, BucketMs: 0), ct);
            var grouped = GroupRows(rows, dimensions, r => r.DimensionValues);
            return new LokiMatrix(steps, grouped.Select(g => new LokiSeries(g.Key, [Reduce(range.Function, g.Value.Select(r => r.Stats).Aggregate((a, b) => a.Then(b)))])).ToList());
        }

        var grid = BucketGrid.For(rangeMs, stepMs, steps);
        if ((long)steps.Length * grid.BucketsPerWindow > MaxWindowMerges)
        {
            throw new LokiException($"{range.Function} over {range.Range} at this step would merge too many buckets; increase the step parameter or shorten the range.");
        }

        var request = new LokiUnwrapRequest(range.Selector, dimensions, grid.FirstBucket, grid.LastBucketEnd, grid.BucketMs);
        var fetched = GroupRows(await source.FetchUnwrapBucketsAsync(request, ct), dimensions, r => r.DimensionValues);
        var result = new List<LokiSeries>(fetched.Count);
        foreach (var (labels, rows) in fetched)
        {
            var buckets = new LokiBucketStats?[grid.BucketCount];
            foreach (var row in rows)
            {
                var index = grid.IndexOf(row.BucketMs);
                if (index >= 0 && index < grid.BucketCount)
                {
                    buckets[index] = buckets[index] is { } existing ? existing.Then(row.Stats) : row.Stats;
                }
            }

            var values = new double?[steps.Length];
            for (var s = 0; s < steps.Length; s++)
            {
                var (lo, hi) = grid.Window(steps[s]);
                LokiBucketStats? merged = null;
                for (var i = lo; i < hi; i++)
                {
                    if (buckets[i] is { } bucket)
                    {
                        merged = merged is { } so ? so.Then(bucket) : bucket;
                    }
                }

                if (merged is { } stats)
                {
                    values[s] = Reduce(range.Function, stats);
                }
            }

            result.Add(new LokiSeries(labels, values));
        }

        return new LokiMatrix(steps, result);
    }

    private static double Reduce(string function, LokiBucketStats stats) => function switch
    {
        "sum_over_time" => stats.Sum,
        "avg_over_time" => stats.Sum / stats.Count,
        "min_over_time" => stats.Min,
        "max_over_time" => stats.Max,
        "first_over_time" => stats.First,
        "last_over_time" => stats.Last,
        "stdvar_over_time" => Math.Max(0, stats.M2 / stats.Count),
        "stddev_over_time" => Math.Sqrt(Math.Max(0, stats.M2 / stats.Count)),
        _ => throw new LokiException($"unsupported function '{function}'."),
    };

    /// <summary>
    /// The bucket grid of a range read: buckets of <see cref="BucketMs"/> starting one window before the
    /// first step, so every step falls on a bucket edge. The window of a step is the buckets that end at or
    /// before it, <c>[lo, hi)</c>.
    /// </summary>
    internal readonly record struct BucketGrid(long FirstBucket, long BucketMs, int BucketsPerWindow, int BucketCount)
    {
        public long LastBucketEnd => FirstBucket + BucketCount * BucketMs;

        public static BucketGrid For(long rangeMs, long stepMs, long[] steps)
        {
            var bucketMs = ChooseBucketMs(rangeMs, stepMs, steps[0], steps[^1]);
            var perWindow = (int)Math.Max(1, Math.Round((double)rangeMs / bucketMs));
            var first = steps[0] - perWindow * bucketMs;
            return new BucketGrid(first, bucketMs, perWindow, (int)((steps[^1] - first) / bucketMs));
        }

        public long IndexOf(long bucketStartMs) => (bucketStartMs - FirstBucket) / BucketMs;

        public (int Lo, int Hi) Window(long stepMs)
        {
            var hi = Math.Min((int)((stepMs - FirstBucket) / BucketMs), BucketCount);
            return (Math.Max(0, hi - BucketsPerWindow), hi);
        }
    }

    /// <summary>
    /// The bucket width in ms. The gcd of window and step keeps every window an exact number of buckets
    /// (a 1m window at a 15s step reads 15s buckets); when that would read more than
    /// <see cref="MaxBuckets"/> buckets it falls back to the step, and a window that is not a whole number
    /// of steps is then rounded to the nearest bucket.
    /// </summary>
    internal static long ChooseBucketMs(long rangeMs, long stepMs, long firstStepMs, long lastStepMs)
    {
        var step = Math.Max(1000, stepMs / 1000 * 1000);
        var range = Math.Max(1000, rangeMs / 1000 * 1000);
        var gcd = Gcd(range, step);
        var span = lastStepMs - firstStepMs + range;
        return span / gcd <= MaxBuckets ? gcd : step;
    }

    private static long Gcd(long a, long b)
    {
        while (b != 0)
        {
            (a, b) = (b, a % b);
        }

        return a;
    }

    private static Dictionary<IReadOnlyDictionary<string, string>, List<TRow>> GroupRows<TRow>(
        IReadOnlyList<TRow> rows, IReadOnlyList<string> dimensions, Func<TRow, string[]> dimensionValues)
    {
        var groups = new Dictionary<IReadOnlyDictionary<string, string>, List<TRow>>(LabelSetComparer.Instance);
        foreach (var row in rows)
        {
            var values = dimensionValues(row);
            var labels = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 0; i < dimensions.Count && i < values.Length; i++)
            {
                // Loki drops empty labels from a series.
                if (values[i].Length > 0)
                {
                    labels[dimensions[i]] = values[i];
                }
            }

            if (!groups.TryGetValue(labels, out var list))
            {
                if (groups.Count >= MaxSeries)
                {
                    throw new LokiException($"maximum of series ({MaxSeries}) reached for a single query; narrow the selector or aggregate with sum by (...).");
                }

                groups[labels] = list = [];
            }

            list.Add(row);
        }

        return groups;
    }

    private async Task<LokiMatrix> AggregateAsync(LokiVectorAggregation aggregation, long[] steps, long stepMs, bool instant, LokiNeed needed, CancellationToken ct)
    {
        var isTopK = aggregation.Function is "topk" or "bottomk";

        // Only a sum is additive, so only it can be grouped by ClickHouse; avg, min, max and count (and the
        // series topk selects) are taken over whole streams, with the grouping labels added to them.
        var innerNeed = aggregation.Function == "sum" && !aggregation.Without && aggregation.Inner is LokiRangeAggregation { Grouping: null } inner0
            && (!inner0.IsUnwrapped || inner0.Function == "sum_over_time")
            ? new LokiNeed(aggregation.Labels, [])
            : new LokiNeed(null, aggregation.Without ? needed.Extra : needed.Extra.Union(aggregation.Labels).ToList());

        var inner = await EvalAsync(aggregation.Inner, steps, stepMs, instant, innerNeed, ct);

        if (isTopK)
        {
            return TopK(aggregation, inner);
        }

        var groups = new Dictionary<IReadOnlyDictionary<string, string>, List<LokiSeries>>(LabelSetComparer.Instance);
        foreach (var series in inner.Series)
        {
            var labels = aggregation.Without
                ? series.Labels.Where(kv => !aggregation.Labels.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value)
                : series.Labels.Where(kv => aggregation.Labels.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
            if (!groups.TryGetValue(labels, out var list))
            {
                groups[labels] = list = [];
            }

            list.Add(series);
        }

        var result = new List<LokiSeries>(groups.Count);
        foreach (var (labels, members) in groups)
        {
            var values = new double?[steps.Length];
            for (var s = 0; s < steps.Length; s++)
            {
                double sum = 0, min = double.PositiveInfinity, max = double.NegativeInfinity;
                var n = 0;
                foreach (var member in members)
                {
                    if (member.Values[s] is not { } v)
                    {
                        continue;
                    }

                    sum += v;
                    min = Math.Min(min, v);
                    max = Math.Max(max, v);
                    n++;
                }

                if (n == 0)
                {
                    continue;
                }

                values[s] = aggregation.Function switch
                {
                    "sum" => sum,
                    "avg" => sum / n,
                    "min" => min,
                    "max" => max,
                    _ => n,
                };
            }

            result.Add(new LokiSeries(labels, values));
        }

        return new LokiMatrix(steps, result);
    }

    private static LokiMatrix TopK(LokiVectorAggregation aggregation, LokiMatrix inner)
    {
        var k = aggregation.K ?? 1;
        var descending = aggregation.Function == "topk";
        var groups = inner.Series.GroupBy(
            s => s.Labels.Where(kv => aggregation.Labels.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value) as IReadOnlyDictionary<string, string>,
            LabelSetComparer.Instance);

        var kept = new Dictionary<LokiSeries, double?[]>(ReferenceEqualityComparer.Instance);
        foreach (var group in groups)
        {
            var members = group.ToList();
            for (var s = 0; s < inner.Steps.Length; s++)
            {
                var ranked = members.Where(m => m.Values[s].HasValue);
                ranked = descending ? ranked.OrderByDescending(m => m.Values[s]) : ranked.OrderBy(m => m.Values[s]);
                foreach (var winner in ranked.Take(k))
                {
                    if (!kept.TryGetValue(winner, out var values))
                    {
                        kept[winner] = values = new double?[inner.Steps.Length];
                    }

                    values[s] = winner.Values[s];
                }
            }
        }

        return new LokiMatrix(inner.Steps, kept.Select(kv => new LokiSeries(kv.Key.Labels, kv.Value)).ToList());
    }

    /// <summary>
    /// <c>left op right</c>. A number applies to every sample of the other side; two vectors are matched
    /// one-to-one on identical label sets (no <c>on</c>/<c>ignoring</c>) and a sample with no partner is dropped.
    /// Division by zero is +Inf or NaN, as in Prometheus and Loki.
    /// </summary>
    internal static LokiMatrix Combine(char op, LokiMatrix left, LokiMatrix right)
    {
        if (left.IsScalar && right.IsScalar)
        {
            var value = Apply(op, left.Series[0].Values[0]!.Value, right.Series[0].Values[0]!.Value);
            var values = new double?[left.Steps.Length];
            Array.Fill(values, value);
            return new LokiMatrix(left.Steps, [new LokiSeries(new Dictionary<string, string>(), values)], IsScalar: true);
        }

        if (right.IsScalar || left.IsScalar)
        {
            var vector = left.IsScalar ? right : left;
            var scalar = (left.IsScalar ? left : right).Series[0].Values[0]!.Value;
            var mapped = vector.Series
                .Select(s => new LokiSeries(s.Labels, s.Values.Select(v => v is { } x ? left.IsScalar ? Apply(op, scalar, x) : Apply(op, x, scalar) : (double?)null).ToArray()))
                .ToList();
            return new LokiMatrix(vector.Steps, mapped);
        }

        var partners = right.Series.GroupBy(s => s.Labels, LabelSetComparer.Instance).ToDictionary(g => g.Key, g => g.First(), LabelSetComparer.Instance);
        var result = new List<LokiSeries>();
        foreach (var l in left.Series)
        {
            if (!partners.TryGetValue(l.Labels, out var r))
            {
                continue;
            }

            var values = new double?[left.Steps.Length];
            var any = false;
            for (var s = 0; s < values.Length; s++)
            {
                if (l.Values[s] is { } a && r.Values[s] is { } b)
                {
                    values[s] = Apply(op, a, b);
                    any = true;
                }
            }

            if (any)
            {
                result.Add(new LokiSeries(l.Labels, values));
            }
        }

        return new LokiMatrix(left.Steps, result);
    }

    private static double Apply(char op, double a, double b) => op switch
    {
        '+' => a + b,
        '-' => a - b,
        '*' => a * b,
        '/' => a / b,
        _ => a % b,
    };

    /// <summary>Equality of label sets, order-independent.</summary>
    private sealed class LabelSetComparer : IEqualityComparer<IReadOnlyDictionary<string, string>>
    {
        public static readonly LabelSetComparer Instance = new();

        public bool Equals(IReadOnlyDictionary<string, string>? x, IReadOnlyDictionary<string, string>? y)
        {
            if (x is null || y is null || x.Count != y.Count)
            {
                return false;
            }

            foreach (var (key, value) in x)
            {
                if (!y.TryGetValue(key, out var other) || !string.Equals(value, other, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        public int GetHashCode(IReadOnlyDictionary<string, string> labels)
        {
            // Order-independent: XOR of per-entry hashes.
            var hash = 0;
            foreach (var (key, value) in labels)
            {
                hash ^= HashCode.Combine(key, value);
            }

            return hash;
        }
    }
}
