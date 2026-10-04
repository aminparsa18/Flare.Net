using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Primitives;

namespace Flare.Api.Prometheus;

/// <summary>
/// A read-only subset of the Prometheus HTTP API (ADR-0109) under <c>/api/v1</c>, so Grafana,
/// <c>promtool</c> and <c>prometheus-adapter</c> can use Flare as a data source: <c>query</c>,
/// <c>query_range</c>, <c>series</c>, <c>labels</c>, <c>label/&lt;name&gt;/values</c> and
/// <c>status/buildinfo</c>. GET and form-encoded POST both work, as Prometheus clients expect.
/// Authentication is the normal Flare pipeline - a personal access token as <c>Authorization: Bearer
/// flr_pat_...</c> (Grafana: "Custom HTTP headers" or bearer auth).
/// </summary>
/// <remarks>
/// Responses are written with <see cref="Utf8JsonWriter"/> directly: the envelope is Prometheus's
/// fixed wire format, not one of Flare's MemoryPack-negotiated DTOs, so it has no source-gen context.
/// Errors use Prometheus's <c>{"status":"error","errorType":...,"error":...}</c> shape; unsupported or
/// malformed PromQL is a 400 <c>bad_data</c>, never a partial result.
/// </remarks>
public static class PrometheusEndpoints
{
    private static readonly TimeSpan InstantLookback = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan InstantStep = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan DefaultMetadataWindow = TimeSpan.FromHours(1);

    /// <summary>How many metrics a <c>labels</c>/<c>label values</c> call with no <c>match[]</c> samples series from.</summary>
    private const int UnscopedMetricLimit = 10;

    public static IEndpointRouteBuilder MapPrometheusEndpoints(this IEndpointRouteBuilder endpoints)
    {
        foreach (var route in new[] { "/api/v1/query", "/api/v1/query_range", "/api/v1/series", "/api/v1/labels" })
        {
            var handler = route switch
            {
                "/api/v1/query" => (Delegate)HandleQueryAsync,
                "/api/v1/query_range" => HandleQueryRangeAsync,
                "/api/v1/series" => HandleSeriesAsync,
                _ => HandleLabelsAsync,
            };
            endpoints.MapMethods(route, ["GET", "POST"], handler);
        }

        endpoints.MapGet("/api/v1/label/{name}/values", HandleLabelValuesAsync);
        endpoints.MapGet("/api/v1/status/buildinfo", HandleBuildInfo);
        return endpoints;
    }

    private static async Task<IResult> HandleQueryAsync(HttpContext http, PromEvaluator evaluator, TimeProvider time, CancellationToken ct) =>
        await GuardAsync(async () =>
        {
            var p = await ReadParametersAsync(http, ct);
            var expr = PromQlParser.Parse(p.Single("query") ?? throw new PromQlException("parameter 'query' is required."));
            var at = p.Single("time") is { } t ? ParseTime(t, "time") : time.GetUtcNow();

            var result = await evaluator.EvaluateAsync(expr, at - InstantLookback, at, InstantStep, ct);
            return Success(w =>
            {
                if (result.IsScalar)
                {
                    w.WriteString("resultType", "scalar");
                    w.WritePropertyName("result");
                    WriteValue(w, at.ToUnixTimeMilliseconds(), result.Series[0].Samples[^1].Value);
                    return;
                }

                w.WriteString("resultType", "vector");
                w.WriteStartArray("result");
                foreach (var series in result.Series.Where(s => s.Samples.Count > 0))
                {
                    w.WriteStartObject();
                    WriteLabels(w, "metric", series.Labels);
                    w.WritePropertyName("value");
                    // An instant vector is stamped with the evaluation time, like Prometheus.
                    WriteValue(w, at.ToUnixTimeMilliseconds(), series.Samples[^1].Value);
                    w.WriteEndObject();
                }

                w.WriteEndArray();
            }, result.Warnings);
        });

