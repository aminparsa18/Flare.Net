using System.ComponentModel;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Flare.Cli.Internal;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Flare.Cli.Commands;

/// <summary>
/// Dumps a time range of log events to NDJSON/CSV via Flare.Api's <c>POST /api/logs/search</c>
/// - a support-bundle-for-a-bug-report command, and the CLI-side counterpart to the
/// dashboard's own Logs Explorer "Export" dialog (<c>src/dashboard/src/lib/logs/export.ts</c>).
/// Drives the same cursor-pagination loop that dialog's <c>fetchAllForExport</c> does
/// client-side, but streams each page straight to the output as it arrives instead of
/// buffering the whole result set in memory first - a CLI export isn't bound by a browser
/// tab's lifetime the way the dashboard's hard <c>EXPORT_ROW_CAP</c> is, but <see cref="Settings.Limit"/>
/// still guards against an unbounded scan. Reuses <see cref="SearchCommand"/>'s
/// <see cref="LogFilterWire"/>/<see cref="LogSearchRequestWire"/>/<see cref="LogSearchResponseWire"/>/
/// <see cref="LogEventDtoWire"/> rather than duplicating them.
///
/// Also implements the multi-signal "incident bundle" mode
/// (<see cref="Settings.IncludeTrace"/>/<see cref="Settings.IncludeLogs"/>/
/// <see cref="Settings.IncludeMetrics"/>, from the roadmap's "CLI: a multi-signal
/// incident.zip export mode" item) - trace + logs + metrics zipped into one archive around
/// a <c>--trace-id</c>, instead of this command's normal logs-only NDJSON/CSV stream. See
/// <see cref="ExecuteIncidentBundleAsync"/>.
/// </summary>
internal sealed class ExportCommand : AsyncCommand<ExportCommand.Settings>
{
    private const int PageSize = 1000; // matches export.ts's EXPORT_PAGE_SIZE - the backend's own LogSearchQueryBuilder max PageSize.

    // Bound on how many distinct metrics an incident bundle's metrics.json can hold - a
    // trace touching many services (or a service emitting many metric names) could
    // otherwise turn a "bundle this one incident" command into an accidental full-fleet
    // metrics dump. Same "safety cap, not a hard ceiling" spirit as Settings.Limit.
    private const int MaxBundleMetrics = 100;

    internal sealed class Settings : InstanceSettings
    {
        [CommandOption("-s|--service <NAME>")]
        [Description("Filter by exact service name. Repeatable.")]
        public string[] Service { get; init; } = [];

        [CommandOption("-l|--level <LEVEL>")]
        [Description("Filter by severity: trace, debug, info, warn, error, fatal. Repeatable.")]
        public string[] Level { get; init; } = [];

        [CommandOption("--trace-id <ID>")]
        [Description("Exact lower-hex TraceId match.")]
        public string? TraceId { get; init; }

        [CommandOption("--span-id <ID>")]
        [Description("Exact lower-hex SpanId match.")]
        public string? SpanId { get; init; }

        [CommandOption("--pattern-id <ID>")]
        [Description("Exact Drain cluster id match.")]
        public string? PatternId { get; init; }

        [CommandOption("--search <TEXT>")]
        [Description("Case-insensitive substring match against the log body.")]
        public string? Search { get; init; }

        [CommandOption("--since <RANGE>")]
        [Description("How far back to export: 15m, 1h, 6h, 24h, 7d. Default 1h.")]
        public string Since { get; init; } = "1h";

        [CommandOption("--format <FORMAT>")]
        [Description("Output format: ndjson (default) or csv.")]
        public string Format { get; init; } = "ndjson";

        [CommandOption("-o|--output <PATH>")]
        [Description("File to write to. Omit to stream to stdout.")]
        public string? Output { get; init; }

        [CommandOption("--limit <COUNT>")]
        [Description("Safety cap on total rows exported. Default 100000.")]
        public int Limit { get; init; } = 100_000;

        [CommandOption("--include-trace")]
        [Description("Incident bundle mode: include the full trace waterfall as trace.json. Requires --trace-id; writes a zip to -o instead of streaming logs.")]
        public bool IncludeTrace { get; init; }

