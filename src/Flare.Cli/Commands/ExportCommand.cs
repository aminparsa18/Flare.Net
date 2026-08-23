using System.ComponentModel;
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
/// </summary>
internal sealed class ExportCommand : AsyncCommand<ExportCommand.Settings>
{
    private const int PageSize = 1000; // matches export.ts's EXPORT_PAGE_SIZE - the backend's own LogSearchQueryBuilder max PageSize.

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
}

/// <summary>
/// Export row shape for NDJSON - parity with export.ts's HEADER field set (EventId,
/// Timestamp, Severity, SeverityNumber, Service, EventName, Message, TraceId, SpanId,
/// LogAttributes), deliberately excluding "low-signal OTel plumbing"
/// (ObservedTimestamp/TraceFlags/*SchemaUrl/ScopeName/ScopeVersion/ResourceAttributes/
/// ScopeAttributes/PatternId/PatternTemplate) the same way that file's own comment
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