    private static async Task<IResult> HandleQueryRangeAsync(HttpContext http, PromEvaluator evaluator, CancellationToken ct) =>
        await GuardAsync(async () =>
        {
            var p = await ReadParametersAsync(http, ct);
            var expr = PromQlParser.Parse(p.Single("query") ?? throw new PromQlException("parameter 'query' is required."));
            var start = ParseTime(p.Single("start") ?? throw new PromQlException("parameter 'start' is required."), "start");
            var end = ParseTime(p.Single("end") ?? throw new PromQlException("parameter 'end' is required."), "end");
            var step = ParseStep(p.Single("step") ?? throw new PromQlException("parameter 'step' is required."));

            var result = await evaluator.EvaluateAsync(expr, start, end, step, ct);
            var startMs = start.ToUnixTimeMilliseconds();
            return Success(w =>
            {
                w.WriteString("resultType", "matrix");
                w.WriteStartArray("result");
                foreach (var series in result.Series.Where(s => s.Samples.Count > 0))
                {
                    w.WriteStartObject();
                    WriteLabels(w, "metric", series.Labels);
                    w.WriteStartArray("values");
                    foreach (var sample in series.Samples.Where(s => s.TimestampMs >= startMs))
                    {
                        WriteValue(w, sample.TimestampMs, sample.Value);
                    }

                    w.WriteEndArray();
                    w.WriteEndObject();
                }

                w.WriteEndArray();
            }, result.Warnings);
        });

    private static async Task<IResult> HandleSeriesAsync(HttpContext http, PromEvaluator evaluator, TimeProvider time, CancellationToken ct) =>
        await GuardAsync(async () =>
        {
            var p = await ReadParametersAsync(http, ct);
            if (p.All("match[]").Count == 0)
            {
                throw new PromQlException("at least one match[] selector is required.");
            }

            var (from, to) = MetadataWindow(p, time);
            var warnings = new List<string>();
            var series = await SelectAsync(evaluator, p.All("match[]"), from, to, warnings, ct);
            return Success(w =>
            {
                w.WriteStartArray("data");
                foreach (var s in series)
                {
                    WriteLabelsObject(w, s.Labels);
                }

                w.WriteEndArray();
            }, warnings, dataIsProperty: true);
        });

    private static async Task<IResult> HandleLabelsAsync(HttpContext http, PromEvaluator evaluator, TimeProvider time, CancellationToken ct) =>
        await GuardAsync(async () =>
        {
            var p = await ReadParametersAsync(http, ct);
            var (from, to) = MetadataWindow(p, time);
            var warnings = new List<string>();
            var series = await SelectAsync(evaluator, p.All("match[]"), from, to, warnings, ct, unscopedWarning: true);
            var names = new SortedSet<string>(StringComparer.Ordinal) { "__name__", "service_name" };
            foreach (var s in series)
            {
                names.UnionWith(s.Labels.Keys);
            }

            return Success(w => WriteStringArray(w, "data", names), warnings, dataIsProperty: true);
        });

    private static async Task<IResult> HandleLabelValuesAsync(
        string name, HttpContext http, PromEvaluator evaluator, TimeProvider time, CancellationToken ct) =>
        await GuardAsync(async () =>
        {
            var p = await ReadParametersAsync(http, ct);
            var (from, to) = MetadataWindow(p, time);
            var warnings = new List<string>();
            var values = new SortedSet<string>(StringComparer.Ordinal);
            var matches = p.All("match[]");

            if (name == "__name__" && matches.Count == 0)
            {
                // Metric names need no series scan: the catalog already lists them.
                foreach (var info in await evaluator.KnownMetricsAsync(from, to, ct))
                {
                    values.UnionWith(PromNames.PromNamesFor(info.MetricName, info.Unit, info.Type)
                        .Where(n => !(n.Kind == PromMetricKind.HistogramBucket && !n.PromName.EndsWith("_bucket", StringComparison.Ordinal)))
                        .Select(n => n.PromName));
                }
            }
            else if (name == "service_name" && matches.Count == 0)
            {
                values.UnionWith((await evaluator.KnownMetricsAsync(from, to, ct)).Select(i => i.ServiceName));
            }
            else
            {
                var series = await SelectAsync(evaluator, matches, from, to, warnings, ct, unscopedWarning: true);
                foreach (var s in series)
                {
                    if (s.Labels.TryGetValue(name, out var value))
                    {
                        values.Add(value);
                    }
                }
            }

            return Success(w => WriteStringArray(w, "data", values), warnings, dataIsProperty: true);
        });

