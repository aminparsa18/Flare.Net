using System.Globalization;
using System.Text.Json;
using Flare.Api.Query;

namespace Flare.Api.Tempo;

/// <summary>A series label: <see cref="Text"/> for a string value, <see cref="Number"/> for the <c>p</c> of a quantile.</summary>
public sealed record TempoLabel(string Key, string? Text, double? Number = null);

public sealed record TempoSample(long TimestampMs, double Value);

public sealed record TempoMetricSeries(IReadOnlyList<TempoLabel> Labels, IReadOnlyList<TempoSample> Samples);

/// <param name="PartialMessage">Set when series were left out; the response then says so.</param>
public sealed record TempoMetricsResult(IReadOnlyList<TempoMetricSeries> Series, string? PartialMessage);

/// <summary>
/// Turns <see cref="SpanMetricRow"/>s into Tempo's series (ADR-0221) and writes them as the JSON of
/// <c>QueryRangeResponse</c> / <c>QueryInstantResponse</c>. Pure.
/// </summary>
public static class TempoMetricsSeries
{
    /// <summary>
    /// One series per group (and, for a quantile, per quantile), labelled by the <c>by</c> fields, with <c>p</c> for a
    /// quantile and the function's name when there is nothing else to label it. <c>rate()</c> and
    /// <c>count_over_time()</c> are zero in a step without spans, so every series covers the whole range; the other
    /// functions omit such steps. Only the <see cref="TraceQlMetricsQueryBuilder.MaxSeries"/> groups with the most
    /// spans are kept. Durations are in seconds, samples are stamped with their step's start.
    /// </summary>
    public static TempoMetricsResult Build(TempoMetricsPlan plan, IReadOnlyList<SpanMetricRow> rows, TempoMetricsWindow window)
    {
        var groups = rows
            .GroupBy(r => string.Join('\u001f', r.Group), StringComparer.Ordinal)
            .Select(g => (Rows: g.ToArray(), Weight: g.Sum(r => r.Weight), Key: g.Key))
            .OrderByDescending(g => g.Weight)
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .ToList();

        string? partial = null;
        if (groups.Count > TraceQlMetricsQueryBuilder.MaxSeries)
        {
            groups = groups.Take(TraceQlMetricsQueryBuilder.MaxSeries).ToList();
            partial = $"more than {TraceQlMetricsQueryBuilder.MaxSeries} series matched; the {TraceQlMetricsQueryBuilder.MaxSeries} with the most spans are included. Group by fewer fields or narrow the spanset.";
        }

        var series = new List<TempoMetricSeries>();
        foreach (var (groupRows, _, _) in groups)
        {
            var byLabels = plan.GroupBy.Select((by, i) => new TempoLabel(by.Label, LabelValue(by.Kind, groupRows[0].Group[i]))).ToList();
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

        return new TempoMetricsResult(series, partial);
    }

    private static List<TempoSample> Samples(TempoMetricsPlan plan, SpanMetricRow[] rows, int valueIndex, TempoMetricsWindow window)
    {
        var byBucket = rows.ToDictionary(r => r.Bucket);
        var samples = new List<TempoSample>();
        var counting = plan.Function == SpanMetricFunction.Count;
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

            var value = counting ? (plan.IsRate ? row.Weight / window.StepSeconds : row.Weight) : row.Values[valueIndex] / 1e9;
            if (double.IsFinite(value))
            {
                samples.Add(new TempoSample(timestamp, value));
            }
        }

        return samples;
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
            else
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
