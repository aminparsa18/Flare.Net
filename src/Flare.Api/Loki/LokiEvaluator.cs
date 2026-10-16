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

/// <summary>Where <see cref="LokiEvaluator"/> gets its data: <see cref="LokiQueryService"/> in production, a fake in tests.</summary>
internal interface ILokiLeafSource
{
    /// <summary>The label names a stream carries when an aggregation does not narrow them.</summary>
    Task<IReadOnlyList<string>> StreamLabelNamesAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<LokiBucketRow>> FetchBucketsAsync(LokiLeafRequest request, CancellationToken cancellationToken);
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
        var dimensions = needed.Exact ?? (await source.StreamLabelNamesAsync(ct)).Union(needed.Extra).ToList();
        var rangeMs = (long)range.Range.TotalMilliseconds;
        var rangeSeconds = range.Range.TotalSeconds;
        var measure = range.Function.StartsWith("bytes", StringComparison.Ordinal) ? LokiMeasure.Bytes : LokiMeasure.Count;
        var perSecond = range.Function is "rate" or "bytes_rate";

        if (instant)
        {
            var end = steps[^1];
            var rows = await source.FetchBucketsAsync(new LokiLeafRequest(range.Selector, dimensions, measure, end - rangeMs, end, BucketMs: 0), ct);
            var grouped = GroupRows(rows, dimensions);
            var series = grouped.Select(g => new LokiSeries(g.Key, [perSecond ? g.Value.Sum(r => r.Value) / rangeSeconds : g.Value.Sum(r => r.Value)])).ToList();
            return new LokiMatrix(steps, series);
        }

        var bucketMs = ChooseBucketMs(rangeMs, stepMs, steps[0], steps[^1]);
        var bucketsPerWindow = (int)Math.Max(1, Math.Round((double)rangeMs / bucketMs));
        // The bucket grid starts one window before the first step, so every step falls on a bucket edge.
        var firstBucket = steps[0] - bucketsPerWindow * bucketMs;
        var bucketCount = (int)((steps[^1] - firstBucket) / bucketMs);
        var lastBucketEnd = firstBucket + bucketCount * bucketMs;
        var request = new LokiLeafRequest(range.Selector, dimensions, measure, firstBucket, lastBucketEnd, bucketMs);
        var fetched = GroupRows(await source.FetchBucketsAsync(request, ct), dimensions);

        var result = new List<LokiSeries>(fetched.Count);
        foreach (var (labels, rows) in fetched)
        {
            // Prefix sums over a dense bucket array: each step's window is two subtractions.
            var sums = new double[bucketCount + 1];
            var present = new int[bucketCount + 1];
            foreach (var row in rows)
            {
                var index = (row.BucketMs - firstBucket) / bucketMs;
                if (index >= 0 && index < bucketCount)
                {
                    sums[index + 1] += row.Value;
                    present[index + 1]++;
                }
            }

            for (var i = 1; i <= bucketCount; i++)
            {
                sums[i] += sums[i - 1];
                present[i] += present[i - 1];
            }

            var values = new double?[steps.Length];
            for (var s = 0; s < steps.Length; s++)
            {
                // The window is the buckets that end at or before the step.
                var hi = Math.Min((int)((steps[s] - firstBucket) / bucketMs), bucketCount);
                var lo = Math.Max(0, hi - bucketsPerWindow);
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

    private static Dictionary<IReadOnlyDictionary<string, string>, List<LokiBucketRow>> GroupRows(IReadOnlyList<LokiBucketRow> rows, IReadOnlyList<string> dimensions)
    {
        var groups = new Dictionary<IReadOnlyDictionary<string, string>, List<LokiBucketRow>>(LabelSetComparer.Instance);
        foreach (var row in rows)
        {
            var labels = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 0; i < dimensions.Count && i < row.DimensionValues.Length; i++)
            {
                // Loki drops empty labels from a series.
                if (row.DimensionValues[i].Length > 0)
                {
                    labels[dimensions[i]] = row.DimensionValues[i];
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
        var innerNeed = aggregation.Function == "sum" && !aggregation.Without && aggregation.Inner is LokiRangeAggregation
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