    private static IResult HandleBuildInfo() =>
        Success(w =>
        {
            // Grafana's data-source test reads this; the version only needs to look like a Prometheus 2.x.
            w.WriteString("version", "2.55.0-flare");
            w.WriteString("revision", "flare");
            w.WriteString("branch", "flare");
            w.WriteString("buildUser", "flare");
            w.WriteString("buildDate", "");
            w.WriteString("goVersion", "n/a");
        }, [], dataIsProperty: false);

    /// <summary>Series for <c>match[]</c> selectors, or - with none - a bounded sample across the busiest metrics.</summary>
    private static async Task<IReadOnlyList<PromRawSeries>> SelectAsync(
        PromEvaluator evaluator,
        IReadOnlyList<string> matches,
        DateTimeOffset from,
        DateTimeOffset to,
        List<string> warnings,
        CancellationToken ct,
        bool unscopedWarning = false)
    {
        var selectors = new List<PromSelector>();
        foreach (var match in matches)
        {
            selectors.Add(PromQlParser.Parse(match) as PromSelector is { Range: null } s
                ? s
                : throw new PromQlException($"match[] '{match}' must be a plain vector selector."));
        }

        if (selectors.Count == 0)
        {
            var known = await evaluator.KnownMetricsAsync(from, to, ct);
            var busiest = known
                .GroupBy(i => (i.MetricName, i.Type, i.Unit))
                .OrderByDescending(g => g.Sum(i => i.SeriesCount))
                .Take(UnscopedMetricLimit);
            foreach (var g in busiest)
            {
                var (promName, _) = PromNames.PromNamesFor(g.Key.MetricName, g.Key.Unit, g.Key.Type).First();
                selectors.Add(new PromSelector(promName, [], null));
            }

            if (unscopedWarning)
            {
                warnings.Add($"no match[] given; sampled the {UnscopedMetricLimit} metrics with the most series. Pass match[] for a complete answer.");
            }
        }

        var result = new List<PromRawSeries>();
        foreach (var selector in selectors)
        {
            result.AddRange(await evaluator.SelectSeriesAsync(selector, from, to, warnings, ct));
        }

        return result;
    }

    private static (DateTimeOffset From, DateTimeOffset To) MetadataWindow(Parameters p, TimeProvider time)
    {
        var to = p.Single("end") is { } e ? ParseTime(e, "end") : time.GetUtcNow();
        var from = p.Single("start") is { } s ? ParseTime(s, "start") : to - DefaultMetadataWindow;
        return from <= to ? (from, to) : throw new PromQlException("end timestamp must not be before start time.");
    }

    private static async Task<IResult> GuardAsync(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (PromQlException ex)
        {
            return Error(StatusCodes.Status400BadRequest, "bad_data", ex.Message);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return Error(StatusCodes.Status400BadRequest, "bad_data", ex.Message);
        }
    }

    private static IResult Error(int status, string errorType, string message) =>
        Results.Text(
            System.Text.Encoding.UTF8.GetString(Write(w =>
            {
                w.WriteString("status", "error");
                w.WriteString("errorType", errorType);
                w.WriteString("error", message);
            })),
            "application/json",
            statusCode: status);

