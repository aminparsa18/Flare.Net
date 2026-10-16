using System.Globalization;
using System.Text.Json;
using ClickHouse.Driver;
using Flare.Api.Model;
using Flare.Api.Query;

namespace Flare.Api.Tempo;

/// <summary>
/// A read-only subset of Grafana Tempo's HTTP API (ADR-0217) under <c>/tempo</c>, so Grafana's Tempo data
/// source can read Flare traces: <c>/tempo/api/echo</c> (the connection test), trace by id (v1 and v2, JSON
/// or protobuf), <c>/tempo/api/search</c> (TraceQL subset or <c>tags</c>), tag and tag-value discovery
/// (v1 and v2), and TraceQL metrics (<c>/tempo/api/metrics/query_range</c> and <c>/query</c>, ADR-0221). Authentication is the normal Flare pipeline - a personal access token as
/// <c>Authorization: Bearer flr_pat_...</c>.
/// </summary>
/// <remarks>
/// <para>
/// Under <c>/tempo</c> rather than at <c>/api</c> because Flare already serves <c>GET /api/traces/{id}</c>
/// with its own shape; Grafana's data source URL is the prefix, <c>http://flare:8080/tempo</c>.
/// </para>
/// <para>
/// Every endpoint is a thin shell over <see cref="ISpanQueryService"/>, so scoping, query caps and the
/// promoted-attribute columns apply as they do for the dashboard. Errors are plain text with a status
/// code, as Tempo writes them; an unsupported TraceQL construct is a 400 that names it.
/// </para>
/// </remarks>
public static class TempoEndpoints
{
    private const int DefaultSearchLimit = 20;
    private const int MaxSearchLimit = 200;
    private const int DefaultTagLimit = 200;

    /// <summary>ClickHouse's <c>CANNOT_COMPILE_REGEXP</c> error code.</summary>
    private const int CannotCompileRegexp = 427;

    /// <summary>Tempo's default <c>spss</c>: how many matching spans each result lists.</summary>
    private const int DefaultSpansPerSpanSet = 3;

    /// <summary>The intrinsics the search subset understands, as Tempo's <c>intrinsic</c> tag scope lists them.</summary>
    private static readonly string[] Intrinsics = ["duration", "name", "rootName", "rootServiceName", "status", "traceDuration"];