        [CommandOption("--include-logs")]
        [Description("Incident bundle mode: include correlated log events as logs.ndjson (always NDJSON inside the archive, regardless of --format). Requires --trace-id.")]
        public bool IncludeLogs { get; init; }

        [CommandOption("--include-metrics")]
        [Description("Incident bundle mode: include metrics.json for every service seen in the trace. Requires --trace-id.")]
        public bool IncludeMetrics { get; init; }

        [CommandOption("--margin <RANGE>")]
        [Description("Incident bundle mode: padding added before/after the trace's own span window when scoping --include-logs/--include-metrics. Default 5m.")]
        public string Margin { get; init; } = "5m";
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var instance = FlareHome.ResolveTarget(settings.InstanceName);

        if (!instance.IsInitialized)
        {
            AnsiConsole.MarkupLine($"[grey]Not initialized yet - run `{instance.StartHint}` first.[/]");
            return 1;
        }

        var format = settings.Format.Trim().ToLowerInvariant();
        if (format is not ("ndjson" or "csv"))
        {
            AnsiConsole.MarkupLine($"[red]✗[/] Unknown --format '{Markup.Escape(settings.Format)}' - expected ndjson or csv.");
            return 1;
        }

        var severityNumbers = new List<byte>();
        foreach (var level in settings.Level)
        {
            if (!SeverityLevels.TryExpand(level, out var numbers))
            {
                AnsiConsole.MarkupLine($"[red]✗[/] Unknown level '{Markup.Escape(level)}' - expected one of: trace, debug, info, warn, error, fatal.");
                return 1;
            }

            severityNumbers.AddRange(numbers);
        }

        if (!TracesCommand.TryParseSince(settings.Since, out var since))
        {
            AnsiConsole.MarkupLine($"[red]✗[/] Couldn't parse --since '{Markup.Escape(settings.Since)}' - expected e.g. 15m, 1h, 6h, 24h, 7d.");
            return 1;
        }

        var to = DateTimeOffset.UtcNow;
        var from = to - since;

        var filter = new LogFilterWire
        {
            From = from,
            To = to,
            Services = settings.Service.Length > 0 ? settings.Service : null,
            SeverityNumbers = severityNumbers.Count > 0 ? severityNumbers : null,
            TraceId = string.IsNullOrWhiteSpace(settings.TraceId) ? null : settings.TraceId,
            SpanId = string.IsNullOrWhiteSpace(settings.SpanId) ? null : settings.SpanId,
            PatternId = string.IsNullOrWhiteSpace(settings.PatternId) ? null : settings.PatternId,
            Search = string.IsNullOrWhiteSpace(settings.Search) ? null : settings.Search,
        };

        if (settings.IncludeTrace || settings.IncludeLogs || settings.IncludeMetrics)
        {
            if (string.IsNullOrWhiteSpace(settings.TraceId))
            {
                AnsiConsole.MarkupLine("[red]✗[/] --include-trace/--include-logs/--include-metrics require --trace-id - an incident bundle is keyed to one trace.");
                return 1;
            }

            if (string.IsNullOrWhiteSpace(settings.Output))
            {
                AnsiConsole.MarkupLine("[red]✗[/] --include-trace/--include-logs/--include-metrics need -o|--output <PATH> - a zip archive isn't something to stream to stdout.");
                return 1;
            }

            if (!TracesCommand.TryParseSince(settings.Margin, out var margin))
            {
                AnsiConsole.MarkupLine($"[red]✗[/] Couldn't parse --margin '{Markup.Escape(settings.Margin)}' - expected e.g. 1m, 5m, 15m.");
                return 1;
            }

            return await ExecuteIncidentBundleAsync(instance, settings, filter, since, margin, cancellationToken);
        }

        var port = instance.ReadEnvValue("FLARE_API_PORT", "8080");
        using var http = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var interrupted = false;
        using var interrupt = InterruptSignal.OnInterrupt(() =>
        {
            interrupted = true;
            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        });

