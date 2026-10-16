using System.Globalization;
using System.Text.Json;
using Flare.Api.Query;

namespace Flare.Api.Tempo;

/// <summary>A series label: <see cref="Text"/> for a string value, <see cref="Number"/> for the <c>p</c> of a quantile.</summary>
public sealed record TempoLabel(string Key, string? Text, double? Number = null)
{
    /// <summary>A label whose value is <c>nil</c>, as <c>compare()</c> labels its totals and its too-many-values marker (ADR-0226): written as an empty value.</summary>
    public static TempoLabel Nil(string key) => new(key, null);
}

public sealed record TempoSample(long TimestampMs, double Value);

/// <summary>A span worth a look at one point of a series, linked by its <c>trace:id</c> label (ADR-0223).</summary>
public sealed record TempoExemplar(IReadOnlyList<TempoLabel> Labels, double Value, long TimestampMs);

public sealed record TempoMetricSeries(IReadOnlyList<TempoLabel> Labels, IReadOnlyList<TempoSample> Samples, IReadOnlyList<TempoExemplar>? Exemplars = null);

/// <param name="PartialMessage">Set when series were left out; the response then says so.</param>
public sealed record TempoMetricsResult(IReadOnlyList<TempoMetricSeries> Series, string? PartialMessage);

/// <summary>
/// Turns <see cref="SpanMetricRow"/>s into Tempo's series (ADR-0221) and writes them as the JSON of
/// <c>QueryRangeResponse</c> / <c>QueryInstantResponse</c>. Pure.
/// </summary>
public static class TempoMetricsSeries
{
    /// <summary>
    /// One series per group (and, for a quantile, per quantile; for a histogram, per bucket), labelled by the <c>by</c>
    /// fields, with <c>p</c> for a quantile, <c>__bucket</c> (the bucket's upper bound) for a histogram, and the function's
    /// name when there is nothing else to label it. <c>rate()</c>, <c>count_over_time()</c> and <c>histogram_over_time()</c>
    /// are zero in a step without spans, so every series covers the whole range; the other functions omit such steps. Only
    /// the <see cref="TraceQlMetricsQueryBuilder.MaxGroups"/> groups with the most spans are kept. Durations are in seconds, an
    /// attribute's values as they are; samples are stamped with their step's start. An <paramref name="exemplars"/> row is
    /// attached to its group's series nearest its value (ADR-0223), and <see cref="TempoMetricsPlan.Stages"/> then run in order: a
    /// <c>topk</c>/<c>bottomk</c> keeps the top or bottom series at each step (ADR-0224), a comparison drops the samples that fail it (ADR-0225).
    /// </summary>
    public static TempoMetricsResult Build(TempoMetricsPlan plan, IReadOnlyList<SpanMetricRow> rows, TempoMetricsWindow window, IReadOnlyList<SpanExemplarRow>? exemplars = null)
    {
        var groups = rows
            .GroupBy(r => string.Join('\u001f', r.Group), StringComparer.Ordinal)
            .Select(g => (Rows: g.ToArray(), Weight: g.Sum(r => r.Weight), Key: g.Key))
            .OrderByDescending(g => g.Weight)
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .ToList();

        string? partial = null;
        var cap = TraceQlMetricsQueryBuilder.MaxGroups(plan.Function);
        if (groups.Count > cap)
        {
            groups = groups.Take(cap).ToList();
            partial = $"more than {cap} {(plan.Function == SpanMetricFunction.Histogram ? "groups" : "series")} matched; the {cap} with the most spans are included. Group by fewer fields or narrow the spanset.";
        }

        var exemplarsByGroup = (exemplars ?? []).ToLookup(e => string.Join('\u001f', e.Group), StringComparer.Ordinal);
        var unit = plan.Value is null ? 1e9 : 1;
        var series = new List<TempoMetricSeries>();
        foreach (var (groupRows, _, key) in groups)
        {
            var byLabels = plan.GroupBy.Select((by, i) => new TempoLabel(by.Label, LabelValue(by.Kind, groupRows[0].Group[i]))).ToList();
            var first = series.Count;
            var exponents = new List<double>();
            if (plan.Function == SpanMetricFunction.Histogram)
            {
                foreach (var bucket in groupRows.GroupBy(r => r.Values[0]).OrderBy(g => g.Key))
                {
                    exponents.Add(bucket.Key);
                    var labels = new List<TempoLabel>(byLabels) { new("__bucket", null, Math.Pow(2, bucket.Key) / unit) };
                    series.Add(new TempoMetricSeries(labels, Samples(plan, bucket.ToArray(), 0, window)));
                }
            }
            else
            {
                var valueCount = plan.Function == SpanMetricFunction.Quantile ? plan.Quantiles.Count : 1;
                for (var v = 0; v < valueCount; v++)
                {
                    var labels = new List<TempoLabel>(byLabels);
                    if (plan.Function == SpanMetricFunction.Quantile)
                    {
                        labels.Add(new TempoLabel("p", null, plan.Quantiles[v]));
                    }
                    else if (labels.Count == 0)
                    {
                        labels.Add(new TempoLabel("__name__", plan.FunctionName));
                    }

                    series.Add(new TempoMetricSeries(labels, Samples(plan, groupRows, v, window)));
                }
            }

            AttachExemplars(plan, series, first, exponents, exemplarsByGroup[key], unit);
        }

        foreach (var stage in plan.Stages)
        {
            series = stage switch
            {
                TraceQlRank rank => Rank(series, rank, window),
                TraceQlFilter filter => Filter(series, filter, window),
                _ => series,
            };
        }

        return new TempoMetricsResult(series, partial);
    }

