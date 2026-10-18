using System.Text.RegularExpressions;
using Flare.Api.Model;
using Flare.Api.Query;

namespace Flare.Api.Prometheus;

internal sealed record PromSample(long TimestampMs, double Value);

internal sealed class PromSeries(Dictionary<string, string> labels)
{
    public Dictionary<string, string> Labels { get; } = labels;

    public List<PromSample> Samples { get; } = [];
}

internal sealed record PromEvalResult(List<PromSeries> Series, bool IsScalar, List<string> Warnings);

/// <summary>A series as fetched from <see cref="IMetricQueryService"/>, already carrying Prometheus-style labels.</summary>
/// <summary>An exemplar and the series it belongs to; <see cref="Labels"/> carry <c>trace_id</c> (and <c>span_id</c> when known).</summary>
internal sealed record PromExemplar(Dictionary<string, string> SeriesLabels, Dictionary<string, string> Labels, double Value, long TimestampMs);

internal sealed record PromRawSeries(Dictionary<string, string> Labels, IReadOnlyList<MetricSeriesPoint> Points);

/// <summary>
/// Evaluates the <see cref="PromQlParser"/> subset over Flare's metric tables by translating each
/// selector into an <see cref="IMetricQueryService"/> request and folding the buckets it returns
/// (ADR-0109). Reuses the existing reset-aware <c>increase()</c> and temporality-aware histogram SQL
/// rather than duplicating it, so a Prometheus <c>rate()</c> and a dashboard's Rate mode agree.
/// </summary>
/// <remarks>
/// <para>
/// <b>Time model.</b> Every selector is fetched with a bucket width equal to the query step, so a
/// sample's timestamp is its bucket's start (aligned to a multiple of the step from the epoch), not
/// <c>start + k*step</c>. A range function's <c>[5m]</c> window slides over <c>round(range/step)</c>
/// buckets (at least one): <c>rate</c> is the summed increase over the window divided by the window's
/// seconds, <c>increase</c> is that rate times the range. A bare gauge selector is the bucket average;
/// there is no staleness lookback beyond the instant-query default the endpoint applies.
/// </para>
/// <para>
/// <b>Aggregation happens here, not in SQL.</b> The series query groups by at most one attribute key;
/// <c>sum by (a, b)</c> needs any label set, so series are fetched at full label granularity and
/// folded in memory. The query service caps a request at <see cref="MaxSeries"/> series; hitting the
/// cap adds a warning instead of a silent partial answer.
/// </para>
/// </remarks>
internal sealed class PromEvaluator(IMetricQueryService metrics, IMetricExemplarService? exemplars = null)
{
    /// <summary>The metric query service's own series cap (<c>MetricSeriesQueryBuilder.MaxTopN</c>).</summary>
    public const int MaxSeries = 200;

