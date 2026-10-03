using System.ComponentModel;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Flare.Mcp;

/// <summary>
/// Read-only MCP tools exposed by `flare mcp`. Each returns compact plain text (not raw
/// JSON) with a hard row cap, because every byte lands in the calling model's context.
/// </summary>
[McpServerToolType]
internal sealed class FlareMcpTools(FlareApiClient api)
{
    private const int MaxLimit = 100;
    private const int MaxBodyChars = 300;

    [McpServerTool(Name = "search_logs", ReadOnly = true)]
    [Description("Search Flare log events, newest first. Returns at most `limit` rows (max 100) as one line each: time, level, service, trace id, message. Use trace_id from a result to pivot to the same request's other logs.")]
    public async Task<string> SearchLogs(
        [Description("Exact service names to include. Omit for all services.")] string[]? services = null,
        [Description("Severity buckets: trace, debug, info, warn, error, fatal. Omit for all.")] string[]? levels = null,
        [Description("Case-insensitive substring match against the log body.")] string? search = null,
        [Description("Exact lower-hex trace id.")] string? traceId = null,
        [Description("Log attribute equals filters, each formatted key=value.")] string[]? attributes = null,
        [Description("How far back to look: e.g. 15m, 1h, 6h, 24h, 7d. Default 1h.")] string since = "1h",
        [Description("Max rows to return, 1-100. Default 20.")] int limit = 20,
        [Description("Scope to telemetry since the service(s) last started (a new process start, detected via service.instance.id) instead of `since`. Requires `services`.")] bool lastRun = false,
        CancellationToken cancellationToken = default)
    {
        if (NotReady() is { } notReady)
        {
            throw new McpException(notReady);
        }

        var severityNumbers = new List<byte>();
        foreach (var level in levels ?? [])
        {
            if (!SeverityLevels.TryExpand(level, out var numbers))
            {
                throw new McpException($"Unknown level '{level}' - expected one of: trace, debug, info, warn, error, fatal.");
            }

            severityNumbers.AddRange(numbers);
        }

        var (from, rangeError) = await ResolveFromAsync(since, lastRun, services, cancellationToken);
        if (rangeError is not null)
        {
            throw new McpException(rangeError);
        }

        if (!AttributeFlagParsing.TryParse(attributes ?? [], [], [], [], out var parsedAttrs, out var attrError))
        {
            throw new McpException(attrError);
        }

        var to = DateTimeOffset.UtcNow;
        var pageSize = Math.Clamp(limit, 1, MaxLimit);
        var filter = new LogFilterWire
        {
            From = from,
            To = to,
            Services = services is { Length: > 0 } ? services : null,
            SeverityNumbers = severityNumbers.Count > 0 ? severityNumbers : null,
            TraceId = string.IsNullOrWhiteSpace(traceId) ? null : traceId,
            Search = string.IsNullOrWhiteSpace(search) ? null : search,
            Attributes = parsedAttrs.Count > 0
                ? parsedAttrs.Select(a => new AttributeFilterWire { Key = a.Key, Value = a.Value, Operator = a.Operator }).ToList()
                : null,
        };

        var (response, error) = await PostAsync<LogSearchResponseWire>(
            "/api/logs/search", new LogSearchRequestWire { Filter = filter, PageSize = pageSize }, cancellationToken);
        if (error is not null)
        {
            throw new McpException(error);
        }

        var events = response?.Events ?? [];
        if (events.Count == 0)
        {
            return "No log events match these filters.";
        }

        var sb = new StringBuilder();
        foreach (var e in events.Take(pageSize))
        {
            var body = e.Body.ReplaceLineEndings(" ");
            if (body.Length > MaxBodyChars)
            {
                body = string.Concat(body.AsSpan(0, MaxBodyChars), "…");
            }

            sb.Append(e.Timestamp.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'"))
              .Append(' ').Append(SeverityLevels.LabelFor(e.SeverityNumber))
              .Append(' ').Append(e.ServiceName)
              .Append(" trace=").Append(string.IsNullOrEmpty(e.TraceId) ? "-" : e.TraceId)
              .Append(" | ").AppendLine(body);
        }

        if (response?.NextCursor is not null)
        {
            sb.AppendLine($"(more results exist - narrow the filters or time range; showing {Math.Min(events.Count, pageSize)})");
        }

        return sb.ToString();
    }

    [McpServerTool(Name = "search_traces", ReadOnly = true)]
    [Description("List recent traces (root spans), newest first, one line each: start time, service, root span name, duration, span count, status, trace id. Pass a trace id to get_trace for the full span tree.")]
    public async Task<string> SearchTraces(
        [Description("Exact service names to include. Omit for all.")] string[]? services = null,
        [Description("Only traces containing an error.")] bool errorsOnly = false,
        [Description("Only traces slower than this many milliseconds.")] int? minDurationMs = null,
        [Description("How far back to look: e.g. 15m, 1h, 6h, 24h, 7d. Default 1h.")] string since = "1h",
        [Description("Max traces, 1-100. Default 20.")] int limit = 20,
        [Description("Scope to telemetry since the service(s) last started (a new process start, detected via service.instance.id) instead of `since`. Requires `services`.")] bool lastRun = false,
        CancellationToken cancellationToken = default)
    {
        if (NotReady() is { } notReady)
        {
            throw new McpException(notReady);
        }

        var (from, rangeError) = await ResolveFromAsync(since, lastRun, services, cancellationToken);
        if (rangeError is not null)
        {
            throw new McpException(rangeError);
        }

        var to = DateTimeOffset.UtcNow;
        var pageSize = Math.Clamp(limit, 1, MaxLimit);
        var request = new SpanSearchRequestWire
        {
            PageSize = pageSize,
            Filter = new SpanFilterWire
            {
                From = from,
                To = to,
                RootSpansOnly = true,
                Services = services is { Length: > 0 } ? services : null,
                StatusCodes = errorsOnly ? ["STATUS_CODE_ERROR"] : null,
                MinDurationNano = minDurationMs is > 0 ? (ulong)minDurationMs.Value * 1_000_000UL : null,
            },
        };

        var (response, error) = await PostAsync<SpanSearchResponseWire>("/api/spans/search", request, cancellationToken);
        if (error is not null)
        {
            throw new McpException(error);
        }

        var spans = response?.Spans ?? [];
        if (spans.Count == 0)
        {
            return "No traces match these filters.";
        }

        var sb = new StringBuilder();
        foreach (var s in spans.Take(pageSize))
        {
            var failed = s.HasError == true || s.StatusCode == "STATUS_CODE_ERROR";
            sb.Append(s.StartTime.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"))
              .Append(' ').Append(s.ServiceName)
              .Append(' ').Append(s.Name)
              .Append(' ').Append(WireFormat.FormatDurationNano(s.DurationNano))
              .Append(' ').Append(s.SpanCount ?? 1).Append(" spans")
              .Append(failed ? " ERROR" : " ok")
              .Append(" trace=").AppendLine(s.TraceId);
        }

        return sb.ToString();
    }

    [McpServerTool(Name = "get_trace", ReadOnly = true)]
    [Description("Get one trace as an indented span tree (parents before children): service, span name, offset from trace start, duration, status. Capped at 200 spans; error spans are marked.")]
    public async Task<string> GetTrace(
        [Description("Lower-hex trace id, e.g. from search_logs or search_traces.")] string traceId,
        CancellationToken cancellationToken = default)
    {
        if (NotReady() is { } notReady)
        {
            throw new McpException(notReady);
        }

        TraceDtoWire? trace;
        try
        {
            using var http = await api.Http.GetAsync($"/api/traces/{Uri.EscapeDataString(traceId)}", cancellationToken);
            if (http.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return $"No spans found for trace {traceId}.";
            }

            if (!http.IsSuccessStatusCode)
            {
                throw new McpException(StatusText($"GET /api/traces/{traceId}", http));
            }

            trace = await http.Content.ReadFromJsonAsync<TraceDtoWire>(WireJsonOptions.Instance, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            throw new McpException(FailureMessage(ex));
        }

        var spans = trace?.Spans ?? [];
        if (spans.Count == 0)
        {
            return $"No spans found for trace {traceId}.";
        }

        var start = spans.Min(x => x.StartTime);
        var ids = new HashSet<string>(spans.Select(x => x.SpanId));
        var children = spans
            .GroupBy(x => !string.IsNullOrEmpty(x.ParentSpanId) && ids.Contains(x.ParentSpanId) ? x.ParentSpanId : "")
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.StartTime).ToList());

        var errors = spans.Count(x => x.StatusCode == "STATUS_CODE_ERROR");
        var sb = new StringBuilder();
        sb.AppendLine($"trace {trace!.TraceId}: {spans.Count} spans, {spans.Select(x => x.ServiceName).Distinct().Count()} services, {errors} errors, total {WireFormat.FormatDurationNano((ulong)(spans.Max(x => x.EndTime) - start).TotalMilliseconds * 1_000_000UL)}");

        var visited = new HashSet<string>();
        var printed = 0;
        void Walk(string parent, int depth)
        {
            if (!children.TryGetValue(parent, out var kids))
            {
                return;
            }

            foreach (var k in kids)
            {
                if (printed >= MaxSpans || !visited.Add(k.SpanId))
                {
                    continue;
                }

                printed++;
                sb.Append(' ', depth * 2)
                  .Append(k.ServiceName).Append(' ').Append(k.Name)
                  .Append(" +").Append((long)(k.StartTime - start).TotalMilliseconds).Append("ms ")
                  .Append(WireFormat.FormatDurationNano(k.DurationNano))
                  .AppendLine(k.StatusCode == "STATUS_CODE_ERROR" ? " ERROR" : "");
                Walk(k.SpanId, depth + 1);
            }
        }

        Walk("", 0);
        if (printed < spans.Count || trace.Truncated)
        {
            sb.AppendLine($"(showing {printed} of {spans.Count}{(trace.Truncated ? "+, server-truncated" : "")} spans)");
        }

        return sb.ToString();
    }

    [McpServerTool(Name = "list_metrics", ReadOnly = true)]
    [Description("List metric names seen recently: name, type (Gauge/Sum/Histogram), unit, emitting service. Use before query_metric to find the exact name.")]
    public async Task<string> ListMetrics(
        [Description("Exact service names to include. Omit for all.")] string[]? services = null,
        [Description("Substring filter on the metric name (case-insensitive).")] string? nameContains = null,
        [Description("How far back to look. Default 1h.")] string since = "1h",
        [Description("Max rows, 1-100. Default 50.")] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        if (NotReady() is { } notReady)
        {
            throw new McpException(notReady);
        }

        if (!WireFormat.TryParseSince(since, out var span))
        {
            throw new McpException($"Couldn't parse since '{since}' - expected e.g. 15m, 1h, 6h, 24h, 7d.");
        }

        var to = DateTimeOffset.UtcNow;
        var (response, error) = await PostAsync<MetricNamesResponseWire>(
            "/api/metrics/names",
            new MetricNamesRequestWire { From = to - span, To = to, Services = services is { Length: > 0 } ? services : null },
            cancellationToken,
            MetricsWireJsonOptions.Instance);
        if (error is not null)
        {
            throw new McpException(error);
        }

        var metrics = (response?.Metrics ?? [])
            .Where(m => string.IsNullOrWhiteSpace(nameContains) || m.MetricName.Contains(nameContains, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (metrics.Count == 0)
        {
            return "No metrics match.";
        }

        var cap = Math.Clamp(limit, 1, MaxLimit);
        var sb = new StringBuilder();
        foreach (var m in metrics.Take(cap))
        {
            sb.AppendLine($"{m.MetricName} [{m.Type}{(string.IsNullOrEmpty(m.Unit) ? "" : ", " + m.Unit)}] service={m.ServiceName} series={m.SeriesCount}");
        }

        if (metrics.Count > cap)
        {
            sb.AppendLine($"(+{metrics.Count - cap} more - use nameContains/services to narrow)");
        }

        return sb.ToString();
    }

    [McpServerTool(Name = "query_metric", ReadOnly = true)]
    [Description("Summarize one metric over a time range: per series (max 10), the first/last/min/avg/max of its value, or for histograms the latest and max p50/p95/p99. Not raw points - use list_metrics first to get the exact metric name.")]
    public async Task<string> QueryMetric(
        [Description("Exact metric name from list_metrics.")] string metricName,
        [Description("Service that emits it. Required only when more than one service emits this metric.")] string? service = null,
        [Description("Attribute key to split series by.")] string? groupBy = null,
        [Description("How far back to look. Default 1h.")] string since = "1h",
        CancellationToken cancellationToken = default)
    {
        if (NotReady() is { } notReady)
        {
            throw new McpException(notReady);
        }

        if (!WireFormat.TryParseSince(since, out var span))
        {
            throw new McpException($"Couldn't parse since '{since}' - expected e.g. 15m, 1h, 6h, 24h, 7d.");
        }

        var to = DateTimeOffset.UtcNow;
        var from = to - span;
        var (names, namesError) = await PostAsync<MetricNamesResponseWire>(
            "/api/metrics/names",
            new MetricNamesRequestWire { From = from, To = to, Services = service is null ? null : [service] },
            cancellationToken,
            MetricsWireJsonOptions.Instance);
        if (namesError is not null)
        {
            throw new McpException(namesError);
        }

        var candidates = (names?.Metrics ?? []).Where(m => m.MetricName == metricName).ToList();
        if (candidates.Count == 0)
        {
            return $"No metric named '{metricName}' in the last {since}. Try list_metrics.";
        }

        if (candidates.Count > 1)
        {
            return $"'{metricName}' is emitted by several services - pass service= one of: {string.Join(", ", candidates.Select(c => c.ServiceName).Distinct())}";
        }

        var metric = candidates[0];
        var bucketSeconds = Math.Max(60, (int)(span.TotalSeconds / 60));
        var (result, error) = await PostAsync<MetricQueryResponseWire>(
            "/api/metrics/query",
            new MetricQueryRequestWire
            {
                MetricName = metric.MetricName,
                Type = metric.Type,
                Filter = new MetricFilterWire { From = from, To = to, Services = [metric.ServiceName] },
                BucketWidthSeconds = bucketSeconds,
                GroupByAttributeKey = string.IsNullOrWhiteSpace(groupBy) ? null : groupBy,
            },
            cancellationToken,
            MetricsWireJsonOptions.Instance);
        if (error is not null)
        {
            throw new McpException(error);
        }

        var series = result?.Series ?? [];
        if (series.Count == 0)
        {
            return "No data in range.";
        }

        var histogram = metric.Type is "Histogram" or "ExponentialHistogram" && !(result?.TreatedAsCounter ?? false);
        var sb = new StringBuilder();
        sb.AppendLine($"{metric.MetricName} [{metric.Type}{(string.IsNullOrEmpty(metric.Unit) ? "" : ", " + metric.Unit)}] last {since}, {bucketSeconds}s buckets, {series.Count} series");
        foreach (var s in series.Take(MaxSeries))
        {
            var label = s.Attributes.Count > 0 ? " {" + string.Join(", ", s.Attributes.Select(a => $"{a.Key}={a.Value}")) + "}" : "";
            sb.Append(s.ServiceName).Append(label).Append(": ");
            if (histogram)
            {
                var pts = s.Points;
                sb.AppendLine(pts.Count == 0
                    ? "no points"
                    : $"latest p50={Fmt(pts[^1].P50)} p95={Fmt(pts[^1].P95)} p99={Fmt(pts[^1].P99)}; max p95={Fmt(pts.Max(p => p.P95))} max p99={Fmt(pts.Max(p => p.P99))}; count={pts.Sum(p => p.Count ?? 0)}");
            }
            else
            {
                var vals = s.Points.Where(p => p.Value is not null).Select(p => p.Value!.Value).ToList();
                sb.AppendLine(vals.Count == 0
                    ? "no points"
                    : $"first={Fmt(vals[0])} last={Fmt(vals[^1])} min={Fmt(vals.Min())} avg={Fmt(vals.Average())} max={Fmt(vals.Max())} ({vals.Count} points)");
            }
        }

        if (series.Count > MaxSeries)
        {
            sb.AppendLine($"(+{series.Count - MaxSeries} more series - narrow with service/groupBy)");
        }

        return sb.ToString();
    }

    [McpServerTool(Name = "list_exceptions", ReadOnly = true)]
    [Description("Top exception groups (type + message) recorded on spans recently: count, first/last seen, affected services. Most frequent first.")]
    public async Task<string> ListExceptions(
        [Description("Exact service names to include. Omit for all.")] string[]? services = null,
        [Description("How far back to look. Default 1h.")] string since = "1h",
        [Description("Max groups, 1-50. Default 20.")] int limit = 20,
        [Description("Scope to telemetry since the service(s) last started (a new process start, detected via service.instance.id) instead of `since`. Requires `services`.")] bool lastRun = false,
        CancellationToken cancellationToken = default)
    {
        if (NotReady() is { } notReady)
        {
            throw new McpException(notReady);
        }

        var (from, rangeError) = await ResolveFromAsync(since, lastRun, services, cancellationToken);
        if (rangeError is not null)
        {
            throw new McpException(rangeError);
        }

        var to = DateTimeOffset.UtcNow;
        var cap = Math.Clamp(limit, 1, 50);
        var (response, error) = await PostAsync<ExceptionGroupsResponseWire>(
            "/api/errors/groups",
            new { filter = new { from = from, to, services = services is { Length: > 0 } ? services : null }, topN = cap },
            cancellationToken);
        if (error is not null)
        {
            throw new McpException(error);
        }

        var groups = response?.Groups ?? [];
        if (groups.Count == 0)
        {
            return "No exceptions recorded in this range.";
        }

        var sb = new StringBuilder();
        foreach (var g in groups.Take(cap))
        {
            var msg = g.ExceptionMessage.ReplaceLineEndings(" ");
            if (msg.Length > MaxBodyChars)
            {
                msg = string.Concat(msg.AsSpan(0, MaxBodyChars), "…");
            }

            sb.AppendLine($"{g.OccurrenceCount}x {g.ExceptionType}: {msg} | services={string.Join(",", g.AffectedServices)} first={g.FirstSeen.UtcDateTime:HH:mm:ss}Z last={g.LastSeen.UtcDateTime:HH:mm:ss}Z");
        }

        return sb.ToString();
    }

    [McpServerTool(Name = "list_firing_alerts", ReadOnly = true)]
    [Description("List alert rules that are currently firing (fired and not yet resolved), with when they last fired, what was observed vs the threshold, and the notification status. Empty when everything is healthy.")]
    public async Task<string> ListFiringAlerts(CancellationToken cancellationToken = default)
    {
        if (NotReady() is { } notReady)
        {
            throw new McpException(notReady);
        }

        AlertRuleListResponseWire? rules;
        try
        {
            using var http = await api.Http.GetAsync("/api/alerts", cancellationToken);
            if (!http.IsSuccessStatusCode)
            {
                throw new McpException(StatusText("GET /api/alerts", http));
            }

            rules = await http.Content.ReadFromJsonAsync<AlertRuleListResponseWire>(WireJsonOptions.Instance, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            throw new McpException(FailureMessage(ex));
        }

        var sb = new StringBuilder();
        var enabled = (rules?.Rules ?? []).Where(r => r.Enabled).ToList();
        foreach (var rule in enabled)
        {
            AlertHistoryResponseWire? history;
            try
            {
                using var http = await api.Http.GetAsync($"/api/alerts/{rule.Id}/history?limit=20", cancellationToken);
                if (!http.IsSuccessStatusCode)
                {
                    continue;
                }

                history = await http.Content.ReadFromJsonAsync<AlertHistoryResponseWire>(WireJsonOptions.Instance, cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException)
            {
                continue;
            }

            // History is newest-first; a rule is firing when its latest event isn't a resolution.
            var latest = history?.Events.OrderByDescending(e => e.FiredAt).FirstOrDefault();
            if (latest is null || latest.Resolved)
            {
                continue;
            }

            sb.AppendLine($"{rule.Name}: fired {latest.FiredAt.UtcDateTime:yyyy-MM-dd HH:mm:ss}Z observed={latest.ObservedValue?.ToString("G4") ?? latest.ObservedCount.ToString()} threshold={latest.ThresholdValue?.ToString("G4") ?? latest.ThresholdCount.ToString()} window={latest.WindowSeconds}s notification={latest.NotificationStatus}{(string.IsNullOrEmpty(latest.SuppressedByWindow) ? "" : " (suppressed by maintenance window)")}");
        }

        return sb.Length == 0 ? $"No alerts firing ({enabled.Count} enabled rules checked)." : sb.ToString();
    }

    private async Task<(DateTimeOffset From, string? Error)> ResolveFromAsync(string since, bool lastRun, string[]? services, CancellationToken cancellationToken)
    {
        if (!lastRun)
        {
            return WireFormat.TryParseSince(since, out var span)
                ? (DateTimeOffset.UtcNow - span, null)
                : throw new McpException($"Couldn't parse since '{since}' - expected e.g. 15m, 1h, 6h, 24h, 7d.");
        }

        if (services is not { Length: > 0 })
        {
            throw new McpException("lastRun needs `services` (which service's last start to scope to).");
        }

        var starts = new List<DateTimeOffset>();
        foreach (var service in services)
        {
            try
            {
                var runs = await new RunLocator(api).GetRunStartsAsync(service, cancellationToken);
                if (runs.Count == 0)
                {
                    throw new McpException($"Couldn't determine a last run for '{service}': it has no spans with a service.instance.id in the last 7 days. Use `since` instead.");
                }

                starts.Add(runs[0]);
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException)
            {
                throw new McpException(FailureMessage(ex));
            }
        }

        return (starts.Min() - RunBoundaries.QueryMargin, null);
    }

    [McpServerTool(Name = "list_runs", ReadOnly = true)]
    [Description("Show when a service was last (re)started, newest first - each run is one process start (replicas started within a minute count as one). Use these timestamps to reason about before/after a restart; lastRun=true on the search tools applies the newest one automatically.")]
    public async Task<string> ListRuns(
        [Description("Exact service name.")] string service,
        CancellationToken cancellationToken = default)
    {
        if (NotReady() is { } notReady)
        {
            throw new McpException(notReady);
        }

        try
        {
            var runs = await new RunLocator(api).GetRunStartsAsync(service, cancellationToken);
            return runs.Count == 0
                ? $"No runs found for '{service}': no spans with a service.instance.id in the last 7 days."
                : string.Join('\n', runs.Take(10).Select((r, i) => $"{(i == 0 ? "latest" : $"previous-{i}")}: started {r.UtcDateTime:yyyy-MM-dd HH:mm:ss}Z"));
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            throw new McpException(FailureMessage(ex));
        }
    }

    [McpServerTool(Name = "diff_traces", ReadOnly = true)]
    [Description("Compare two traces structurally: spans added/removed (matched by service+name), mean duration changes above 20% and 5ms, and per-span error count changes. Baseline is the 'before' trace, candidate the 'after'.")]
    public async Task<string> DiffTraces(
        [Description("Trace id of the 'before' trace.")] string baselineTraceId,
        [Description("Trace id of the 'after' trace.")] string candidateTraceId,
        CancellationToken cancellationToken = default)
    {
        if (NotReady() is { } notReady)
        {
            throw new McpException(notReady);
        }

        try
        {
            var baseline = await FetchTraceAsync(baselineTraceId, cancellationToken);
            var candidate = await FetchTraceAsync(candidateTraceId, cancellationToken);
            if (baseline is null || candidate is null)
            {
                return $"Trace not found: {(baseline is null ? baselineTraceId : candidateTraceId)}";
            }

            return TraceDiff.Render(baseline.Spans, candidate.Spans);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            throw new McpException(FailureMessage(ex));
        }
    }

    [McpServerTool(Name = "compare_runs", ReadOnly = true)]
    [Description("Before/after diff of one endpoint across the service's two most recent runs: takes the latest trace whose root span is `operation` from the previous run and from the latest run (see list_runs), then diffs them like diff_traces. Use after restarting the app with a fix to see what changed.")]
    public async Task<string> CompareRuns(
        [Description("Exact service name.")] string service,
        [Description("Exact root span name of the endpoint, e.g. 'POST /checkout'.")] string operation,
        CancellationToken cancellationToken = default)
    {
        if (NotReady() is { } notReady)
        {
            throw new McpException(notReady);
        }

        try
        {
            var runs = await new RunLocator(api).GetRunStartsAsync(service, cancellationToken);
            if (runs.Count < 2)
            {
                return runs.Count == 0
                    ? $"No runs found for '{service}': no spans with a service.instance.id in the last 7 days."
                    : $"Only one run of '{service}' is on record (started {runs[0].UtcDateTime:HH:mm:ss}Z); need a previous run to compare against.";
            }

            var now = DateTimeOffset.UtcNow;
            var current = await LatestRootAsync(service, operation, runs[0] - RunBoundaries.QueryMargin, now, cancellationToken);
            var previous = await LatestRootAsync(service, operation, runs[1] - RunBoundaries.QueryMargin, runs[0] - RunBoundaries.QueryMargin, cancellationToken);
            if (current is null || previous is null)
            {
                return $"No '{operation}' trace in the {(current is null ? "latest" : "previous")} run of '{service}'. Exercise the endpoint in both runs first.";
            }

            var baseline = await FetchTraceAsync(previous.TraceId, cancellationToken);
            var candidate = await FetchTraceAsync(current.TraceId, cancellationToken);
            if (baseline is null || candidate is null)
            {
                return "Couldn't load one of the traces (it may have been removed).";
            }

            return $"previous run (started {runs[1].UtcDateTime:HH:mm:ss}Z) trace={previous.TraceId}\nlatest run (started {runs[0].UtcDateTime:HH:mm:ss}Z) trace={current.TraceId}\n"
                + TraceDiff.Render(baseline.Spans, candidate.Spans);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            throw new McpException(FailureMessage(ex));
        }
    }

    private async Task<TraceDtoWire?> FetchTraceAsync(string traceId, CancellationToken cancellationToken)
    {
        using var http = await api.Http.GetAsync($"/api/traces/{Uri.EscapeDataString(traceId)}", cancellationToken);
        return http.IsSuccessStatusCode
            ? await http.Content.ReadFromJsonAsync<TraceDtoWire>(WireJsonOptions.Instance, cancellationToken)
            : null;
    }

    private async Task<SpanDtoWire?> LatestRootAsync(string service, string operation, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        // Names narrows server-side, but an API older than the facet sidebar ignores it, so the
        // name is re-checked here over a page of the newest roots - otherwise compare_runs would
        // silently diff whichever endpoint happened to be newest.
        using var http = await api.Http.PostAsJsonAsync(
            "/api/spans/search",
            new SpanSearchRequestWire
            {
                PageSize = 100,
                Filter = new SpanFilterWire { From = from, To = to, Services = [service], Names = [operation], RootSpansOnly = true },
            },
            WireJsonOptions.Instance,
            cancellationToken);
        http.EnsureSuccessStatusCode();
        return (await http.Content.ReadFromJsonAsync<SpanSearchResponseWire>(WireJsonOptions.Instance, cancellationToken))?
            .Spans.FirstOrDefault(s => s.Name == operation);
    }

    private const int MaxSpans = 200;
    private const int MaxSeries = 10;

    private static string Fmt(double? v) => v is null ? "-" : v.Value.ToString("G4", System.Globalization.CultureInfo.InvariantCulture);

    private string? NotReady() =>
        api.NotReadyMessage;

    private string FailureMessage(Exception ex) => ex switch
    {
        HttpRequestException { StatusCode: { } code } => StatusText($"Flare API request", (int)code, code.ToString()),
        _ => $"Couldn't reach the Flare API at {api.Http.BaseAddress} - is the stack running? (`flare status`)",
    };

    private static string StatusText(string what, HttpResponseMessage response) =>
        StatusText(what, (int)response.StatusCode, response.ReasonPhrase ?? response.StatusCode.ToString());

    private static string StatusText(string what, int code, string reason) => code switch
    {
        401 => $"{what} failed: 401 Unauthorized - this Flare requires sign-in. Supply a personal access token (flr_pat_...) as a Bearer token (`flare mcp`: --token or FLARE_API_TOKEN).",
        403 => $"{what} failed: 403 Forbidden - the token's user lacks permission for this call.",
        _ => $"{what} failed: {code} {reason}",
    };

    private async Task<(T? Value, string? Error)> PostAsync<T>(string path, object body, CancellationToken cancellationToken, JsonSerializerOptions? options = null)
        where T : class
    {
        options ??= WireJsonOptions.Instance;
        try
        {
            using var http = await api.Http.PostAsJsonAsync(path, body, options, cancellationToken);
            if (!http.IsSuccessStatusCode)
            {
                return (null, StatusText($"POST {path}", http));
            }

            return (await http.Content.ReadFromJsonAsync<T>(options, cancellationToken), null);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            return (null, FailureMessage(ex));
        }
    }
}

// Hand-mirrors of Flare.Api's ErrorModels.cs / AlertHistoryEntry - only the fields the MCP tools read.
internal sealed class ExceptionGroupWire
{
    public string ExceptionType { get; init; } = "";

    public string ExceptionMessage { get; init; } = "";

    public ulong OccurrenceCount { get; init; }

    public DateTimeOffset FirstSeen { get; init; }

    public DateTimeOffset LastSeen { get; init; }

    public List<string> AffectedServices { get; init; } = [];
}

internal sealed class ExceptionGroupsResponseWire
{
    public List<ExceptionGroupWire> Groups { get; init; } = [];
}

internal sealed class AlertHistoryEntryWire
{
    public DateTimeOffset FiredAt { get; init; }

    public ulong ObservedCount { get; init; }

    public ulong ThresholdCount { get; init; }

    public double? ObservedValue { get; init; }

    public double? ThresholdValue { get; init; }

    public int WindowSeconds { get; init; }

    public string NotificationStatus { get; init; } = "";

    public string SuppressedByWindow { get; init; } = "";

    public bool Resolved { get; init; }
}

internal sealed class AlertHistoryResponseWire
{
    public List<AlertHistoryEntryWire> Events { get; init; } = [];
}
