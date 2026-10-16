using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using ClickHouse.Driver;
using Microsoft.Extensions.Primitives;

namespace Flare.Api.Loki;

/// <summary>
/// A read-only subset of Grafana Loki's HTTP API (ADR-0236) under <c>/loki/api/v1</c>, so Grafana's Loki
/// data source can read Flare logs: <c>query_range</c> and <c>query</c> (LogQL log and metric queries),
/// <c>labels</c>, <c>label/&lt;name&gt;/values</c>, <c>series</c>, <c>index/stats</c> and
/// <c>status/buildinfo</c>. GET and form-encoded POST both work. Authentication is the normal Flare
/// pipeline - a personal access token as <c>Authorization: Bearer flr_pat_...</c> (Grafana: a custom HTTP
/// header on the data source). The data source URL is the server root, e.g. <c>http://flare:8080</c>.
/// </summary>
/// <remarks>
/// Responses are written with <see cref="Utf8JsonWriter"/>: the envelope is Loki's fixed wire format, not
/// one of Flare's MemoryPack-negotiated DTOs. Errors are plain text with a status code, as Loki writes
/// them; unsupported LogQL is a 400 that names the construct, never a partial result.
/// </remarks>
public static class LokiEndpoints
{
    private static readonly TimeSpan DefaultQueryWindow = TimeSpan.FromHours(1);
    private static readonly TimeSpan DefaultMetadataWindow = TimeSpan.FromHours(6);

    /// <summary>ClickHouse's <c>CANNOT_COMPILE_REGEXP</c> error code.</summary>
    private const int CannotCompileRegexp = 427;