        // Only dispose a real file writer - Console.Out is process-lifetime-owned and
        // shouldn't be torn down here (this command isn't necessarily the last thing to
        // touch it, e.g. under a future host process or test harness). Encoding.UTF8's
        // default preamble (BOM) is deliberately NOT used here - caught live: it corrupted
        // the CSV header's first cell to "﻿EventId" for any reader that doesn't know
        // to strip it (e.g. Python's csv module without utf-8-sig), and NDJSON parsers are
        // even less forgiving of a stray BOM before the first '{'.
        var fileWriter = settings.Output is null ? null : new StreamWriter(settings.Output, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        var writer = fileWriter ?? Console.Out;

        var total = 0;
        var wroteCsvHeader = false;
        string? cursor = null;

        try
        {
            while (total < settings.Limit)
            {
                LogSearchResponseWire? response;
                try
                {
                    using var httpResponse = await http.PostAsJsonAsync(
                        "/api/logs/search",
                        new LogSearchRequestWire { Filter = filter, Cursor = cursor, PageSize = PageSize },
                        WireJsonOptions.Instance,
                        cts.Token);

                    if (!httpResponse.IsSuccessStatusCode)
                    {
                        AnsiConsole.MarkupLine($"[red]✗[/] POST /api/logs/search failed: {(int)httpResponse.StatusCode} {httpResponse.ReasonPhrase}");
                        return 1;
                    }

                    response = await httpResponse.Content.ReadFromJsonAsync<LogSearchResponseWire>(WireJsonOptions.Instance, cts.Token);
                }
                catch (Exception ex) when (ex is HttpRequestException or JsonException)
                {
                    AnsiConsole.MarkupLine($"[red]✗[/] Couldn't reach the API on localhost:{port} - is `api` running? Check `flare status`.");
                    return 1;
                }

                var events = response?.Events ?? [];
                if (events.Count == 0)
                {
                    break;
                }

                foreach (var e in events)
                {
                    if (total >= settings.Limit)
                    {
                        break;
                    }

                    if (format == "csv")
                    {
                        if (!wroteCsvHeader)
                        {
                            await writer.WriteAsync(CsvHeader);
                            wroteCsvHeader = true;
                        }

                        await WriteCsvRowAsync(writer, e);
                    }
                    else
                    {
                        await WriteNdjsonRowAsync(writer, e);
                    }

                    total++;
                }

                await Console.Error.WriteLineAsync($"Fetched {total} row(s)...");

                cursor = response?.NextCursor;
                if (cursor is null)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Ctrl+C - fall through to report what was written so far.
        }
        finally
        {
            // Runs on every exit path out of the try above, including the early `return 1`s
            // on HTTP failure - a real file writer must always be flushed and closed, not
            // left dangling on an error path.
            await writer.FlushAsync();
            if (fileWriter is not null)
            {
                await fileWriter.DisposeAsync();
            }
        }

        if (interrupted)
        {
            await Console.Error.WriteLineAsync($"Interrupted - wrote {total} row(s).");
            return 0;
        }

        await Console.Error.WriteLineAsync(total >= settings.Limit
            ? $"Wrote {total} row(s) - stopped at --limit ({settings.Limit}). Raise --limit or narrow --since to export more."
            : $"Wrote {total} row(s).");

        return 0;
    }

    private const string CsvHeader = "EventId,Timestamp,Severity,SeverityNumber,Service,EventName,Message,TraceId,SpanId,LogAttributesJson\r\n";

    private static async Task WriteCsvRowAsync(TextWriter writer, LogEventDtoWire e)
    {
        var attributesJson = JsonSerializer.Serialize(e.LogAttributes, WireJsonOptions.Instance);
        var fields = new[]
        {
            e.EventId.ToString(),
            e.Timestamp.ToString("O"),
            e.SeverityText,
            e.SeverityNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
            e.ServiceName,
            e.EventName,
            e.Body,
            e.TraceId,
            e.SpanId,
            attributesJson,
        };

        await writer.WriteAsync(string.Join(',', fields.Select(CsvEscape)));
        await writer.WriteAsync("\r\n");
    }

    // Mirrors dashboard/src/lib/logs/export.ts's csvEscape exactly - quote on comma/quote/
    // newline, double internal quotes.
    private static string CsvEscape(string field)
    {
        if (field.IndexOfAny([',', '"', '\r', '\n']) < 0)
        {
            return field;
        }

        return $"\"{field.Replace("\"", "\"\"")}\"";
    }