    /// <summary>
    /// Puts each exemplar on one series of its group: for a histogram the bucket its value falls in (the next larger one if
    /// that has no spans in the window), for a quantile the largest quantile, whose slow spans are the interesting ones.
    /// </summary>
    private static void AttachExemplars(TempoMetricsPlan plan, List<TempoMetricSeries> series, int first, List<double> exponents, IEnumerable<SpanExemplarRow> exemplars, double unit)
    {
        var buckets = new Dictionary<int, List<TempoExemplar>>();
        foreach (var e in exemplars)
        {
            int target;
            if (plan.Function == SpanMetricFunction.Histogram)
            {
                var exponent = Math.Ceiling(Math.Log2(Math.Max(1, Math.Truncate(e.Value))));
                var at = exponents.FindIndex(x => x >= exponent);
                target = first + (at < 0 ? exponents.Count - 1 : at);
            }
            else if (plan.Function == SpanMetricFunction.Quantile)
            {
                target = first + plan.Quantiles.ToList().IndexOf(plan.Quantiles.Max());
            }
            else
            {
                target = first;
            }

            if (!buckets.TryGetValue(target, out var list))
            {
                buckets[target] = list = [];
            }

            list.Add(new TempoExemplar([new TempoLabel("trace:id", e.TraceId), new TempoLabel("span:id", e.SpanId)], e.Value / unit, e.TimestampMs));
        }

        foreach (var (index, list) in buckets)
        {
            series[index] = series[index] with { Exemplars = list.OrderBy(x => x.TimestampMs).ToList() };
        }
    }

    /// <summary>
    /// <c>topk(k)</c> / <c>bottomk(k)</c>, evaluated at every step as in Prometheus: of the series with a sample at that
    /// step, only the k largest (smallest) keep it, ties going to the earlier series. A series left without a sample is
    /// dropped, so more than k series can be returned overall, as in Tempo. An exemplar stays only with a step its series kept.
    /// </summary>
    private static List<TempoMetricSeries> Rank(List<TempoMetricSeries> series, TraceQlRank rank, TempoMetricsWindow window)
    {
        var kept = series.Select(_ => new HashSet<long>()).ToArray();
        var perStep = new Dictionary<long, List<(int Series, double Value)>>();
        for (var i = 0; i < series.Count; i++)
        {
            foreach (var sample in series[i].Samples)
            {
                if (!perStep.TryGetValue(sample.TimestampMs, out var list))
                {
                    perStep[sample.TimestampMs] = list = [];
                }

                list.Add((i, sample.Value));
            }
        }

        foreach (var (timestamp, list) in perStep)
        {
            var ordered = rank.Top ? list.OrderByDescending(x => x.Value) : list.OrderBy(x => x.Value);
            foreach (var (index, _) in ordered.ThenBy(x => x.Series).Take(rank.K))
            {
                kept[index].Add(timestamp);
            }
        }

        var result = new List<TempoMetricSeries>();
        for (var i = 0; i < series.Count; i++)
        {
            var samples = series[i].Samples.Where(x => kept[i].Contains(x.TimestampMs)).ToList();
            if (samples.Count > 0)
            {
                result.Add(series[i] with { Samples = samples, Exemplars = series[i].Exemplars?.Where(x => kept[i].Contains(StepStart(x.TimestampMs, window))).ToList() });
            }
        }

        return result;
    }

    /// <summary>
    /// A comparison after the function (<c>| rate() &gt; 5</c>): a sample that fails it is dropped, as is an exemplar of a step
    /// that lost its sample; a series left with no sample is dropped. A step missing from a series stays missing, so a line
    /// chart shows a gap, as it does for Tempo.
    /// </summary>
    private static List<TempoMetricSeries> Filter(List<TempoMetricSeries> series, TraceQlFilter filter, TempoMetricsWindow window)
    {
        var result = new List<TempoMetricSeries>();
        foreach (var s in series)
        {
            var samples = s.Samples.Where(x => filter.Keeps(x.Value)).ToList();
            if (samples.Count == 0)
            {
                continue;
            }

            var steps = samples.Select(x => x.TimestampMs).ToHashSet();
            result.Add(s with { Samples = samples, Exemplars = s.Exemplars?.Where(x => steps.Contains(StepStart(x.TimestampMs, window))).ToList() });
        }

        return result;
    }

