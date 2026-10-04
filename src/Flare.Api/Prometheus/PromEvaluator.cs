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
internal sealed class PromEvaluator(IMetricQueryService metrics)
{
    /// <summary>The metric query service's own series cap (<c>MetricSeriesQueryBuilder.MaxTopN</c>).</summary>
    public const int MaxSeries = 200;

    /// <summary>Prometheus's own per-query resolution limit.</summary>
    public const int MaxPoints = 11_000;

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);

    private sealed record Context(long StartMs, long EndMs, long StepMs, List<string> Warnings)
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

    public async Task<PromEvalResult> EvaluateAsync(
        PromExpr expr, DateTimeOffset start, DateTimeOffset end, TimeSpan step, CancellationToken cancellationToken)
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

        var ctx = new Context(start.ToUnixTimeMilliseconds(), end.ToUnixTimeMilliseconds(), stepSeconds * 1000, []);
        var series = await EvalAsync(expr, ctx, cancellationToken);
        return new PromEvalResult(series, expr is PromNumber, ctx.Warnings);
    }

    /// <summary>Series matching a selector in a window - backs <c>/api/v1/series</c>, <c>/labels</c> and <c>/label/&lt;name&gt;/values</c>.</summary>
    public async Task<IReadOnlyList<PromRawSeries>> SelectSeriesAsync(
        PromSelector selector, DateTimeOffset from, DateTimeOffset to, List<string> warnings, CancellationToken cancellationToken)
    {
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
            {
                var series = new PromSeries([]);
                series.Samples.AddRange(ctx.Timestamps().Select(t => new PromSample(t, number.Value)));
                return [series];
            }

            case PromSelector selector:
                return await EvalSelectorAsync(selector, ctx, cancellationToken);

            case PromRangeFunction fn:
                return await EvalRangeFunctionAsync(fn, ctx, cancellationToken);

            case PromAggregation agg:
                return Aggregate(agg, await EvalAsync(agg.Inner, ctx, cancellationToken));

            case PromHistogramQuantile hq:
                return await EvalHistogramQuantileAsync(hq, ctx, cancellationToken);

            default:
                throw new PromQlException("unsupported expression.");
        }
    }

    private async Task<List<PromSeries>> EvalSelectorAsync(PromSelector selector, Context ctx, CancellationToken cancellationToken)
    {
        var from = DateTimeOffset.FromUnixTimeMilliseconds(ctx.StartMs);
        var to = DateTimeOffset.FromUnixTimeMilliseconds(ctx.EndMs).AddMilliseconds(1);
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

        var raw = await FetchAsync(selector, metric, from, to, ctx.StepSeconds, metric.Type, false, false, ctx.Warnings, cancellationToken);
        return raw.Select(r =>
        {
            var series = new PromSeries(r.Labels);
            series.Samples.AddRange(r.Points
                .Where(p => p.Value is not null)
                .Select(p => new PromSample(p.BucketStart.ToUnixTimeMilliseconds(), p.Value!.Value)));
            return series;
        }).Where(s => s.Samples.Count > 0).ToList();
    }

    private async Task<List<PromSeries>> EvalRangeFunctionAsync(PromRangeFunction fn, Context ctx, CancellationToken cancellationToken)
    {
        var range = fn.Selector.Range!.Value;
        var from = DateTimeOffset.FromUnixTimeMilliseconds(ctx.StartMs) - range;
        var to = DateTimeOffset.FromUnixTimeMilliseconds(ctx.EndMs).AddMilliseconds(1);
        var metric = await ResolveAsync(fn.Selector, from, to, cancellationToken);
        if (metric is null)
        {
            return [];
        }

        if (metric.Kind == PromMetricKind.HistogramBucket)
        {
            throw new PromQlException($"{fn.Name}({metric.PromName}[...]) is only supported inside histogram_quantile; use {metric.PromName[..^"_bucket".Length]}_count or _sum for a plain rate.");
        }

        // A gauge under rate()/increase() is an untyped Prometheus counter (`*_total` scraped without
        // type metadata arrives as a Gauge) - the same reading ADR-0066's "treat as counter" gives it.
        var treatAsCounter = metric.Kind == PromMetricKind.Gauge;
        var type = metric.Kind == PromMetricKind.Counter ? MetricPointType.Sum : metric.Type;
        var raw = await FetchAsync(fn.Selector, metric, from, to, ctx.StepSeconds, type, treatAsCounter, false, ctx.Warnings, cancellationToken);

        var buckets = WindowBuckets(range, ctx);
        var windowSeconds = buckets * ctx.StepSeconds;
        var result = new List<PromSeries>();
        foreach (var r in raw)
        {
            var increases = new Dictionary<long, double>();
            foreach (var p in r.Points)
            {
                var inc = metric.Kind switch
                {
                    PromMetricKind.HistogramCount => (double?)p.Count,
                    PromMetricKind.HistogramSum => p.Sum,
                    _ => p.Value,
                };
                if (inc is { } v)
                {
                    increases[p.BucketStart.ToUnixTimeMilliseconds()] = v;
                }
            }

            var series = new PromSeries(WithoutName(r.Labels));
            foreach (var t in ctx.Timestamps())
            {
                var sum = 0.0;
                var any = false;
                for (var i = 0; i < buckets; i++)
                {
                    if (increases.TryGetValue(t - i * ctx.StepMs, out var v))
                    {
                        sum += v;
                        any = true;
                    }
                }

                if (any)
                {
                    var perSecond = sum / windowSeconds;
                    series.Samples.Add(new PromSample(t, fn.Name == "rate" ? perSecond : perSecond * range.TotalSeconds));
                }
            }

            if (series.Samples.Count > 0)
            {
                result.Add(series);
            }
        }

        return result;
    }

    private async Task<List<PromSeries>> EvalHistogramQuantileAsync(PromHistogramQuantile hq, Context ctx, CancellationToken cancellationToken)
    {
        // Accepted shapes: histogram_quantile(q, rate(m[r])) and
        // histogram_quantile(q, sum [by|without (...)] (rate(m[r]))). Anything richer is rejected, not approximated.
        PromRangeFunction fn;
        IReadOnlyList<string> groupLabels = [];
        var without = true;
        switch (hq.Inner)
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
                    "histogram_quantile's argument must be rate()/increase() of a histogram, optionally wrapped in sum [by|without (...)].");
        }

        var range = fn.Selector.Range!.Value;
        var from = DateTimeOffset.FromUnixTimeMilliseconds(ctx.StartMs) - range;
        var to = DateTimeOffset.FromUnixTimeMilliseconds(ctx.EndMs).AddMilliseconds(1);
        var metric = await ResolveAsync(fn.Selector, from, to, cancellationToken);
        if (metric is null)
        {
            return [];
        }

        if (metric.Kind != PromMetricKind.HistogramBucket)
        {
            throw new PromQlException($"histogram_quantile needs a histogram metric; '{metric.PromName}' is not one.");
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

                var ts = p.BucketStart.ToUnixTimeMilliseconds();
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

                if (Quantile(hq.Quantile, window) is { } value)
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
                    _ => values.Count,
                }));
            }

            result.Add(series);
        }

        return result;
    }

    private static int WindowBuckets(TimeSpan range, Context ctx) =>
        (int)Math.Max(1, Math.Round(range.TotalMilliseconds / ctx.StepMs));

    private static Dictionary<string, string> WithoutName(Dictionary<string, string> labels) =>
        labels.Where(kv => kv.Key != "__name__").ToDictionary(kv => kv.Key, kv => kv.Value);

    private static Dictionary<string, string> GroupLabels(Dictionary<string, string> labels, IReadOnlyList<string> named, bool without) =>
        without
            ? labels.Where(kv => kv.Key != "__name__" && !named.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value)
            : labels.Where(kv => named.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);

    private static string GroupKey(Dictionary<string, string> labels) =>
        string.Join('\0', labels.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key}={kv.Value}"));

    private async Task<PromMetric?> ResolveAsync(PromSelector selector, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var name = selector.EffectiveMetricName ?? throw new PromQlException("vector selector must name a metric.");
        return PromNames.Resolve(name, await KnownMetricsAsync(from, to, cancellationToken));
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
        CancellationToken cancellationToken)
    {
        // Equality matchers are pushed down so the series cap applies to the matching set. Every
        // matcher (pushed or not) is re-checked below, which is what makes !=, =~ and !~ work.
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