    /// <summary>Prometheus's own per-query resolution limit.</summary>
    public const int MaxPoints = 11_000;

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);

    /// <param name="QueryStartMs">What <c>@ start()</c> means: the start of the query range, not of a window being evaluated inside it.</param>
    /// <param name="QueryEndMs">What <c>@ end()</c> means.</param>
    private sealed record Context(long StartMs, long EndMs, long StepMs, List<string> Warnings, long QueryStartMs, long QueryEndMs)
    {
        public int StepSeconds => (int)(StepMs / 1000);

        public IEnumerable<long> Timestamps()
        {
            for (var t = StartMs / StepMs * StepMs; t <= EndMs; t += StepMs)
            {
                yield return t;
            }
        }
    }

    /// <param name="evaluationTime">Set for an instant query, whose window reaches back for a lookback: <c>@ start()</c> and <c>@ end()</c> then both mean this time rather than the window's edges.</param>
    public async Task<PromEvalResult> EvaluateAsync(
        PromExpr expr, DateTimeOffset start, DateTimeOffset end, TimeSpan step, CancellationToken cancellationToken, DateTimeOffset? evaluationTime = null)
    {
        // Whole seconds only: the metric query service buckets with an INTERVAL n SECOND.
        var stepSeconds = Math.Max(1, (long)Math.Round(step.TotalSeconds));
        if (end < start)
        {
            throw new PromQlException("end timestamp must not be before start time.");
        }

        if ((end - start).TotalSeconds / stepSeconds > MaxPoints)
        {
            throw new PromQlException(
                $"exceeded maximum resolution of {MaxPoints} points per timeseries. Try decreasing the query resolution (?step=XX).");
        }

        var ctx = new Context(
            start.ToUnixTimeMilliseconds(),
            end.ToUnixTimeMilliseconds(),
            stepSeconds * 1000,
            [],
            (evaluationTime ?? start).ToUnixTimeMilliseconds(),
            (evaluationTime ?? end).ToUnixTimeMilliseconds());
        var series = await EvalAsync(expr, ctx, cancellationToken);
        return new PromEvalResult(series, PromExprs.IsScalar(expr), ctx.Warnings);
    }

    /// <summary>Series matching a selector in a window - backs <c>/api/v1/series</c>, <c>/labels</c> and <c>/label/&lt;name&gt;/values</c>.</summary>
    public async Task<IReadOnlyList<PromRawSeries>> SelectSeriesAsync(
        PromSelector selector, DateTimeOffset from, DateTimeOffset to, List<string> warnings, CancellationToken cancellationToken)
    {
        if (selector.IsMultiMetric)
        {
            var all = new List<PromRawSeries>();
            foreach (var one in await ExpandAsync(selector, from, to, warnings, cancellationToken))
            {
                all.AddRange(await SelectSeriesAsync(one, from, to, warnings, cancellationToken));
            }

            return all;
        }

        var metric = await ResolveAsync(selector, from, to, cancellationToken);
        if (metric is null)
        {
            return [];
        }

        var bucket = (int)Math.Max(1, Math.Ceiling((to - from).TotalSeconds));
        var type = metric.Kind == PromMetricKind.Counter ? MetricPointType.Sum : metric.Type;
        return await FetchAsync(selector, metric, from, to, bucket, type, treatAsCounter: false, includeBuckets: false, warnings, cancellationToken);
    }

    public async Task<IReadOnlyList<MetricNameInfo>> KnownMetricsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken) =>
        (await metrics.GetNamesAsync(new MetricNamesRequest { From = from, To = to }, cancellationToken)).Metrics;

    private async Task<List<PromSeries>> EvalAsync(PromExpr expr, Context ctx, CancellationToken cancellationToken)
    {
        switch (expr)
        {
            case PromNumber number:
                return [ScalarSeries(number, ctx)];

            // A __name__ pattern is expanded into one selector per matching metric first (see EvalMultiMetricAsync).
            case PromSelector { IsMultiMetric: true } selector:
                return await EvalMultiMetricAsync(selector, s => s, ctx, cancellationToken);

            case PromRangeFunction { Selector.IsMultiMetric: true } fn:
                return await EvalMultiMetricAsync(fn.Selector, s => fn with { Selector = s }, ctx, cancellationToken);

            case PromTimestamp { Inner: PromSelector { IsMultiMetric: true } selector }:
                return await EvalMultiMetricAsync(selector, s => new PromTimestamp(s), ctx, cancellationToken);

            case PromTimestamp { Inner: PromSelector { At: { } at } selector }:
                return await PinnedAsync(at, ctx, async c => WithoutNames(await EvalSelectorAsync(selector, c, cancellationToken, MetricGaugeAggregate.LastTime)));

            case PromTimestamp { Inner: PromSelector selector }:
                return WithoutNames(await EvalSelectorAsync(selector, ctx, cancellationToken, MetricGaugeAggregate.LastTime));

            case PromTimestamp timestamp:
                return StepTimestamps(await EvalAsync(timestamp.Inner, ctx, cancellationToken));

            case PromSelector { At: { } at } selector:
                return await PinnedAsync(at, ctx, c => EvalSelectorAsync(selector, c, cancellationToken));

            case PromSelector selector:
                return await EvalSelectorAsync(selector, ctx, cancellationToken);

            case PromRangeFunction { Selector.At: { } at } fn:
                return await PinnedAsync(at, ctx, c => EvalRangeFunctionAsync(fn, c, cancellationToken));

            case PromRangeFunction fn:
                return await EvalRangeFunctionAsync(fn, ctx, cancellationToken);

            case PromSubqueryFunction { Subquery.At: { } at } fn:
                return await PinnedAsync(at, ctx, c => EvalSubqueryFunctionAsync(fn, c, cancellationToken));

            case PromSubqueryFunction fn:
                return await EvalSubqueryFunctionAsync(fn, ctx, cancellationToken);

            case PromTime:
                return [TimeSeries(ctx)];

            case PromScalar scalar:
                return [Scalar(await EvalAsync(scalar.Inner, ctx, cancellationToken), ctx)];

            case PromSort sort:
                return PromFunctions.Sort(sort, await EvalAsync(sort.Inner, ctx, cancellationToken));

            case PromAggregation agg:
                return Aggregate(agg, await EvalAsync(agg.Inner, ctx, cancellationToken));

            case PromFunction fn:
                return PromFunctions.Apply(fn, await EvalAsync(fn.Argument, ctx, cancellationToken));

            case PromLabelFunction lf:
                return PromLabelFunctions.Apply(lf, await EvalAsync(lf.Inner, ctx, cancellationToken));

            case PromAbsent absent:
                return Absent(absent, await EvalAsync(absent.Inner, ctx, cancellationToken), ctx);

            case PromBinary binary:
                return await EvalBinaryAsync(binary, ctx, cancellationToken);

            case PromHistogramQuantile hq when HistogramAt(hq.Inner) is { } at:
                return await PinnedAsync(at, ctx, c => EvalHistogramAsync("histogram_quantile", hq.Inner, w => Quantile(hq.Quantile, w), c, cancellationToken));

            case PromHistogramQuantile hq:
                return await EvalHistogramAsync("histogram_quantile", hq.Inner, w => Quantile(hq.Quantile, w), ctx, cancellationToken);

            case PromHistogramFraction hf when HistogramAt(hf.Inner) is { } at:
                return await PinnedAsync(at, ctx, c => EvalHistogramAsync("histogram_fraction", hf.Inner, w => PromWindowMath.HistogramFraction(hf.Lower, hf.Upper, w), c, cancellationToken));

            case PromHistogramFraction hf:
                return await EvalHistogramAsync("histogram_fraction", hf.Inner, w => PromWindowMath.HistogramFraction(hf.Lower, hf.Upper, w), ctx, cancellationToken);

            default:
                throw new PromQlException("unsupported expression.");
        }
    }

    private static PromAt? HistogramAt(PromExpr inner) => inner switch
    {
        PromRangeFunction fn => fn.Selector.At,
        PromAggregation { Inner: PromRangeFunction fn } => fn.Selector.At,
        _ => null,
    };

    private static List<PromSeries> WithoutNames(List<PromSeries> series) =>
        series.Select(s => new PromSeries(WithoutName(s.Labels)).With(s.Samples)).ToList();

    // timestamp() of anything but a selector: each sample's own step, in seconds.
    private static List<PromSeries> StepTimestamps(List<PromSeries> series)
    {
        var result = new List<PromSeries>(series.Count);
        foreach (var s in series)
        {
            var stamped = new PromSeries(WithoutName(s.Labels));
            stamped.Samples.AddRange(s.Samples.Select(x => new PromSample(x.TimestampMs, x.TimestampMs / 1000.0)));
            result.Add(stamped);
        }

        return result;
    }

    /// <summary>How many metrics one <c>__name__</c> pattern is evaluated against; more are skipped with a warning.</summary>
    private const int MaxExpandedMetrics = 50;

    /// <summary>
    /// A selector that names its metrics with a pattern (<c>{__name__=~"http_.*"}</c>) is evaluated once per matching
    /// metric and the series are concatenated, each keeping its own <c>__name__</c>. A metric the surrounding
    /// expression cannot read (a counter outside <c>rate()</c>, a histogram under a gauge function) is skipped with a
    /// warning; if every match fails, the first error is the answer.
    /// </summary>
    private async Task<List<PromSeries>> EvalMultiMetricAsync(
        PromSelector selector, Func<PromSelector, PromExpr> rebuild, Context ctx, CancellationToken cancellationToken)
    {
        var shift = OffsetMs(selector.Offset, ctx);
        var from = DateTimeOffset.FromUnixTimeMilliseconds(ctx.StartMs - shift) - (selector.Range ?? TimeSpan.Zero);
        var to = DateTimeOffset.FromUnixTimeMilliseconds(ctx.EndMs - shift).AddMilliseconds(1);
        var concrete = await ExpandAsync(selector, from, to, ctx.Warnings, cancellationToken);

        var result = new List<PromSeries>();
        PromQlException? first = null;
        var failed = 0;
        foreach (var one in concrete)
        {
            try
            {
                result.AddRange(await EvalAsync(rebuild(one), ctx, cancellationToken));
            }
            catch (PromQlException ex)
            {
                first ??= ex;
                failed++;
                ctx.Warnings.Add($"skipped '{one.MetricName}': {ex.Message}");
            }
        }

        return failed > 0 && failed == concrete.Count ? throw first! : result;
    }

    /// <summary>The metrics a <c>__name__</c> pattern matches, as one single-name selector each (alphabetical, at most <see cref="MaxExpandedMetrics"/>).</summary>
    private async Task<List<PromSelector>> ExpandAsync(
        PromSelector selector, DateTimeOffset from, DateTimeOffset to, List<string> warnings, CancellationToken cancellationToken)
    {
        var known = await KnownMetricsAsync(from, to, cancellationToken);
        var names = known
            .SelectMany(i => PromNames.PromNamesFor(i.MetricName, i.Unit, i.Type))
            // The bare histogram name is an alias of _bucket; a pattern should list the metric once.
            .Where(n => !(n.Kind == PromMetricKind.HistogramBucket && !n.PromName.EndsWith("_bucket", StringComparison.Ordinal)))
            .Select(n => n.PromName)
            .Distinct(StringComparer.Ordinal)
            .Where(n => selector.NameMatchers.All(m => Matches(m, n)))
            .Order(StringComparer.Ordinal)
            .ToList();
        if (names.Count > MaxExpandedMetrics)
        {
            warnings.Add($"the __name__ pattern matched {names.Count} metrics; only the first {MaxExpandedMetrics} (alphabetical) are included. Narrow the pattern.");
            names = names.Take(MaxExpandedMetrics).ToList();
        }

        var labelMatchers = selector.Matchers.Where(m => m.Name != "__name__").ToList();
        return names.Select(n => selector with { MetricName = n, Matchers = labelMatchers }).ToList();
    }

    /// <summary>How far back a pinned (<c>@</c>) evaluation looks for its newest sample; the instant-query lookback (<c>PrometheusEndpoints.InstantLookback</c>).</summary>
    private static readonly long PinLookbackMs = (long)TimeSpan.FromMinutes(5).TotalMilliseconds;

    /// <summary>
    /// The <c>@</c> modifier: evaluate as an instant query at the pinned time (the newest sample at or before it,
    /// within the five-minute lookback an instant query uses) and repeat that sample at every step of the real
    /// query. Offsets inside still count back from the pinned time.
    /// </summary>
    private static async Task<List<PromSeries>> PinnedAsync(PromAt at, Context ctx, Func<Context, Task<List<PromSeries>>> evaluate)
    {
        var atMs = at.Kind switch
        {
            PromAtKind.Start => ctx.QueryStartMs,
            PromAtKind.End => ctx.QueryEndMs,
            _ => at.TimestampMs,
        };

        var pinned = await evaluate(ctx with { StartMs = atMs - Math.Max(PinLookbackMs, ctx.StepMs), EndMs = atMs });
        var timestamps = ctx.Timestamps().ToList();
        var result = new List<PromSeries>(pinned.Count);
        foreach (var s in pinned)
        {
            if (s.Samples.Count == 0)
            {
                continue;
            }

            var value = s.Samples.MaxBy(x => x.TimestampMs)!.Value;
            var series = new PromSeries(s.Labels);
            series.Samples.AddRange(timestamps.Select(ts => new PromSample(ts, value)));
            result.Add(series);
        }

        return result;
    }

    // 1 at every step the inner vector has no sample, as Prometheus does; a never-resolved metric is absent everywhere.
    private static List<PromSeries> Absent(PromAbsent absent, List<PromSeries> inner, Context ctx)
    {
        var present = inner.SelectMany(s => s.Samples.Select(x => x.TimestampMs)).ToHashSet();
        var series = new PromSeries(absent.Labels.ToDictionary(kv => kv.Key, kv => kv.Value));
        series.Samples.AddRange(ctx.Timestamps().Where(t => !present.Contains(t)).Select(t => new PromSample(t, 1)));
        return series.Samples.Count == 0 ? [] : [series];
    }

    /// <summary>
    /// <c>time()</c>: each step's timestamp in seconds. A step is stamped with its bucket start, but an instant
    /// query (or a range query whose start equals its end) is asked about one moment, so its last step reads that moment.
    /// </summary>
    private static PromSeries TimeSeries(Context ctx)
    {
        var series = new PromSeries([]);
        var timestamps = ctx.Timestamps().ToList();
        var exactEnd = ctx.QueryStartMs == ctx.QueryEndMs && ctx.EndMs == ctx.QueryEndMs;
        for (var i = 0; i < timestamps.Count; i++)
        {
            var ms = exactEnd && i == timestamps.Count - 1 ? ctx.QueryEndMs : timestamps[i];
            series.Samples.Add(new PromSample(timestamps[i], ms / 1000.0));
        }

        return series;
    }

    /// <summary><c>scalar(v)</c>: v's value at each step where it has exactly one series, NaN otherwise.</summary>
    private static PromSeries Scalar(List<PromSeries> vector, Context ctx)
    {
        var byTimestamp = new Dictionary<long, (int Count, double Value)>();
        foreach (var sample in vector.SelectMany(s => s.Samples))
        {
            byTimestamp[sample.TimestampMs] = byTimestamp.TryGetValue(sample.TimestampMs, out var seen)
                ? (seen.Count + 1, sample.Value)
                : (1, sample.Value);
        }

        var series = new PromSeries([]);
        series.Samples.AddRange(ctx.Timestamps().Select(t =>
            new PromSample(t, byTimestamp.TryGetValue(t, out var at) && at.Count == 1 ? at.Value : double.NaN)));
        return series;
    }

    private static PromSeries ScalarSeries(PromNumber number, Context ctx)
    {
        var series = new PromSeries([]);
        series.Samples.AddRange(ctx.Timestamps().Select(t => new PromSample(t, number.Value)));
        return series;
    }

    private async Task<List<PromSeries>> EvalBinaryAsync(PromBinary b, Context ctx, CancellationToken cancellationToken)
    {
        // Scalar op scalar where one side moves with time (time(), scalar(v)): constants were folded at parse time.
        if (PromExprs.IsScalar(b.Left) && PromExprs.IsScalar(b.Right))
        {
            return PromBinaryOperations.ApplyScalars(
                b, await EvalAsync(b.Left, ctx, cancellationToken), await EvalAsync(b.Right, ctx, cancellationToken));
        }

        if (b.Left is not PromNumber && PromExprs.IsScalar(b.Left))
        {
            return PromBinaryOperations.ApplyScalar(
                b, await EvalAsync(b.Right, ctx, cancellationToken), await EvalAsync(b.Left, ctx, cancellationToken), scalarOnLeft: true);
        }

        if (b.Right is not PromNumber && PromExprs.IsScalar(b.Right))
        {
            return PromBinaryOperations.ApplyScalar(
                b, await EvalAsync(b.Left, ctx, cancellationToken), await EvalAsync(b.Right, ctx, cancellationToken), scalarOnLeft: false);
        }

        // Scalars are handled as constants, not as series, so `x * 100` costs one metric query.
        if (b.Left is PromNumber leftScalar)
        {
            return PromBinaryOperations.ApplyScalar(b, await EvalAsync(b.Right, ctx, cancellationToken), leftScalar.Value, scalarOnLeft: true);
        }

        if (b.Right is PromNumber rightScalar)
        {
            return PromBinaryOperations.ApplyScalar(b, await EvalAsync(b.Left, ctx, cancellationToken), rightScalar.Value, scalarOnLeft: false);
        }

        var left = await EvalAsync(b.Left, ctx, cancellationToken);
        var right = await EvalAsync(b.Right, ctx, cancellationToken);
        return PromBinaryOperations.ApplyVectors(b, left, right);
    }

    /// <summary>
    /// An <c>offset</c> as a whole number of steps. Buckets are aligned to the step, so shifting by a
    /// multiple of it keeps an offset series on the same timestamps as the rest of the query; an offset
    /// that is not a multiple is rounded to the nearest step.
    /// </summary>
    private static long OffsetMs(TimeSpan offset, Context ctx) =>
        (long)Math.Round(offset.TotalMilliseconds / ctx.StepMs) * ctx.StepMs;

    private async Task<List<PromSeries>> EvalSelectorAsync(
        PromSelector selector, Context ctx, CancellationToken cancellationToken, MetricGaugeAggregate? aggregate = null)
    {
        var shift = OffsetMs(selector.Offset, ctx);
        var from = DateTimeOffset.FromUnixTimeMilliseconds(ctx.StartMs - shift);
        var to = DateTimeOffset.FromUnixTimeMilliseconds(ctx.EndMs - shift).AddMilliseconds(1);
        var metric = await ResolveAsync(selector, from, to, cancellationToken);
        if (metric is null)
        {
            return [];
        }

        if (metric.Kind != PromMetricKind.Gauge)
        {
            throw new PromQlException(metric.Kind switch
            {
                PromMetricKind.Counter => $"'{metric.PromName}' is a counter; Flare stores increases, not running totals, so wrap it in rate() or increase().",
                PromMetricKind.HistogramBucket => $"'{metric.PromName}' is a histogram; use histogram_quantile(q, rate({metric.PromName}[5m])).",
                _ => $"'{metric.PromName}' is a histogram aggregate; wrap it in rate() or increase().",
            });
        }

        var raw = await FetchAsync(selector, metric, from, to, ctx.StepSeconds, metric.Type, false, false, ctx.Warnings, cancellationToken, aggregate);
        return raw.Select(r =>
        {
            var series = new PromSeries(r.Labels);
            series.Samples.AddRange(r.Points
                .Where(p => p.Value is not null)
                .Select(p => new PromSample(p.BucketStart.ToUnixTimeMilliseconds() + shift, p.Value!.Value)));
            return series;
        }).Where(s => s.Samples.Count > 0).ToList();
    }

    private async Task<List<PromSeries>> EvalRangeFunctionAsync(PromRangeFunction fn, Context ctx, CancellationToken cancellationToken)
    {
        var range = fn.Selector.Range!.Value;
        var shift = OffsetMs(fn.Selector.Offset, ctx);
        var from = DateTimeOffset.FromUnixTimeMilliseconds(ctx.StartMs - shift) - range;
        var to = DateTimeOffset.FromUnixTimeMilliseconds(ctx.EndMs - shift).AddMilliseconds(1);
        var metric = await ResolveAsync(fn.Selector, from, to, cancellationToken);
        if (metric is null)
        {
            return [];
        }

        if (metric.Kind == PromMetricKind.HistogramBucket)
        {
            throw new PromQlException($"{fn.Name}({metric.PromName}[...]) is only supported inside histogram_quantile; use {metric.PromName[..^"_bucket".Length]}_count or _sum for a plain rate.");
        }

        // The *_over_time functions read gauge samples; the counter functions read increases.
        var overTime = PromQlParser.OverTimeFunctions.Contains(fn.Name);
        if (overTime && metric.Kind != PromMetricKind.Gauge)
        {
            throw new PromQlException($"{fn.Name}() applies to gauges; '{metric.PromName}' is not one. Use rate() or increase() for counters and histograms.");
        }

        // A gauge under rate()/increase() is an untyped Prometheus counter (`*_total` scraped without
        // type metadata arrives as a Gauge) - the same reading ADR-0066's "treat as counter" gives it.
        var treatAsCounter = metric.Kind == PromMetricKind.Gauge && !overTime;
        var type = metric.Kind == PromMetricKind.Counter ? MetricPointType.Sum : metric.Type;
        var raw = await FetchAsync(
            fn.Selector, metric, from, to, ctx.StepSeconds, type, treatAsCounter, false, ctx.Warnings, cancellationToken, GaugeAggregateFor(fn.Name));

        var buckets = WindowBuckets(range, ctx);
        var windowSeconds = buckets * ctx.StepSeconds;
        var result = new List<PromSeries>();
        foreach (var r in raw)
        {
            var values = new Dictionary<long, double>();
            foreach (var p in r.Points)
            {
                var v = metric.Kind switch
                {
                    PromMetricKind.HistogramCount => (double?)p.Count,
                    PromMetricKind.HistogramSum => p.Sum,
                    _ => p.Value,
                };
                if (v is { } value)
                {
                    values[p.BucketStart.ToUnixTimeMilliseconds() + shift] = value;
                }
            }

            var series = new PromSeries(WithoutName(r.Labels));
            var window = new List<double>(buckets);
            var regression = fn.Name is "deriv" or "predict_linear";
            var times = new List<long>(regression ? buckets : 0);
            foreach (var t in ctx.Timestamps())
            {
                // Oldest bucket first, so the last element is the newest.
                window.Clear();
                times.Clear();
                for (var i = buckets - 1; i >= 0; i--)
                {
                    if (values.TryGetValue(t - i * ctx.StepMs, out var v))
                    {
                        window.Add(v);
                        if (regression)
                        {
                            times.Add(BucketCenterMs(t - i * ctx.StepMs, ctx));
                        }
                    }
                }

                if (window.Count == 0)
                {
                    continue;
                }

                double? value = fn.Name switch
                {
                    "rate" => window.Sum() / windowSeconds,
                    "increase" => window.Sum() / windowSeconds * range.TotalSeconds,
                    // The newest bucket's increase per second: the bucketed analogue of the last two raw samples.
                    "irate" => window[^1] / ctx.StepSeconds,
                    "avg_over_time" => window.Average(),
                    "min_over_time" => window.Min(),
                    "max_over_time" => window.Max(),
                    // Each bucket holds its own sum, count, change or reset total, so the window's is their total.
                    "sum_over_time" or "count_over_time" or "delta" or "changes" or "resets" => window.Sum(),
                    "deriv" or "predict_linear" => Regress(fn, window, times, Math.Min(t + ctx.StepMs, ctx.EndMs)),
                    // The newest bucket holds the newest point's step from its predecessor.
                    "idelta" => window[^1],
                    "holt_winters" => PromWindowMath.HoltWinters(window, fn.Parameter!.Value, fn.Parameter2!.Value),
                    _ => window[^1],
                };

                if (value is { } v2)
                {
                    series.Samples.Add(new PromSample(t, v2));
                }
            }

            if (series.Samples.Count > 0)
            {
                result.Add(series);
            }
        }

        return result;
    }

    // Gauge buckets are reduced in SQL from the raw points, so a window of min/max/sum/count buckets is exact (ADR-0245).
    private static MetricGaugeAggregate? GaugeAggregateFor(string function) => function switch
    {
        "min_over_time" => MetricGaugeAggregate.Min,
        "max_over_time" => MetricGaugeAggregate.Max,
        "sum_over_time" => MetricGaugeAggregate.Sum,
        "count_over_time" => MetricGaugeAggregate.Count,
        "delta" => MetricGaugeAggregate.Delta,
        "changes" => MetricGaugeAggregate.Changes,
        "resets" => MetricGaugeAggregate.Resets,
        "idelta" => MetricGaugeAggregate.LastDelta,
        _ => null,
    };

    /// <summary>
    /// Where a bucket's average sits on the time axis for a regression: its middle, or - for the newest bucket,
    /// which holds data only up to the query's end - the middle of what it holds.
    /// </summary>
    private static long BucketCenterMs(long bucketStartMs, Context ctx) =>
        bucketStartMs + ctx.StepMs > ctx.EndMs ? (bucketStartMs + Math.Max(ctx.EndMs, bucketStartMs)) / 2 : bucketStartMs + ctx.StepMs / 2;

    /// <summary>
    /// <c>deriv</c> (the slope, per second) and <c>predict_linear</c> (the line's value <c>Parameter</c> seconds after
    /// <paramref name="evaluatedAtMs"/>) over the points; null with fewer than two.
    /// </summary>
    private static double? Regress(PromRangeFunction fn, List<double> values, List<long> timesMs, long evaluatedAtMs) =>
        Regress(fn.Name, fn.Parameter, values, timesMs, evaluatedAtMs);

    private static double? Regress(string name, double? ahead, List<double> values, List<long> timesMs, long evaluatedAtMs)
    {
        var xs = timesMs.Select(t => (t - evaluatedAtMs) / 1000.0).ToList();
        if (PromWindowMath.Fit(xs, values) is not { } fit)
        {
            return null;
        }

        return name == "deriv" ? fit.Slope : fit.Intercept + fit.Slope * (ahead ?? 0);
    }

    /// <summary>
    /// <c>*_over_time(expr[range:resolution])</c>. The inner expression is evaluated over its own grid
    /// (<c>resolution</c>, the query step when omitted) covering the window of every outer step, then each outer
    /// step reduces the inner samples in <c>(t - range, t]</c>. Inner samples are bucket starts like everywhere
    /// else, so a window can include the bucket that starts at its own end. An empty window has no sample.
    /// </summary>
    private async Task<List<PromSeries>> EvalSubqueryFunctionAsync(PromSubqueryFunction fn, Context ctx, CancellationToken cancellationToken)
    {
        var sq = fn.Subquery;
        var resolutionMs = sq.Resolution is { } r
            ? Math.Max(1, (long)Math.Round(r.TotalSeconds)) * 1000
            : ctx.StepMs;
        var rangeMs = (long)sq.Range.TotalMilliseconds;
        var shift = OffsetMs(sq.Offset, ctx);

        var innerStart = ctx.StartMs - shift - rangeMs;
        var innerEnd = ctx.EndMs - shift;
        if ((innerEnd - innerStart) / resolutionMs > MaxPoints)
        {
            throw new PromQlException(
                $"exceeded maximum resolution of {MaxPoints} points per timeseries in a subquery. Try a larger subquery resolution, e.g. [1h:5m].");
        }

        var inner = await EvalAsync(sq.Inner, ctx with { StartMs = innerStart, EndMs = innerEnd, StepMs = resolutionMs }, cancellationToken);

        var result = new List<PromSeries>();
        foreach (var s in inner)
        {
            var samples = s.Samples.OrderBy(x => x.TimestampMs).ToList();
            var series = new PromSeries(WithoutName(s.Labels));
            var lo = 0;
            var hi = 0;
            foreach (var t in ctx.Timestamps())
            {
                // The window (t - shift - range, t - shift]; both edges only ever move forward with t.
                var windowEnd = t - shift;
                while (hi < samples.Count && samples[hi].TimestampMs <= windowEnd)
                {
                    hi++;
                }

                while (lo < hi && samples[lo].TimestampMs <= windowEnd - rangeMs)
                {
                    lo++;
                }

                if (lo == hi)
                {
                    continue;
                }

                var points = samples.GetRange(lo, hi - lo);
                var window = points.Select(x => x.Value).ToList();
                double? value = fn.Name switch
                {
                    "avg_over_time" => window.Average(),
                    "min_over_time" => window.Min(),
                    "max_over_time" => window.Max(),
                    "sum_over_time" => window.Sum(),
                    "count_over_time" => window.Count,
                    // Like Prometheus, delta needs two samples and is last minus first (no extrapolation to the window edges).
                    "delta" => window.Count >= 2 ? window[^1] - window[0] : null,
                    "changes" => PromWindowMath.Changes(window),
                    "resets" => PromWindowMath.Resets(window),
                    "deriv" or "predict_linear" => Regress(fn.Name, fn.Parameter, window, points.Select(x => x.TimestampMs).ToList(), windowEnd),
                    "idelta" => window.Count >= 2 ? window[^1] - window[^2] : null,
                    "holt_winters" => PromWindowMath.HoltWinters(window, fn.Parameter!.Value, fn.Parameter2!.Value),
                    _ => window[^1],
                };

                if (value is { } v)
                {
                    series.Samples.Add(new PromSample(t, v));
                }
            }

            if (series.Samples.Count > 0)
            {
                result.Add(series);
            }
        }

        return result;
    }

    /// <summary>
    /// <c>histogram_quantile</c> and <c>histogram_fraction</c> share one shape: the buckets of
    /// <c>rate(m[r])</c>, optionally summed, folded over the window and reduced to one number by <paramref name="reduce"/>.
    /// </summary>
    private async Task<List<PromSeries>> EvalHistogramAsync(
        string function, PromExpr argument, Func<SortedDictionary<(double Lower, double Upper), double>, double?> reduce, Context ctx, CancellationToken cancellationToken)
    {
        // Accepted shapes: f(rate(m[r])) and f(sum [by|without (...)] (rate(m[r]))). Anything richer is rejected, not approximated.
        PromRangeFunction fn;
        IReadOnlyList<string> groupLabels = [];
        var without = true;
        switch (argument)
        {
            case PromRangeFunction direct:
                fn = direct;
                break;
            case PromAggregation { Op: "sum", Inner: PromRangeFunction inner } agg:
                fn = inner;
                groupLabels = agg.Labels;
                without = agg.Without;
                break;
            default:
                throw new PromQlException(
                    $"{function}'s argument must be rate()/increase() of a histogram, optionally wrapped in sum [by|without (...)].");
        }

        if (fn.Name is not ("rate" or "increase"))
        {
            throw new PromQlException($"{function} does not accept {fn.Name}(); use rate() or increase().");
        }

        var range = fn.Selector.Range!.Value;
        var shift = OffsetMs(fn.Selector.Offset, ctx);
        var from = DateTimeOffset.FromUnixTimeMilliseconds(ctx.StartMs - shift) - range;
        var to = DateTimeOffset.FromUnixTimeMilliseconds(ctx.EndMs - shift).AddMilliseconds(1);
        var metric = await ResolveAsync(fn.Selector, from, to, cancellationToken);
        if (metric is null)
        {
            return [];
        }

        if (metric.Kind != PromMetricKind.HistogramBucket)
        {
            throw new PromQlException($"{function} needs a histogram metric; '{metric.PromName}' is not one.");
        }

        var raw = await FetchAsync(fn.Selector, metric, from, to, ctx.StepSeconds, metric.Type, false, includeBuckets: true, ctx.Warnings, cancellationToken);
        var buckets = WindowBuckets(range, ctx);

        // Bucket (lower, upper) -> count per series per bucket timestamp, merged by group.
        var groups = new Dictionary<string, (Dictionary<string, string> Labels, Dictionary<long, SortedDictionary<(double, double), double>> Perts)>();
        foreach (var r in raw)
        {
            var labels = GroupLabels(r.Labels, groupLabels, without);
            labels.Remove("le");
            var key = GroupKey(labels);
            if (!groups.TryGetValue(key, out var group))
            {
                group = (labels, []);
                groups[key] = group;
            }

            foreach (var p in r.Points)
            {
                if (p.BucketCounts is not { Count: > 0 } counts || p.BucketLowers is null || p.BucketUppers is null)
                {
                    continue;
                }

                var ts = p.BucketStart.ToUnixTimeMilliseconds() + shift;
                if (!group.Perts.TryGetValue(ts, out var merged))
                {
                    merged = [];
                    group.Perts[ts] = merged;
                }

                for (var i = 0; i < counts.Count; i++)
                {
                    var bound = (p.BucketLowers[i], p.BucketUppers[i]);
                    merged[bound] = merged.GetValueOrDefault(bound) + counts[i];
                }
            }
        }

        var result = new List<PromSeries>();
        foreach (var (labels, perTs) in groups.Values)
        {
            var series = new PromSeries(labels);
            foreach (var t in ctx.Timestamps())
            {
                var window = new SortedDictionary<(double, double), double>();
                for (var i = 0; i < buckets; i++)
                {
                    if (!perTs.TryGetValue(t - i * ctx.StepMs, out var at))
                    {
                        continue;
                    }

                    foreach (var (bound, count) in at)
                    {
                        window[bound] = window.GetValueOrDefault(bound) + count;
                    }
                }

                if (reduce(window) is { } value)
                {
                    series.Samples.Add(new PromSample(t, value));
                }
            }

            if (series.Samples.Count > 0)
            {
                result.Add(series);
            }
        }

        return result;
    }

    /// <summary>
    /// Prometheus's <c>histogram_quantile</c> rank walk with linear interpolation inside the bucket
    /// holding the target rank. Out-of-range quantiles follow Prometheus (<c>-Inf</c> below 0, <c>+Inf</c>
    /// above 1); a window with no observations has no sample.
    /// </summary>
    internal static double? Quantile(double q, SortedDictionary<(double Lower, double Upper), double> buckets)
    {
        var total = buckets.Values.Sum();
        if (total <= 0)
        {
            return null;
        }

        if (q < 0)
        {
            return double.NegativeInfinity;
        }

        if (q > 1)
        {
            return double.PositiveInfinity;
        }

        var rank = q * total;
        var cumulative = 0.0;
        foreach (var ((lower, upper), count) in buckets)
        {
            if (cumulative + count >= rank && count > 0)
            {
                return lower + (upper - lower) * ((rank - cumulative) / count);
            }

            cumulative += count;
        }

        return buckets.Keys.Last().Upper;
    }

    internal static List<PromSeries> Aggregate(PromAggregation agg, List<PromSeries> input)
    {
        if (agg.Op is "topk" or "bottomk")
        {
            return TopK(agg, input);
        }

        if (agg.Op == "count_values")
        {
            return CountValues(agg, input);
        }

        var groups = new Dictionary<string, (Dictionary<string, string> Labels, SortedDictionary<long, List<double>> Values)>();
        foreach (var s in input)
        {
            var labels = GroupLabels(s.Labels, agg.Labels, agg.Without);
            var key = GroupKey(labels);
            if (!groups.TryGetValue(key, out var group))
            {
                group = (labels, []);
                groups[key] = group;
            }

            foreach (var sample in s.Samples)
            {
                if (!group.Values.TryGetValue(sample.TimestampMs, out var values))
                {
                    values = [];
                    group.Values[sample.TimestampMs] = values;
                }

                values.Add(sample.Value);
            }
        }

        var result = new List<PromSeries>();
        foreach (var (labels, byTs) in groups.Values)
        {
            var series = new PromSeries(labels);
            foreach (var (ts, values) in byTs)
            {
                series.Samples.Add(new PromSample(ts, agg.Op switch
                {
                    "sum" => values.Sum(),
                    "avg" => values.Average(),
                    "min" => values.Min(),
                    "max" => values.Max(),
                    "group" => 1,
                    "stdvar" => Variance(values),
                    "stddev" => Math.Sqrt(Variance(values)),
                    "quantile" => QuantileOf(agg.Parameter ?? double.NaN, values),
                    _ => values.Count,
                }));
            }

            result.Add(series);
        }

        return result;
    }

    /// <summary>
    /// <c>count_values("label", v)</c>: at every timestamp, within each group, one series per distinct value
    /// holding how many input series have it, with the value written into <c>label</c>.
    /// </summary>
    private static List<PromSeries> CountValues(PromAggregation agg, List<PromSeries> input)
    {
        var label = agg.ValueLabel ?? throw new PromQlException("count_values needs a label name.");
        var groups = new Dictionary<string, (Dictionary<string, string> Labels, SortedDictionary<long, Dictionary<string, int>> Counts)>();
        foreach (var s in input)
        {
            var labels = GroupLabels(s.Labels, agg.Labels, agg.Without);
            var key = GroupKey(labels);
            if (!groups.TryGetValue(key, out var group))
            {
                group = (labels, []);
                groups[key] = group;
            }

            foreach (var sample in s.Samples)
            {
                if (!group.Counts.TryGetValue(sample.TimestampMs, out var counts))
                {
                    counts = [];
                    group.Counts[sample.TimestampMs] = counts;
                }

                var text = PrometheusEndpoints.FormatSample(sample.Value);
                counts[text] = counts.GetValueOrDefault(text) + 1;
            }
        }

        var result = new List<PromSeries>();
        foreach (var (labels, byTimestamp) in groups.Values)
        {
            var bySeries = new Dictionary<string, PromSeries>();
            foreach (var (ts, counts) in byTimestamp)
            {
                foreach (var (text, count) in counts)
                {
                    if (!bySeries.TryGetValue(text, out var series))
                    {
                        series = new PromSeries(new Dictionary<string, string>(labels) { [label] = text });
                        bySeries[text] = series;
                        result.Add(series);
                    }

                    series.Samples.Add(new PromSample(ts, count));
                }
            }
        }

        return result;
    }

    /// <summary>
    /// <c>topk</c>/<c>bottomk</c>: at every timestamp, within each group, the <c>k</c> series with the
    /// largest (smallest) value. Unlike other aggregations the series keep their own labels, so a
    /// series appears only at the timestamps where it ranks.
    /// </summary>
    private static List<PromSeries> TopK(PromAggregation agg, List<PromSeries> input)
    {
        var k = agg.Parameter ?? 0;
        if (double.IsNaN(k) || k < 1)
        {
            return [];
        }

        var limit = k >= int.MaxValue ? int.MaxValue : (int)k;
        var top = agg.Op == "topk";
        var selected = new Dictionary<PromSeries, List<PromSample>>(ReferenceEqualityComparer.Instance);
        foreach (var group in input.GroupBy(s => GroupKey(GroupLabels(s.Labels, agg.Labels, agg.Without))))
        {
            var byTimestamp = group
                .SelectMany(s => s.Samples.Where(x => !double.IsNaN(x.Value)).Select(x => (Series: s, Sample: x)))
                .GroupBy(x => x.Sample.TimestampMs);
            foreach (var at in byTimestamp)
            {
                var ranked = top
                    ? at.OrderByDescending(x => x.Sample.Value)
                    : at.OrderBy(x => x.Sample.Value);
                foreach (var (series, sample) in ranked.Take(limit))
                {
                    if (!selected.TryGetValue(series, out var samples))
                    {
                        samples = [];
                        selected[series] = samples;
                    }

                    samples.Add(sample);
                }
            }
        }

        var result = new List<PromSeries>();
        foreach (var (source, samples) in selected)
        {
            var series = new PromSeries(new Dictionary<string, string>(source.Labels));
            series.Samples.AddRange(samples.OrderBy(x => x.TimestampMs));
            result.Add(series);
        }

        return result;
    }

    private static double Variance(List<double> values)
    {
        var mean = values.Average();
        return values.Sum(v => (v - mean) * (v - mean)) / values.Count;
    }

    /// <summary>Prometheus's <c>quantile</c> aggregation: linear interpolation between the two nearest ranks; <c>-Inf</c> below 0, <c>+Inf</c> above 1.</summary>
    internal static double QuantileOf(double q, List<double> values)
    {
        if (double.IsNaN(q))
        {
            return double.NaN;
        }

        if (q < 0)
        {
            return double.NegativeInfinity;
        }

        if (q > 1)
        {
            return double.PositiveInfinity;
        }

        var sorted = values.Order().ToList();
        var rank = q * (sorted.Count - 1);
        var lower = (int)Math.Floor(rank);
        var upper = Math.Min(lower + 1, sorted.Count - 1);
        return sorted[lower] + (sorted[upper] - sorted[lower]) * (rank - lower);
    }

    private static int WindowBuckets(TimeSpan range, Context ctx) =>
        (int)Math.Max(1, Math.Round(range.TotalMilliseconds / ctx.StepMs));

    private static Dictionary<string, string> WithoutName(Dictionary<string, string> labels) =>
        labels.Where(kv => kv.Key != "__name__").ToDictionary(kv => kv.Key, kv => kv.Value);

    private static Dictionary<string, string> GroupLabels(Dictionary<string, string> labels, IReadOnlyList<string> named, bool without) =>
        without
            ? labels.Where(kv => kv.Key != "__name__" && !named.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value)
            : labels.Where(kv => named.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);

    private static string GroupKey(Dictionary<string, string> labels) => PromBinaryOperations.GroupKey(labels);

    private async Task<PromMetric?> ResolveAsync(PromSelector selector, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var name = selector.EffectiveMetricName
            ?? throw new PromQlException("this function needs a single metric name; a __name__ pattern works on plain selectors, range functions such as rate(), and timestamp().");
        return PromNames.Resolve(name, await KnownMetricsAsync(from, to, cancellationToken));
    }

    /// <summary>
    /// Equality matchers pushed down to the query so a series cap applies to the matching set: <c>service_name</c>
    /// becomes a service filter and any other label is mapped back to its original attribute key. Every matcher
    /// (pushed or not) is re-checked on the labels afterwards, which is what makes <c>!=</c>, <c>=~</c> and <c>!~</c> work.
    /// </summary>
    private async Task<(List<string> Services, List<MetricAttributeFilter> Attributes)> PushDownAsync(
        PromSelector selector, PromMetric metric, MetricPointType type, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var matchers = selector.LabelMatchers.ToList();
        var services = matchers
            .Where(m => m is { Name: "service_name", Op: PromMatchOp.Equal, Value.Length: > 0 })
            .Select(m => m.Value).ToList();

        var attributeFilters = new List<MetricAttributeFilter>();
        var pushable = matchers.Where(m => m is { Name: not "service_name", Op: PromMatchOp.Equal, Value.Length: > 0 }).ToList();
        if (pushable.Count > 0)
        {
            var keys = await metrics.GetAttributeKeysAsync(
                new MetricAttributeKeysRequest
                {
                    MetricName = metric.OtelName,
                    Type = type,
                    Filter = new MetricFilter { From = from, To = to },
                },
                cancellationToken);
            var originalKeys = keys.Keys
                .GroupBy(k => PromNames.LabelName(k.Key))
                .ToDictionary(g => g.Key, g => g.First().Key);
            foreach (var m in pushable)
            {
                if (originalKeys.TryGetValue(m.Name, out var original))
                {
                    attributeFilters.Add(new MetricAttributeFilter { Key = original, Value = m.Value });
                }
            }
        }

        return (services, attributeFilters);
    }

    /// <summary>
    /// Exemplars for one selector (ADR-0241): the trace-linked samples the SDK attached to the metric's points,
    /// labelled like the series they belong to. Empty for a metric Flare does not know or one that cannot carry
    /// exemplars (a histogram's <c>_sum</c> and <c>_count</c>), and when no exemplar store is configured.
    /// </summary>
    public async Task<IReadOnlyList<PromExemplar>> ExemplarsAsync(
        PromSelector selector, DateTimeOffset from, DateTimeOffset to, int limit, CancellationToken cancellationToken)
    {
        if (exemplars is null)
        {
            return [];
        }

        if (selector.IsMultiMetric)
        {
            var all = new List<PromExemplar>();
            foreach (var one in await ExpandAsync(selector, from, to, [], cancellationToken))
            {
                all.AddRange(await ExemplarsAsync(one, from, to, limit, cancellationToken));
            }

            return all;
        }

        var metric = await ResolveAsync(selector, from, to, cancellationToken);
        if (metric is null or { Kind: PromMetricKind.HistogramSum or PromMetricKind.HistogramCount })
        {
            return [];
        }

        var (services, attributeFilters) = await PushDownAsync(selector, metric, metric.Type, from, to, cancellationToken);
        var rows = await exemplars.GetAsync(
            new MetricExemplarsRequest
            {
                MetricName = metric.OtelName,
                Type = metric.Type,
                Filter = new MetricFilter
                {
                    From = from,
                    To = to,
                    Services = services.Count > 0 ? services : null,
                    Attributes = attributeFilters.Count > 0 ? attributeFilters : null,
                },
                Limit = limit,
            },
            cancellationToken);

        var matchers = selector.LabelMatchers.ToList();
        var result = new List<PromExemplar>(rows.Count);
        foreach (var row in rows)
        {
            var labels = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["__name__"] = metric.PromName,
                ["service_name"] = row.ServiceName,
            };
            foreach (var (key, value) in row.DataPointAttributes)
            {
                labels[PromNames.LabelName(key)] = value;
            }

            if (!matchers.All(m => Matches(m, labels.GetValueOrDefault(m.Name, ""))))
            {
                continue;
            }

            var exemplarLabels = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (key, value) in row.FilteredAttributes)
            {
                exemplarLabels[PromNames.LabelName(key)] = value;
            }

            exemplarLabels["trace_id"] = row.TraceId;
            if (!string.IsNullOrEmpty(row.SpanId))
            {
                exemplarLabels["span_id"] = row.SpanId;
            }

            result.Add(new PromExemplar(labels, exemplarLabels, row.Value, row.Time.ToUnixTimeMilliseconds()));
        }

        return result;
    }

    private async Task<List<PromRawSeries>> FetchAsync(
        PromSelector selector,
        PromMetric metric,
        DateTimeOffset from,
        DateTimeOffset to,
        int bucketSeconds,
        MetricPointType type,
        bool treatAsCounter,
        bool includeBuckets,
        List<string> warnings,
        CancellationToken cancellationToken,
        MetricGaugeAggregate? gaugeAggregate = null)
    {
        var matchers = selector.LabelMatchers.ToList();
        var (services, attributeFilters) = await PushDownAsync(selector, metric, type, from, to, cancellationToken);

        var response = await metrics.QueryAsync(
            new MetricQueryRequest
            {
                MetricName = metric.OtelName,
                Type = treatAsCounter ? MetricPointType.Gauge : type,
                Filter = new MetricFilter
                {
                    From = from,
                    To = to,
                    Services = services.Count > 0 ? services : null,
                    Attributes = attributeFilters.Count > 0 ? attributeFilters : null,
                },
                BucketWidthSeconds = bucketSeconds,
                TopN = MaxSeries,
                TreatAsCounter = treatAsCounter ? true : null,
                IncludeBuckets = includeBuckets ? true : null,
                GaugeAggregate = gaugeAggregate,
            },
            cancellationToken);

        if (response.Series.Count >= MaxSeries)
        {
            warnings.Add($"'{metric.PromName}' matched at least {MaxSeries} series; only the {MaxSeries} largest are included. Narrow the selector with label matchers.");
        }

        var result = new List<PromRawSeries>(response.Series.Count);
        foreach (var s in response.Series)
        {
            var labels = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["__name__"] = metric.PromName,
                ["service_name"] = s.ServiceName,
            };
            foreach (var (key, value) in s.Attributes)
            {
                labels[PromNames.LabelName(key)] = value;
            }

            if (matchers.All(m => Matches(m, labels.GetValueOrDefault(m.Name, ""))))
            {
                result.Add(new PromRawSeries(labels, s.Points));
            }
        }

        return result;
    }

    internal static bool Matches(PromMatcher matcher, string value) => matcher.Op switch
    {
        PromMatchOp.Equal => value == matcher.Value,
        PromMatchOp.NotEqual => value != matcher.Value,
        PromMatchOp.Regex => AnchoredRegex(matcher.Value).IsMatch(value),
        _ => !AnchoredRegex(matcher.Value).IsMatch(value),
    };

    private static Regex AnchoredRegex(string pattern)
    {
        try
        {
            return new Regex($"^(?:{pattern})$", RegexOptions.CultureInvariant, RegexTimeout);
        }
        catch (ArgumentException ex)
        {
            throw new PromQlException($"invalid regular expression '{pattern}': {ex.Message}");
        }
    }
}