    public static IEndpointRouteBuilder MapLokiEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var loki = endpoints.MapGroup("/loki/api/v1");
        loki.MapMethods("/query_range", ["GET", "POST"], HandleQueryRangeAsync);
        loki.MapMethods("/query", ["GET", "POST"], HandleQueryAsync);
        loki.MapMethods("/labels", ["GET", "POST"], HandleLabelsAsync);
        loki.MapMethods("/label/{name}/values", ["GET", "POST"], HandleLabelValuesAsync);
        loki.MapMethods("/series", ["GET", "POST"], HandleSeriesAsync);
        loki.MapMethods("/index/stats", ["GET", "POST"], HandleStatsAsync);
        loki.MapGet("/status/buildinfo", () => Json(w =>
        {
            w.WriteString("version", "3.0.0-flare");
            w.WriteString("revision", "flare");
            w.WriteString("branch", "flare");
            w.WriteString("buildUser", "flare");
            w.WriteString("buildDate", "");
            w.WriteString("goVersion", "n/a");
        }));
        return endpoints;
    }

    private static async Task<IResult> HandleQueryRangeAsync(HttpContext http, LokiQueryService service, TimeProvider time, CancellationToken ct) =>
        await GuardAsync(async () =>
        {
            var p = await ReadParametersAsync(http, ct);
            var expr = LokiQlParser.Parse(p.Single("query") ?? throw new LokiException("parameter 'query' is required."));
            var (start, end) = QueryWindow(p, time, DefaultQueryWindow);

            if (expr is LokiLogSelector selector)
            {
                var limit = p.Single("limit") is { } l ? ParseInt(l, "limit") : LokiQueries.DefaultLimit;
                var forward = p.Single("direction") switch
                {
                    null or "" or "backward" or "BACKWARD" => false,
                    "forward" or "FORWARD" => true,
                    var other => throw new LokiException($"invalid direction '{other}': expected 'forward' or 'backward'."),
                };
                var entries = await service.QueryLogsAsync(selector, start, end, limit, forward, ct);
                var categorize = http.Request.Headers["X-Loki-Response-Encoding-Flags"].ToString().Contains("categorize-labels", StringComparison.Ordinal);
                return Success(w => WriteStreams(w, entries, categorize));
            }

            var step = p.Single("step") is { } stepText
                ? ParseStep(stepText)
                : TimeSpan.FromSeconds(Math.Max(1, Math.Floor((end - start).TotalSeconds / 250)));
            var matrix = await service.EvaluateAsync(expr, start, end, step, instant: false, ct);
            return Success(w =>
            {
                w.WriteString("resultType", "matrix");
                w.WriteStartArray("result");
                foreach (var series in matrix.Series.Where(s => s.Values.Any(v => v.HasValue)))
                {
                    w.WriteStartObject();
                    WriteLabels(w, "metric", series.Labels);
                    w.WriteStartArray("values");
                    for (var i = 0; i < matrix.Steps.Length; i++)
                    {
                        if (series.Values[i] is { } value)
                        {
                            WriteSample(w, matrix.Steps[i], value);
                        }
                    }

                    w.WriteEndArray();
                    w.WriteEndObject();
                }

                w.WriteEndArray();
            });
        });

    private static async Task<IResult> HandleQueryAsync(HttpContext http, LokiQueryService service, TimeProvider time, CancellationToken ct) =>
        await GuardAsync(async () =>
        {
            var p = await ReadParametersAsync(http, ct);
            var expr = LokiQlParser.Parse(p.Single("query") ?? throw new LokiException("parameter 'query' is required."));
            if (expr is LokiLogSelector)
            {
                throw new LokiException("instant queries on a log selector are not supported; use query_range.");
            }

            var at = p.Single("time") is { } t ? ParseTime(t, "time") : time.GetUtcNow();
            var matrix = await service.EvaluateAsync(expr, at, at, TimeSpan.FromSeconds(1), instant: true, ct);
            var atMs = at.ToUnixTimeMilliseconds();
            return Success(w =>
            {
                if (matrix.IsScalar)
                {
                    w.WriteString("resultType", "scalar");
                    w.WritePropertyName("result");
                    WriteSample(w, atMs, matrix.Series[0].Values[0]!.Value);
                    return;
                }

                w.WriteString("resultType", "vector");
                w.WriteStartArray("result");
                foreach (var series in matrix.Series.Where(s => s.Values[0].HasValue))
                {
                    w.WriteStartObject();
                    WriteLabels(w, "metric", series.Labels);
                    w.WritePropertyName("value");
                    WriteSample(w, atMs, series.Values[0]!.Value);
                    w.WriteEndObject();
                }

                w.WriteEndArray();
            });
        });

    private static async Task<IResult> HandleLabelsAsync(HttpContext http, LokiQueryService service, TimeProvider time, CancellationToken ct) =>
        await GuardAsync(async () =>
        {
            var p = await ReadParametersAsync(http, ct);
            var (start, end) = QueryWindow(p, time, DefaultMetadataWindow);
            var labels = await service.LabelsAsync(start, end, ct);
            return SuccessList(labels);
        });

    private static async Task<IResult> HandleLabelValuesAsync(string name, HttpContext http, LokiQueryService service, TimeProvider time, CancellationToken ct) =>
        await GuardAsync(async () =>
        {
            var p = await ReadParametersAsync(http, ct);
            var (start, end) = QueryWindow(p, time, DefaultMetadataWindow);
            var selector = p.Single("query") is { Length: > 0 } q ? LokiQlParser.ParseSelector(q) : null;
            var values = await service.LabelValuesAsync(name, selector, start, end, ct);
            return SuccessList(values);
        });

    private static async Task<IResult> HandleSeriesAsync(HttpContext http, LokiQueryService service, TimeProvider time, CancellationToken ct) =>
        await GuardAsync(async () =>
        {
            var p = await ReadParametersAsync(http, ct);
            var (start, end) = QueryWindow(p, time, DefaultMetadataWindow);
            var selectors = p.All("match[]").Concat(p.All("match")).Select(LokiQlParser.ParseSelector).ToList();
            var streams = await service.SeriesAsync(selectors, start, end, ct);
            return Json(w =>
            {
                w.WriteString("status", "success");
                w.WriteStartArray("data");
                foreach (var labels in streams)
                {
                    w.WriteStartObject();
                    foreach (var (key, value) in labels)
                    {
                        w.WriteString(key, value);
                    }

                    w.WriteEndObject();
                }

                w.WriteEndArray();
            });
        });

    private static async Task<IResult> HandleStatsAsync(HttpContext http, LokiQueryService service, TimeProvider time, CancellationToken ct) =>
        await GuardAsync(async () =>
        {
            var p = await ReadParametersAsync(http, ct);
            var (start, end) = QueryWindow(p, time, DefaultQueryWindow);
            var selector = LokiQlParser.ParseSelector(p.Single("query") ?? throw new LokiException("parameter 'query' is required."));
            var stats = await service.StatsAsync(selector, start, end, ct);
            return Json(w =>
            {
                w.WriteNumber("streams", stats.Streams);
                w.WriteNumber("chunks", 0);
                w.WriteNumber("bytes", stats.Bytes);
                w.WriteNumber("entries", stats.Entries);
            });
        });

    /// <summary>
    /// <c>resultType: streams</c>. With Loki's <c>categorize-labels</c> encoding flag (which Grafana sends)
    /// the stream carries only the indexed labels and each entry a third element with its structured
    /// metadata and parsed labels; without it everything is merged into the stream labels, as Loki 2 did.
    /// </summary>
    internal static void WriteStreams(Utf8JsonWriter w, IReadOnlyList<LokiLogEntry> entries, bool categorize)
    {
        var streams = new List<(IReadOnlyDictionary<string, string> Labels, List<LokiLogEntry> Entries)>();
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            var labels = categorize
                ? entry.StreamLabels
                : entry.StreamLabels
                    .Concat(entry.StructuredMetadata)
                    .Concat(entry.Parsed)
                    .GroupBy(kv => kv.Key, StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => g.Last().Value, StringComparer.Ordinal);
            var key = string.Join('\u0001', labels.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Key + "\u0002" + kv.Value));
            if (!index.TryGetValue(key, out var position))
            {
                position = streams.Count;
                index[key] = position;
                streams.Add((labels, []));
            }

            streams[position].Entries.Add(entry);
        }

        w.WriteString("resultType", "streams");
        if (categorize)
        {
            // Grafana's parser reads the third element of an entry only when the response declares it.
            w.WriteStartArray("encodingFlags");
            w.WriteStringValue("categorize-labels");
            w.WriteEndArray();
        }

        w.WriteStartArray("result");
        foreach (var (labels, members) in streams)
        {
            w.WriteStartObject();
            WriteLabels(w, "stream", labels);
            w.WriteStartArray("values");
            foreach (var entry in members)
            {
                w.WriteStartArray();
                w.WriteStringValue(entry.Ns.ToString(CultureInfo.InvariantCulture));
                w.WriteStringValue(entry.Line);
                if (categorize)
                {
                    w.WriteStartObject();
                    if (entry.StructuredMetadata.Count > 0)
                    {
                        WriteLabels(w, "structuredMetadata", entry.StructuredMetadata);
                    }

                    if (entry.Parsed.Count > 0)
                    {
                        WriteLabels(w, "parsed", entry.Parsed);
                    }

                    w.WriteEndObject();
                }

                w.WriteEndArray();
            }

            w.WriteEndArray();
            w.WriteEndObject();
        }

        w.WriteEndArray();
    }

    private static async Task<IResult> GuardAsync(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (Exception ex) when (ex is LokiException or ArgumentException)
        {
            return Error(StatusCodes.Status400BadRequest, ex.Message);
        }
        catch (ClickHouseServerException ex) when (ex.ErrorCode == CannotCompileRegexp)
        {
            // ClickHouse compiles the pattern (RE2) when the query runs; say so rather than surface its stack trace as a 500.
            return Error(StatusCodes.Status400BadRequest, "invalid regular expression in the query (RE2 syntax).");
        }
    }

    private static IResult Error(int status, string message) => Results.Text(message, "text/plain", statusCode: status);

    private static IResult Success(Action<Utf8JsonWriter> writeData) =>
        Json(w =>
        {
            w.WriteString("status", "success");
            w.WriteStartObject("data");
            writeData(w);
            w.WriteEndObject();
        });

    private static IResult SuccessList(IEnumerable<string> values) =>
        Json(w =>
        {
            w.WriteString("status", "success");
            w.WriteStartArray("data");
            foreach (var value in values)
            {
                w.WriteStringValue(value);
            }

            w.WriteEndArray();
        });

    private static IResult Json(Action<Utf8JsonWriter> body)
    {
        using var stream = new MemoryStream();
        // Relaxed escaping: log lines are full of quotes and angle brackets, and \u0022 for every quote is noise in a response meant for a log viewer.
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStartObject();
            body(writer);
            writer.WriteEndObject();
        }

        return Results.Bytes(stream.ToArray(), "application/json");
    }

    private static void WriteLabels(Utf8JsonWriter w, string property, IReadOnlyDictionary<string, string> labels)
    {
        w.WriteStartObject(property);
        foreach (var (key, value) in labels.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            w.WriteString(key, value);
        }

        w.WriteEndObject();
    }

    private static void WriteSample(Utf8JsonWriter w, long timestampMs, double value)
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

    /// <summary>
    /// <c>start</c>/<c>end</c> (or <c>since</c>) of a request. <c>end</c> defaults to now and <c>start</c>
    /// to <paramref name="defaultWindow"/> before it.
    /// </summary>
    private static (DateTimeOffset Start, DateTimeOffset End) QueryWindow(Parameters p, TimeProvider time, TimeSpan defaultWindow)
    {
        var end = p.Single("end") is { } e ? ParseTime(e, "end") : time.GetUtcNow();
        var start = p.Single("start") is { } s ? ParseTime(s, "start")
            : p.Single("since") is { } since ? end - LokiQlParser.ParseDuration(since)
            : end - defaultWindow;
        return start <= end ? (start, end) : throw new LokiException("end timestamp must not be before start time.");
    }

    /// <summary>
    /// A Loki timestamp: unix nanoseconds, or seconds / milliseconds / microseconds by magnitude, or RFC 3339.
    /// Grafana sends nanoseconds.
    /// </summary>
    internal static DateTimeOffset ParseTime(string text, string name)
    {
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number))
        {
            var magnitude = Math.Abs(number);
            var ms = magnitude switch
            {
                >= 1e17 => number / 1e6,
                >= 1e14 => number / 1e3,
                >= 1e11 => number,
                _ => number * 1000,
            };
            return DateTimeOffset.FromUnixTimeMilliseconds((long)Math.Round(ms));
        }

        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : throw new LokiException($"invalid parameter '{name}': cannot parse \"{text}\" to a valid timestamp.");
    }

    /// <summary>A step: float seconds or a duration such as <c>15s</c>.</summary>
    internal static TimeSpan ParseStep(string text)
    {
        var step = double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            ? TimeSpan.FromSeconds(seconds)
            : LokiQlParser.ParseDuration(text);
        return step > TimeSpan.Zero
            ? step
            : throw new LokiException("zero or negative query resolution step widths are not accepted. Try a positive integer.");
    }

    private static int ParseInt(string text, string name) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0
            ? value
            : throw new LokiException($"invalid parameter '{name}': expected a positive integer.");

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