    public static IEndpointRouteBuilder MapTempoEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var tempo = endpoints.MapGroup("/tempo/api");
        tempo.MapGet("/echo", () => Results.Text("echo", "text/plain"));
        tempo.MapGet("/traces/{traceId}", (string traceId, HttpContext http, ISpanQueryService spans, CancellationToken ct) =>
            HandleTraceAsync(traceId, http, spans, v2: false, ct));
        tempo.MapGet("/v2/traces/{traceId}", (string traceId, HttpContext http, ISpanQueryService spans, CancellationToken ct) =>
            HandleTraceAsync(traceId, http, spans, v2: true, ct));
        tempo.MapGet("/search", HandleSearchAsync);
        tempo.MapGet("/metrics/query_range", (HttpContext http, ISpanQueryService spans, TimeProvider time, CancellationToken ct) =>
            HandleMetricsAsync(http, spans, time, instant: false, ct));
        tempo.MapGet("/metrics/query", (HttpContext http, ISpanQueryService spans, TimeProvider time, CancellationToken ct) =>
            HandleMetricsAsync(http, spans, time, instant: true, ct));
        tempo.MapGet("/search/tags", (HttpContext http, ISpanQueryService spans, TimeProvider time, CancellationToken ct) =>
            HandleTagsAsync(http, spans, time, v2: false, ct));
        tempo.MapGet("/v2/search/tags", (HttpContext http, ISpanQueryService spans, TimeProvider time, CancellationToken ct) =>
            HandleTagsAsync(http, spans, time, v2: true, ct));
        tempo.MapGet("/search/tag/{tag}/values", (string tag, HttpContext http, ISpanQueryService spans, CancellationToken ct) =>
            HandleTagValuesAsync(tag, http, spans, v2: false, ct));
        tempo.MapGet("/v2/search/tag/{tag}/values", (string tag, HttpContext http, ISpanQueryService spans, CancellationToken ct) =>
            HandleTagValuesAsync(tag, http, spans, v2: true, ct));
        return endpoints;
    }

    private static async Task<IResult> HandleTraceAsync(string traceId, HttpContext http, ISpanQueryService spans, bool v2, CancellationToken ct)
    {
        if (NormalizeTraceId(traceId) is not { } id)
        {
            return Error(StatusCodes.Status400BadRequest, $"invalid trace id '{traceId}': expected up to 32 hex characters.");
        }

        var trace = await spans.GetTraceAsync(id, ct);
        if (trace is null)
        {
            return Error(StatusCodes.Status404NotFound, "trace not found");
        }

        // Flare keeps a very large trace to its first levels (SpanQueryService.GetTraceAsync); v2 can say so.
        var partial = v2 && trace.Truncated
            ? $"the trace is larger than {TraceByIdQueryBuilder.MaxSpans} spans; Flare returned its first {trace.Spans.Count} (top levels first)."
            : null;
        var protobuf = WantsProtobuf(http);
        var body = (protobuf, v2) switch
        {
            (true, true) => TempoTraceEncoder.ToTraceByIdResponseProtobuf(trace.Spans, partial),
            (true, false) => TempoTraceEncoder.ToTraceProtobuf(trace.Spans),
            (false, true) => TempoTraceEncoder.ToTraceByIdResponseJson(trace.Spans, partial),
            _ => TempoTraceEncoder.ToTraceJson(trace.Spans),
        };
        return Results.Bytes(body, protobuf ? "application/protobuf" : "application/json");
    }

    private static async Task<IResult> HandleSearchAsync(HttpContext http, ISpanQueryService spans, CancellationToken ct)
    {
        var query = http.Request.Query;
        try
        {
            var q = query["q"].ToString();
            var tags = query["tags"].ToString();
            if (q.Length > 0 && tags.Length > 0)
            {
                return Error(StatusCodes.Status400BadRequest, "use either 'q' or 'tags', not both.");
            }

            var plan = q.Length > 0
                ? TempoSearchPlanner.Plan(TraceQlParser.Parse(q))
                : TempoSearchPlanner.PlanTags(tags);
            plan = TempoSearchPlanner.WithDurations(plan, query["minDuration"], query["maxDuration"]);

            var (from, to) = Window(query);
            var limit = ParseLimit(query["limit"], DefaultSearchLimit, MaxSearchLimit);
            var filter = new SpanFilter
            {
                From = from,
                To = to,
                RootSpansOnly = true,
                Services = plan.RootServices.Count == 0 ? null : plan.RootServices,
                Names = plan.RootNames.Count == 0 ? null : plan.RootNames,
                MinDurationNano = plan.MinTraceDurationNano,
                MaxDurationNano = plan.MaxTraceDurationNano,
                Structure = plan.Structure,
            };

            var spansPerSpanSet = ParseLimit(query["spss"], DefaultSpansPerSpanSet, TraceMatchedSpansQueryBuilder.MaxSpansPerTrace);

            var response = await spans.SearchAsync(new SpanSearchRequest { Filter = filter, PageSize = limit }, ct);

            // Which spans of each trace matched, for Grafana's nested result rows. A query with nothing span-specific
            // (only trace-level fields, or { }) has no spans to point at.
            IReadOnlyDictionary<string, TraceMatchedSpans> matched = new Dictionary<string, TraceMatchedSpans>();
            IReadOnlyDictionary<string, TraceSelectedSpans> selected = new Dictionary<string, TraceSelectedSpans>();
            if (plan.Structure is { } structure && response.Spans.Count > 0)
            {
                var traceIds = response.Spans.Select(r => r.TraceId).ToArray();
                if (plan.UsesSelector)
                {
                    // select() and &>> (ADR-0227): the spans come with their attribute values, and the selector decides which are listed.
                    var rows = await spans.GetSelectedSpansAsync(SelectedSpansRequestFor(traceIds, structure, plan), ct);
                    selected = TempoSpanSelector.Select(rows, plan, spansPerSpanSet).ToDictionary(t => t.TraceId, StringComparer.Ordinal);
                }
                else
                {
                    var found = await spans.GetMatchedSpansAsync(new MatchedSpansRequest(traceIds, structure, spansPerSpanSet), ct);
                    matched = found.ToDictionary(t => t.TraceId, StringComparer.Ordinal);
                }
            }

            return Json(w =>
            {
                w.WriteStartObject();
                w.WriteStartArray("traces");
                foreach (var root in response.Spans)
                {
                    w.WriteStartObject();
                    w.WriteString("traceID", root.TraceId);
                    if (root.ServiceName.Length > 0)
                    {
                        w.WriteString("rootServiceName", root.ServiceName);
                    }

                    w.WriteString("rootTraceName", root.Name);
                    w.WriteString("startTimeUnixNano", TempoTraceEncoder.UnixNanos(root.StartTime).ToString(CultureInfo.InvariantCulture));
                    w.WriteNumber("durationMs", root.DurationNano / 1_000_000);
                    if (matched.TryGetValue(root.TraceId, out var traceSpans) && traceSpans.Spans.Count > 0)
                    {
                        WriteSpanSets(w, set => WriteSpanSet(set, traceSpans));
                    }
                    else if (selected.TryGetValue(root.TraceId, out var selectedSpans) && selectedSpans.Spans.Count > 0)
                    {
                        WriteSpanSets(w, set => WriteSpanSet(set, selectedSpans));
                    }

                    w.WriteEndObject();
                }

                w.WriteEndArray();
                w.WriteStartObject("metrics");
                w.WriteNumber("inspectedTraces", response.Spans.Count);
                w.WriteEndObject();
                w.WriteEndObject();
            });
        }
        catch (Exception ex) when (ex is TraceQlException or ArgumentException)
        {
            return Error(StatusCodes.Status400BadRequest, ex.Message);
        }
        catch (ClickHouseServerException ex) when (ex.ErrorCode == CannotCompileRegexp)
        {
            // ClickHouse compiles the pattern (RE2) when the query runs; say so rather than surface its stack trace as a 500.
            return Error(StatusCodes.Status400BadRequest, "invalid regular expression in the query (RE2 syntax).");
        }
    }

    /// <summary>The keys a select path reads: the fields of <c>select()</c> and the attributes the conditions compared on, per bag.</summary>
    internal static SelectedSpansRequest SelectedSpansRequestFor(IReadOnlyList<string> traceIds, TraceStructureFilter structure, TempoSearchPlan plan)
    {
        var (spanKeys, resourceKeys) = TraceMatchedSpansQueryBuilder.EchoedKeys(structure.Conditions);
        var span = spanKeys.ToList();
        var resource = resourceKeys.ToList();
        var events = new List<string>();
        foreach (var field in plan.Select)
        {
            var keys = field.Scope switch
            {
                TempoSelectScope.Span => span,
                TempoSelectScope.Resource => resource,
                TempoSelectScope.Event => events,
                _ => null,
            };
            if (keys is not null && !keys.Contains(field.Key, StringComparer.Ordinal))
            {
                keys.Add(field.Key);
            }
        }

        return new SelectedSpansRequest(traceIds, structure, span, resource, events, plan.NeedsWholeTrace,
            plan.Select.Any(f => f.Scope == TempoSelectScope.Intrinsic && f.Key == "statusMessage"));
    }

    /// <summary>Tempo writes the spanset twice: the list, and the first one under its deprecated singular name.</summary>
    private static void WriteSpanSets(Utf8JsonWriter w, Action<Utf8JsonWriter> spanSet)
    {
        w.WriteStartArray("spanSets");
        spanSet(w);
        w.WriteEndArray();
        w.WritePropertyName("spanSet");
        spanSet(w);
    }

    /// <summary>TraceQL metrics (ADR-0221): <c>/metrics/query_range</c> is a series per group, <c>/metrics/query</c> one value per group.</summary>
    private static async Task<IResult> HandleMetricsAsync(HttpContext http, ISpanQueryService spans, TimeProvider time, bool instant, CancellationToken ct)
    {
        var query = http.Request.Query;
        try
        {
            var plan = TempoMetricsPlanner.Plan(TraceQlParser.ParseMetrics(query["q"].ToString()));
            var window = TempoMetricsWindow.Resolve(
                query["start"], query["end"], query["since"], query["step"], time.GetUtcNow(), instant, TraceQlMetricsQueryBuilder.MaxSteps);


            // Exemplars (ADR-0223) are for charts, so a range query only: `exemplars=n` as Grafana sends it, or with(exemplars=true) in the query.
            var exemplarCount = instant ? 0 : ExemplarCount(query["exemplars"], plan.Exemplars);
            var request = new SpanMetricsRequest(
                plan.Condition,
                plan.Function,
                plan.Quantiles,
                plan.GroupBy,
                DateTimeOffset.FromUnixTimeSeconds(window.FromSeconds),
                DateTimeOffset.FromUnixTimeSeconds(window.ToSeconds),
                window.StepSeconds,
                plan.Value,
                exemplarCount,
                plan.Ancestor,
                plan.Root,
                plan.Compare,
                plan.Spansets);

            TempoMetricsResult result;
            if (plan.Function == SpanMetricFunction.Compare)
            {
                result = TempoCompareSeries.Build(plan, await spans.GetSpanCompareAsync(request, ct), window);
            }
            else
            {
                var rows = await spans.GetSpanMetricsAsync(request, ct);
                var exemplars = exemplarCount > 0 && rows.Count > 0 ? await spans.GetSpanExemplarsAsync(request, ct) : null;
                result = TempoMetricsSeries.Build(plan, rows, window, exemplars);
            }

            return Json(w =>
            {
                if (instant)
                {
                    TempoMetricsSeries.WriteInstant(w, result);
                }
                else
                {
                    TempoMetricsSeries.WriteRange(w, result);
                }
            });
        }
        catch (Exception ex) when (ex is TraceQlException or ArgumentException)
        {
            return Error(StatusCodes.Status400BadRequest, ex.Message);
        }
        catch (ClickHouseServerException ex) when (ex.ErrorCode == CannotCompileRegexp)
        {
            return Error(StatusCodes.Status400BadRequest, "invalid regular expression in the query (RE2 syntax).");
        }
    }

    /// <summary>How many exemplars to return: the <c>exemplars</c> parameter if it is a positive number, else <see cref="DefaultExemplars"/> when the query said <c>with(exemplars=true)</c>, else none.</summary>
    internal static int ExemplarCount(string? parameter, bool queryAsked)
    {
        if (int.TryParse(parameter, NumberStyles.None, CultureInfo.InvariantCulture, out var count))
        {
            return Math.Min(count, TraceQlMetricsQueryBuilder.MaxExemplars);
        }

        return queryAsked ? DefaultExemplars : 0;
    }

    private const int DefaultExemplars = 100;

    internal static void WriteSpanSet(Utf8JsonWriter w, TraceMatchedSpans trace)
    {
        const string ServiceNameKey = "service.name";

        w.WriteStartObject();
        w.WriteStartArray("spans");
        foreach (var span in trace.Spans)
        {
            w.WriteStartObject();
            w.WriteString("spanID", span.SpanId);
            w.WriteString("name", span.Name);
            w.WriteString("startTimeUnixNano", TempoTraceEncoder.UnixNanos(span.StartTime).ToString(CultureInfo.InvariantCulture));
            w.WriteString("durationNanos", span.DurationNano.ToString(CultureInfo.InvariantCulture));

            // service.name always, then the attributes the query compared on (Flare stores every value as a string).
            var echoed = span.Attributes.Where(a => a.Key != ServiceNameKey).ToArray();
            if (span.ServiceName.Length > 0 || echoed.Length > 0)
            {
                w.WriteStartArray("attributes");
                if (span.ServiceName.Length > 0)
                {
                    WriteAttribute(w, ServiceNameKey, span.ServiceName);
                }

                foreach (var (key, value) in echoed)
                {
                    WriteAttribute(w, key, value, infer: true);
                }

                w.WriteEndArray();
            }

            w.WriteEndObject();
        }

        w.WriteEndArray();
        w.WriteNumber("matched", trace.Matched);
        w.WriteEndObject();
    }

    internal static void WriteSpanSet(Utf8JsonWriter w, TraceSelectedSpans trace)
    {
        w.WriteStartObject();
        w.WriteStartArray("spans");
        foreach (var span in trace.Spans)
        {
            w.WriteStartObject();
            w.WriteString("spanID", span.SpanId);
            w.WriteString("name", span.Name);
            w.WriteString("startTimeUnixNano", TempoTraceEncoder.UnixNanos(span.StartTime).ToString(CultureInfo.InvariantCulture));
            w.WriteString("durationNanos", span.DurationNano.ToString(CultureInfo.InvariantCulture));
            if (span.Attributes.Count > 0)
            {
                w.WriteStartArray("attributes");
                foreach (var attribute in span.Attributes)
                {
                    w.WriteStartObject();
                    w.WriteString("key", attribute.Key);
                    w.WriteStartObject("value");
                    if (attribute.Number is { } number)
                    {
                        // An int64 is a string in protobuf JSON, which is what Grafana's Traces Drilldown parses.
                        w.WriteString("intValue", number.ToString(CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        TempoTypedValue.WriteMember(w, attribute.Text ?? string.Empty, attribute.Infer);
                    }

                    w.WriteEndObject();
                    w.WriteEndObject();
                }

                w.WriteEndArray();
            }

            w.WriteEndObject();
        }

        w.WriteEndArray();
        w.WriteNumber("matched", trace.Matched);
        w.WriteEndObject();
    }

    private static void WriteAttribute(Utf8JsonWriter w, string key, string value, bool infer = false)
    {
        w.WriteStartObject();
        w.WriteString("key", key);
        w.WriteStartObject("value");
        TempoTypedValue.WriteMember(w, value, infer);
        w.WriteEndObject();
        w.WriteEndObject();
    }

    private static async Task<IResult> HandleTagsAsync(HttpContext http, ISpanQueryService spans, TimeProvider time, bool v2, CancellationToken ct)
    {
        var query = http.Request.Query;
        try
        {
            var scope = query["scope"].ToString();
            if (scope is not ("" or "none" or "span" or "resource" or "intrinsic"))
            {
                return Error(StatusCodes.Status400BadRequest, $"unknown scope '{scope}'; use span, resource or intrinsic.");
            }

            var (from, to) = Window(query);
            var limit = ParseLimit(query["limit"], DefaultTagLimit, SpanAttributeKeysQueryBuilder.MaxLimit);
            var now = time.GetUtcNow();
            var window = (From: from ?? now - SpanFilterSqlBuilder.DefaultLookback, To: to ?? now);

            var wantSpan = scope is "" or "none" or "span";
            var wantResource = scope is "" or "none" or "resource";
            var spanTags = wantSpan ? await spans.GetAttributeKeysAsync(new SpanAttributeKeysRequest(SpanAttributeBag.Span, window.From, window.To, limit), ct) : [];
            var resourceTags = wantResource ? await spans.GetAttributeKeysAsync(new SpanAttributeKeysRequest(SpanAttributeBag.Resource, window.From, window.To, limit), ct) : [];

            return Json(w =>
            {
                w.WriteStartObject();
                if (v2)
                {
                    w.WriteStartArray("scopes");
                    if (wantSpan)
                    {
                        WriteScope(w, "span", spanTags);
                    }

                    if (wantResource)
                    {
                        WriteScope(w, "resource", resourceTags);
                    }

                    if (scope is "" or "none" or "intrinsic")
                    {
                        WriteScope(w, "intrinsic", Intrinsics);
                    }

                    w.WriteEndArray();
                }
                else
                {
                    w.WriteStartArray("tagNames");
                    foreach (var tag in spanTags.Concat(resourceTags).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
                    {
                        w.WriteStringValue(tag);
                    }

                    w.WriteEndArray();
                }

                w.WriteEndObject();
            });
        }
        catch (ArgumentException ex)
        {
            return Error(StatusCodes.Status400BadRequest, ex.Message);
        }
    }

    private static void WriteScope(System.Text.Json.Utf8JsonWriter w, string name, IEnumerable<string> tags)
    {
        w.WriteStartObject();
        w.WriteString("name", name);
        w.WriteStartArray("tags");
        foreach (var tag in tags.Order(StringComparer.Ordinal))
        {
            w.WriteStringValue(tag);
        }

        w.WriteEndArray();
        w.WriteEndObject();
    }

    private static async Task<IResult> HandleTagValuesAsync(string tag, HttpContext http, ISpanQueryService spans, bool v2, CancellationToken ct)
    {
        var query = http.Request.Query;
        try
        {
            var (from, to) = Window(query);
            var limit = ParseLimit(query["limit"], DefaultTagLimit, 1000);
            var values = new List<(string Type, string Value)>();

            var field = TempoSearchPlanner.TagField(tag);
            var request = TagValuesRequest(field, new SpanFilter { From = from, To = to }, limit);
            if (request is not null)
            {
                var response = await spans.GetAttributeValuesAsync(request.Value.Request, ct);
                values.AddRange(response.Values
                    .Select(v => request.Value.Map(v.Value))
                    .Where(v => v.Length > 0)
                    .Select(v => (request.Value.Type, v)));
            }

            return Json(w =>
            {
                w.WriteStartObject();
                w.WriteStartArray("tagValues");
                foreach (var (type, value) in values)
                {
                    if (v2)
                    {
                        w.WriteStartObject();
                        w.WriteString("type", type);
                        w.WriteString("value", value);
                        w.WriteEndObject();
                    }
                    else
                    {
                        w.WriteStringValue(value);
                    }
                }

                w.WriteEndArray();
                w.WriteEndObject();
            });
        }
        catch (ArgumentException ex)
        {
            return Error(StatusCodes.Status400BadRequest, ex.Message);
        }
    }

    /// <summary>
    /// The value query for one tag, with how to present each value; null for a tag that has no value
    /// list here (durations).
    /// </summary>
    private static (SpanAttributeValuesRequest Request, string Type, Func<string, string> Map)? TagValuesRequest(string field, SpanFilter filter, int limit)
    {
        SpanAttributeValuesRequest For(SpanValuesField values, SpanFilter f, SpanAttributeBag bag = SpanAttributeBag.Span, string key = "") =>
            new() { Filter = f, Field = values, Bag = bag, Key = key, Limit = limit };

        var rootsOnly = filter with { RootSpansOnly = true };
        switch (field)
        {
            case "name":
                return (For(SpanValuesField.Name, filter), "string", v => v);
            case "rootName":
                return (For(SpanValuesField.Name, rootsOnly), "string", v => v);
            case "rootServiceName":
                return (For(SpanValuesField.Service, rootsOnly), "string", v => v);
            case "status":
                return (For(SpanValuesField.Status, filter), "keyword", StatusKeyword);
            case "duration" or "traceDuration":
                return null;
        }

        if (field.StartsWith("span.", StringComparison.Ordinal))
        {
            return (For(SpanValuesField.Attribute, filter, SpanAttributeBag.Span, field["span.".Length..]), "string", v => v);
        }

        var key = field.StartsWith("resource.", StringComparison.Ordinal) ? field["resource.".Length..] : field[1..];
        if (key == "service.name")
        {
            return (For(SpanValuesField.Service, filter), "string", v => v);
        }

        var bag = field.StartsWith("resource.", StringComparison.Ordinal) || TempoSearchPlanner.ResourceNamespaces.Any(p => key.StartsWith(p, StringComparison.Ordinal))
            ? SpanAttributeBag.Resource
            : SpanAttributeBag.Span;
        return (For(SpanValuesField.Attribute, filter, bag, key), "string", v => v);
    }

    private static string StatusKeyword(string label) => label switch
    {
        "STATUS_CODE_ERROR" => "error",
        "STATUS_CODE_OK" => "ok",
        "STATUS_CODE_UNSET" => "unset",
        _ => "",
    };

    /// <summary>Tempo's ids are hex of any length up to 32, left-padded; Flare stores 32 lower-case characters.</summary>
    internal static string? NormalizeTraceId(string text)
    {
        if (text.Length is 0 or > 32 || !text.All(char.IsAsciiHexDigit))
        {
            return null;
        }

        return text.PadLeft(32, '0').ToLowerInvariant();
    }

    private static bool WantsProtobuf(HttpContext http) =>
        http.Request.Headers.Accept.Any(a => a is not null && (a.Contains("application/protobuf", StringComparison.OrdinalIgnoreCase)
            || a.Contains("application/x-protobuf", StringComparison.OrdinalIgnoreCase)));

    /// <summary><c>start</c>/<c>end</c> are unix seconds; either may be absent (the query builder then looks back an hour from now).</summary>
    private static (DateTimeOffset? From, DateTimeOffset? To) Window(IQueryCollection query)
    {
        var from = UnixSeconds(query["start"], "start");
        var to = UnixSeconds(query["end"], "end");
        if (from is { } f && to is { } t && t <= f)
        {
            throw new ArgumentException("'end' must be after 'start'.");
        }

        return (from, to);
    }

    private static DateTimeOffset? UnixSeconds(string? text, string name)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        return long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) && seconds < 100_000_000_000
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : throw new ArgumentException($"invalid '{name}' '{text}': expected unix seconds.");
    }

    private static int ParseLimit(string? text, int defaultLimit, int maxLimit)
    {
        if (string.IsNullOrEmpty(text))
        {
            return defaultLimit;
        }

        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var limit) && limit > 0
            ? Math.Min(limit, maxLimit)
            : throw new ArgumentException($"invalid limit '{text}'.");
    }

    private static IResult Error(int status, string message) => Results.Text(message, "text/plain", statusCode: status);

    private static IResult Json(Action<Utf8JsonWriter> body)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            body(writer);
        }

        return Results.Bytes(stream.ToArray(), "application/json");
    }
}