    private static List<TempoSample> Samples(TempoMetricsPlan plan, SpanMetricRow[] rows, int valueIndex, TempoMetricsWindow window)
    {
        var byBucket = rows.ToDictionary(r => r.Bucket);
        var samples = new List<TempoSample>();
        var counting = plan.Function is SpanMetricFunction.Count or SpanMetricFunction.Histogram;
        for (long bucket = 0; bucket < window.Steps; bucket++)
        {
            var timestamp = (window.FromSeconds + bucket * window.StepSeconds) * 1000;
            if (!byBucket.TryGetValue(bucket, out var row))
            {
                if (counting)
                {
                    samples.Add(new TempoSample(timestamp, 0));
                }

                continue;
            }

            var value = counting ? (plan.IsRate ? row.Weight / window.StepSeconds : row.Weight) : row.Values[valueIndex] / (plan.Value is null ? 1e9 : 1);
            if (double.IsFinite(value))
            {
                samples.Add(new TempoSample(timestamp, value));
            }
        }

        return samples;
    }

    /// <summary>The timestamp of the step a point in time falls in, as a sample is stamped.</summary>
    private static long StepStart(long timestampMs, TempoMetricsWindow window)
    {
        var stepMs = window.StepSeconds * 1000L;
        var from = window.FromSeconds * 1000;
        return from + Math.Max(0, (timestampMs - from) / stepMs) * stepMs;
    }

    /// <summary>TraceQL's words for the status and kind a span stores as <c>STATUS_CODE_ERROR</c> and <c>2</c>.</summary>
    private static string LabelValue(SpanGroupKind kind, string stored) => kind switch
    {
        SpanGroupKind.Status => stored switch { "STATUS_CODE_ERROR" => "error", "STATUS_CODE_OK" => "ok", "STATUS_CODE_UNSET" => "unset", _ => stored },
        SpanGroupKind.Kind => stored switch { "1" => "internal", "2" => "server", "3" => "client", "4" => "producer", "5" => "consumer", "0" => "unspecified", _ => stored },
        _ => stored,
    };

    /// <summary><c>{"series":[{"labels":[...],"samples":[{"timestampMs":"...","value":...}]}],"metrics":{},"status":"PARTIAL","message":"..."}</c>.</summary>
    public static void WriteRange(Utf8JsonWriter w, TempoMetricsResult result)
    {
        w.WriteStartObject();
        w.WriteStartArray("series");
        foreach (var series in result.Series)
        {
            w.WriteStartObject();
            WriteLabels(w, series.Labels);
            w.WriteStartArray("samples");
            foreach (var sample in series.Samples)
            {
                w.WriteStartObject();
                w.WriteString("timestampMs", sample.TimestampMs.ToString(CultureInfo.InvariantCulture));
                w.WriteNumber("value", sample.Value);
                w.WriteEndObject();
            }

            w.WriteEndArray();
            if (series.Exemplars is { Count: > 0 } exemplars)
            {
                w.WriteStartArray("exemplars");
                foreach (var exemplar in exemplars)
                {
                    w.WriteStartObject();
                    WriteLabels(w, exemplar.Labels);
                    w.WriteNumber("value", exemplar.Value);
                    w.WriteString("timestampMs", exemplar.TimestampMs.ToString(CultureInfo.InvariantCulture));
                    w.WriteEndObject();
                }

                w.WriteEndArray();
            }

            w.WriteEndObject();
        }

        w.WriteEndArray();
        WriteTail(w, result);
    }

    /// <summary><c>{"series":[{"labels":[...],"value":...}],"metrics":{}}</c>: each series' one value over the whole range; a series with none is left out.</summary>
    public static void WriteInstant(Utf8JsonWriter w, TempoMetricsResult result)
    {
        w.WriteStartObject();
        w.WriteStartArray("series");
        foreach (var series in result.Series.Where(s => s.Samples.Count > 0))
        {
            w.WriteStartObject();
            WriteLabels(w, series.Labels);
            w.WriteNumber("value", series.Samples[0].Value);
            w.WriteEndObject();
        }

        w.WriteEndArray();
        WriteTail(w, result);
    }

    private static void WriteLabels(Utf8JsonWriter w, IReadOnlyList<TempoLabel> labels)
    {
        w.WriteStartArray("labels");
        foreach (var label in labels)
        {
            w.WriteStartObject();
            w.WriteString("key", label.Key);
            w.WriteStartObject("value");
            if (label.Number is { } number)
            {
                w.WriteNumber("doubleValue", number);
            }
            else if (label.Text is not null)
            {
                w.WriteString("stringValue", label.Text);
            }

            w.WriteEndObject();
            w.WriteEndObject();
        }

        w.WriteEndArray();
    }

    private static void WriteTail(Utf8JsonWriter w, TempoMetricsResult result)
    {
        w.WriteStartObject("metrics");
        w.WriteEndObject();
        if (result.PartialMessage is { } message)
        {
            w.WriteString("status", "PARTIAL");
            w.WriteString("message", message);
        }

        w.WriteEndObject();
    }
}