    private static async Task WriteNdjsonRowAsync(TextWriter writer, LogEventDtoWire e)
    {
        var row = new ExportEventWire
        {
            EventId = e.EventId,
            Timestamp = e.Timestamp,
            Severity = e.SeverityText,
            SeverityNumber = e.SeverityNumber,
            Service = e.ServiceName,
            EventName = e.EventName,
            Message = e.Body,
            TraceId = e.TraceId,
            SpanId = e.SpanId,
            LogAttributes = e.LogAttributes,
        };

        await writer.WriteLineAsync(JsonSerializer.Serialize(row, WireJsonOptions.Instance));
    }

    /// <summary>
    /// The "incident bundle" mode: a zip at <see cref="Settings.Output"/> containing
    /// <c>manifest.json</c> (always) plus whichever of <c>trace.json</c>/<c>logs.ndjson</c>/
    /// <c>metrics.json</c> the caller asked for. Fetches the trace first via
    /// <c>GET /api/traces/{traceId}</c> (reusing <see cref="TracesCommand"/>'s
    /// <see cref="TraceDtoWire"/>/<see cref="SpanDtoWire"/>) purely to derive the archive's
    /// scope - the span-covered time window (padded by <paramref name="margin"/>) and the
    /// distinct set of services involved - since that's the whole point of keying a bundle
    /// off a trace-id rather than making the caller guess a <c>--since</c> window and
    /// service list by hand the way the plain log export does. Falls back to
    /// <paramref name="sinceFallback"/> ending "now" (and an empty service list, so
    /// --include-metrics finds nothing) if the trace itself has already aged out of
    /// retention - the archive still gets built rather than failing outright, since
    /// --include-logs's own <paramref name="logFilterTemplate"/>.TraceId still scopes logs
    /// correctly even without a resolved span window.
    /// </summary>
    private static async Task<int> ExecuteIncidentBundleAsync(
        FlareInstance instance,
        Settings settings,
        LogFilterWire logFilterTemplate,
        TimeSpan sinceFallback,
        TimeSpan margin,
        CancellationToken cancellationToken)
    {
        var port = instance.ReadEnvValue("FLARE_API_PORT", "8080");
        using var http = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var interrupted = false;
        using var interrupt = InterruptSignal.OnInterrupt(() =>
        {
            interrupted = true;
            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        });

        var traceId = settings.TraceId!;
        TraceDtoWire? trace;
        try
        {
            using var httpResponse = await http.GetAsync($"/api/traces/{Uri.EscapeDataString(traceId)}", cts.Token);

            if (httpResponse.StatusCode == HttpStatusCode.NotFound)
            {
                trace = null;
            }
            else if (!httpResponse.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[red]✗[/] GET /api/traces/{Markup.Escape(traceId)} failed: {(int)httpResponse.StatusCode} {httpResponse.ReasonPhrase}");
                return 1;
            }
            else
            {
                trace = await httpResponse.Content.ReadFromJsonAsync<TraceDtoWire>(WireJsonOptions.Instance, cts.Token);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] Couldn't reach the API on localhost:{port} - is `api` running? Check `flare status`.");
            return 1;
        }

        var spans = trace?.Spans ?? [];
        List<string> services;
        DateTimeOffset windowFrom;
        DateTimeOffset windowTo;

        if (spans.Count > 0)
        {
            windowFrom = spans.Min(s => s.StartTime) - margin;
            windowTo = spans.Max(s => s.EndTime) + margin;
            services = spans.Select(s => s.ServiceName).Distinct().OrderBy(s => s, StringComparer.Ordinal).ToList();
        }
        else
        {
            await Console.Error.WriteLineAsync(
                $"No spans found for trace {traceId} (already aged out of retention?) - falling back to --since ({settings.Since}) for --include-logs; --include-metrics will find nothing without a resolved service list.");
            windowTo = DateTimeOffset.UtcNow;
            windowFrom = windowTo - sinceFallback;
            services = [];
        }

        var manifest = new IncidentManifestWire
        {
            TraceId = traceId,
            GeneratedAt = DateTimeOffset.UtcNow,
            From = windowFrom,
            To = windowTo,
            Services = services,
            SpanCount = spans.Count,
            IncludesTrace = settings.IncludeTrace,
            IncludesLogs = settings.IncludeLogs,
            IncludesMetrics = settings.IncludeMetrics,
        };

        try
        {
            using var fileStream = new FileStream(settings.Output!, FileMode.Create, FileAccess.Write);
            using (var zip = new ZipArchive(fileStream, ZipArchiveMode.Create))
            {
                await WriteJsonEntryAsync(zip, "manifest.json", manifest, cts.Token);

                if (settings.IncludeTrace)
                {
                    await WriteJsonEntryAsync(zip, "trace.json", trace ?? new TraceDtoWire { TraceId = traceId, Spans = [] }, cts.Token);
                    await Console.Error.WriteLineAsync($"trace.json: {spans.Count} span(s).");
                }

                if (settings.IncludeLogs)
                {
                    var logFilter = new LogFilterWire
                    {
                        From = windowFrom,
                        To = windowTo,
                        Services = logFilterTemplate.Services,
                        SeverityNumbers = logFilterTemplate.SeverityNumbers,
                        TraceId = logFilterTemplate.TraceId,
                        SpanId = logFilterTemplate.SpanId,
                        PatternId = logFilterTemplate.PatternId,
                        Search = logFilterTemplate.Search,
                    };

                    var entry = zip.CreateEntry("logs.ndjson", CompressionLevel.Optimal);
                    await using var entryStream = entry.Open();
                    await using var writer = new StreamWriter(entryStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

                    var logCount = await ExportLogsNdjsonAsync(http, logFilter, settings.Limit, writer, cts.Token);
                    await writer.FlushAsync(cts.Token);
                    await Console.Error.WriteLineAsync($"logs.ndjson: {logCount} row(s).");
                }

                if (settings.IncludeMetrics)
                {
                    if (services.Count == 0)
                    {
                        await Console.Error.WriteLineAsync("metrics.json: skipped - no services resolved from the trace.");
                    }
                    else
                    {
                        var metricCount = await WriteMetricsEntryAsync(zip, http, services, windowFrom, windowTo, cts.Token);
                        await Console.Error.WriteLineAsync($"metrics.json: {metricCount} metric(s) across {services.Count} service(s).");
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Ctrl+C - each entry above is fully written (and its own stream closed) before
            // the next one starts, so the zip's central directory (written when the
            // ZipArchive itself is disposed, in the `using` block's normal unwind) still
            // ends up valid and openable, just missing whatever section was interrupted
            // mid-fetch - same "report what was written so far" spirit as classic export
            // mode's own OperationCanceledException handling.
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or IOException or UnauthorizedAccessException)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] Couldn't build the incident bundle: {Markup.Escape(ex.Message)}");
            TryDeleteIncompleteBundle(settings.Output!);
            return 1;
        }

        if (interrupted)
        {
            await Console.Error.WriteLineAsync($"Interrupted - wrote partial bundle to {settings.Output}.");
            return 0;
        }

        await Console.Error.WriteLineAsync($"Wrote incident bundle to {settings.Output}.");
        return 0;
    }

    // A failed (non-cancelled) bundle build leaves a truncated, misleading zip behind
    // otherwise - best-effort cleanup, not fatal if it can't be removed (e.g. still held
    // open by another process on Windows).
    private static void TryDeleteIncompleteBundle(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static async Task WriteJsonEntryAsync<T>(ZipArchive zip, string entryName, T value, CancellationToken cancellationToken)
    {
        var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
        await using var entryStream = entry.Open();
        await JsonSerializer.SerializeAsync(entryStream, value, IncidentJsonOptions.Instance, cancellationToken);
    }

    /// <summary>
    /// NDJSON-only, no-CSV, no-per-page-stderr-noise-beyond-a-single-progress-line cousin of
    /// the classic export loop above - kept as its own small pagination loop rather than
    /// generalizing that one, since the two callers want different failure handling (a
    /// bundle's log phase throws on HTTP failure so <see cref="ExecuteIncidentBundleAsync"/>'s
    /// single catch block can clean up the half-written zip; the classic path prints and
    /// returns 1 directly).
    /// </summary>
    private static async Task<int> ExportLogsNdjsonAsync(HttpClient http, LogFilterWire filter, int limit, TextWriter writer, CancellationToken cancellationToken)
    {
        var total = 0;
        string? cursor = null;

        while (total < limit)
        {
            using var httpResponse = await http.PostAsJsonAsync(
                "/api/logs/search",
                new LogSearchRequestWire { Filter = filter, Cursor = cursor, PageSize = PageSize },
                WireJsonOptions.Instance,
                cancellationToken);

            if (!httpResponse.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"POST /api/logs/search failed: {(int)httpResponse.StatusCode} {httpResponse.ReasonPhrase}");
            }

            var response = await httpResponse.Content.ReadFromJsonAsync<LogSearchResponseWire>(WireJsonOptions.Instance, cancellationToken);
            var events = response?.Events ?? [];
            if (events.Count == 0)
            {
                break;
            }

            foreach (var e in events)
            {
                if (total >= limit)
                {
                    break;
                }

                await WriteNdjsonRowAsync(writer, e);
                total++;
            }

            await Console.Error.WriteLineAsync($"logs.ndjson: fetched {total} row(s)...");

            cursor = response?.NextCursor;
            if (cursor is null)
            {
                break;
            }
        }

        return total;
    }

    /// <summary>
    /// Builds metrics.json: every metric name <see cref="MetricsCommand"/>'s
    /// <c>POST /api/metrics/names</c> reports for <paramref name="services"/> over
    /// [<paramref name="from"/>, <paramref name="to"/>] (capped at <see cref="MaxBundleMetrics"/>),
    /// each queried via <c>POST /api/metrics/query</c> at one bucket width for the whole
    /// window (<see cref="MetricCommand.PickBucketWidthSeconds"/> - same picker
    /// <c>flare metric</c> itself uses). No <c>--group-by</c> equivalent here: a bundle
    /// wants raw per-service+attribute series, not a caller-chosen collapse.
    /// </summary>
    private static async Task<int> WriteMetricsEntryAsync(
        ZipArchive zip,
        HttpClient http,
        IReadOnlyList<string> services,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var namesResponse = await MetricsPostAsync<MetricNamesRequestWire, MetricNamesResponseWire>(
            http,
            "/api/metrics/names",
            new MetricNamesRequestWire { From = from, To = to, Services = services },
            cancellationToken);

        var names = namesResponse?.Metrics ?? [];
        if (names.Count > MaxBundleMetrics)
        {
            await Console.Error.WriteLineAsync(
                $"metrics.json: {names.Count} metric(s) found across {services.Count} service(s) - keeping the first {MaxBundleMetrics} to bound the bundle's size.");
            names = names.Take(MaxBundleMetrics).ToList();
        }

        var bucketWidthSeconds = MetricCommand.PickBucketWidthSeconds((to - from).TotalSeconds);
        var bundled = new List<IncidentMetricWire>(names.Count);

        foreach (var metric in names)
        {
            var queryResponse = await MetricsPostAsync<MetricQueryRequestWire, MetricQueryResponseWire>(
                http,
                "/api/metrics/query",
                new MetricQueryRequestWire
                {
                    MetricName = metric.MetricName,
                    Type = metric.Type,
                    Filter = new MetricFilterWire { From = from, To = to, Services = [metric.ServiceName] },
                    BucketWidthSeconds = bucketWidthSeconds,
                },
                cancellationToken);

            bundled.Add(new IncidentMetricWire
            {
                MetricName = metric.MetricName,
                ServiceName = metric.ServiceName,
                Type = metric.Type,
                Unit = metric.Unit,
                Description = metric.Description,
                BucketWidthSeconds = bucketWidthSeconds,
                Series = queryResponse?.Series ?? [],
            });
        }

        await WriteJsonEntryAsync(zip, "metrics.json", bundled, cancellationToken);
        return bundled.Count;
    }

    private static async Task<TResponse?> MetricsPostAsync<TRequest, TResponse>(HttpClient http, string path, TRequest body, CancellationToken cancellationToken)
        where TResponse : class
    {
        using var httpResponse = await http.PostAsJsonAsync(path, body, MetricsWireJsonOptions.Instance, cancellationToken);
        if (!httpResponse.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"POST {path} failed: {(int)httpResponse.StatusCode} {httpResponse.ReasonPhrase}");
        }

        return await httpResponse.Content.ReadFromJsonAsync<TResponse>(MetricsWireJsonOptions.Instance, cancellationToken);
    }
}

/// <summary>
/// Export row shape for NDJSON - parity with export.ts's HEADER field set (EventId,
/// Timestamp, Severity, SeverityNumber, Service, EventName, Message, TraceId, SpanId,
/// LogAttributes), deliberately excluding "low-signal OTel plumbing"
/// (ObservedTimestamp/IngestedAt/TraceFlags/*SchemaUrl/ScopeName/ScopeVersion/
/// ResourceAttributes/ScopeAttributes/PatternId/PatternTemplate) the same way that file's own comment
/// documents. One object per line, no enclosing array/pretty-print - unlike export.ts's
/// own (never-shipped) NDJSON idea, which would have pretty-printed a JSON array instead.
/// </summary>
internal sealed class ExportEventWire
{
    public required Guid EventId { get; init; }