    private static IResult Success(Action<Utf8JsonWriter> writeData, List<string> warnings, bool dataIsProperty = false) =>
        Results.Bytes(
            Write(w =>
            {
                w.WriteString("status", "success");
                if (dataIsProperty)
                {
                    // The callback writes the "data" property itself (an array, not an object).
                    writeData(w);
                }
                else
                {
                    w.WriteStartObject("data");
                    writeData(w);
                    w.WriteEndObject();
                }

                if (warnings.Count > 0)
                {
                    WriteStringArray(w, "warnings", warnings);
                }
            }),
            "application/json");

    private static byte[] Write(Action<Utf8JsonWriter> body)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            body(writer);
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    private static void WriteStringArray(Utf8JsonWriter w, string property, IEnumerable<string> values)
    {
        w.WriteStartArray(property);
        foreach (var v in values)
        {
            w.WriteStringValue(v);
        }

        w.WriteEndArray();
    }

    private static void WriteLabels(Utf8JsonWriter w, string property, Dictionary<string, string> labels)
    {
        w.WritePropertyName(property);
        WriteLabelsObject(w, labels);
    }

    private static void WriteLabelsObject(Utf8JsonWriter w, Dictionary<string, string> labels)
    {
        w.WriteStartObject();
        foreach (var (key, value) in labels.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            w.WriteString(key, value);
        }

        w.WriteEndObject();
    }

    private static void WriteValue(Utf8JsonWriter w, long timestampMs, double value)
    {
        w.WriteStartArray();
        w.WriteRawValue((timestampMs / 1000.0).ToString("0.###", CultureInfo.InvariantCulture));
        w.WriteStringValue(FormatSample(value));
        w.WriteEndArray();
    }

    /// <summary>Prometheus's sample-value text: <c>NaN</c>, <c>+Inf</c>, <c>-Inf</c> or a round-trippable number.</summary>
    internal static string FormatSample(double value) =>
        double.IsNaN(value) ? "NaN"
        : double.IsPositiveInfinity(value) ? "+Inf"
        : double.IsNegativeInfinity(value) ? "-Inf"
        : value.ToString("R", CultureInfo.InvariantCulture);

    /// <summary>A Prometheus timestamp: unix seconds (fractional allowed) or RFC 3339.</summary>
    internal static DateTimeOffset ParseTime(string text, string name)
    {
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            && double.IsFinite(seconds) && Math.Abs(seconds) < 1e11)
        {
            return DateTimeOffset.FromUnixTimeMilliseconds((long)Math.Round(seconds * 1000));
        }

        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : throw new PromQlException($"invalid parameter '{name}': cannot parse \"{text}\" to a valid timestamp.");
    }

    /// <summary>A Prometheus step: float seconds or a duration such as <c>15s</c>.</summary>
    internal static TimeSpan ParseStep(string text)
    {
        var step = double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            ? TimeSpan.FromSeconds(seconds)
            : PromQlParser.ParseDuration(text);
        return step > TimeSpan.Zero
            ? step
            : throw new PromQlException("zero or negative query resolution step widths are not accepted. Try a positive integer.");
    }

    private static async Task<Parameters> ReadParametersAsync(HttpContext http, CancellationToken ct)
    {
        var values = new Dictionary<string, StringValues>(StringComparer.Ordinal);
        foreach (var (key, value) in http.Request.Query)
        {
            values[key] = value;
        }

        if (http.Request.HasFormContentType)
        {
            foreach (var (key, value) in await http.Request.ReadFormAsync(ct))
            {
                values[key] = values.TryGetValue(key, out var existing) ? StringValues.Concat(existing, value) : value;
            }
        }

        return new Parameters(values);
    }

    private sealed class Parameters(Dictionary<string, StringValues> values)
    {
        public string? Single(string name) => values.TryGetValue(name, out var v) && v.Count > 0 ? v[0] : null;

        public IReadOnlyList<string> All(string name) =>
            values.TryGetValue(name, out var v) ? v.Where(s => !string.IsNullOrEmpty(s)).Select(s => s!).ToList() : [];
    }
}