    public required DateTimeOffset Timestamp { get; init; }

    public required string Severity { get; init; }

    public required byte SeverityNumber { get; init; }

    public required string Service { get; init; }

    public string EventName { get; init; } = "";

    public required string Message { get; init; }

    public string TraceId { get; init; } = "";

    public string SpanId { get; init; } = "";

    public Dictionary<string, string> LogAttributes { get; init; } = [];
}

// ---- Incident bundle wire shapes - own JSON options (pretty-printed; these are files a
// human reads while triaging, not another service's wire format) rather than reusing
// WireJsonOptions/MetricsWireJsonOptions. ------------------------------------------------

/// <summary>
/// incident.zip's manifest.json - the bundle's own table of contents, so a bug-report
/// reader (or a future re-import tool) doesn't have to infer the covered trace/time
/// window/services from file presence and content alone.
/// </summary>
internal sealed class IncidentManifestWire
{
    public required string TraceId { get; init; }

    public DateTimeOffset GeneratedAt { get; init; }

    public DateTimeOffset From { get; init; }

    public DateTimeOffset To { get; init; }

    public List<string> Services { get; init; } = [];

    public int SpanCount { get; init; }

    public bool IncludesTrace { get; init; }

    public bool IncludesLogs { get; init; }

    public bool IncludesMetrics { get; init; }
}

/// <summary>
/// One metrics.json entry - <see cref="MetricNameInfoWire"/>'s descriptive fields plus the
/// queried series, flattened into one self-describing record instead of the separate
/// names/query pair <c>flare metric</c>'s own interactive name-resolution step needs.
/// </summary>
internal sealed class IncidentMetricWire
{
    public required string MetricName { get; init; }

    public required string ServiceName { get; init; }

    /// <summary>"Gauge" | "Sum" | "Histogram" - see MetricPointType.</summary>
    public required string Type { get; init; }

    public string? Unit { get; init; }

    public string? Description { get; init; }

    public int BucketWidthSeconds { get; init; }

    public List<MetricSeriesWire> Series { get; init; } = [];
}

internal static class IncidentJsonOptions
{
    public static readonly JsonSerializerOptions Instance = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };
}
