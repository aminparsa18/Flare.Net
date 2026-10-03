using Flare.Mcp;
using System.ComponentModel;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Flare.Cli.Internal;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Flare.Cli.Commands;

/// <summary>
/// One-shot trace search via Flare.Api's <c>POST /api/spans/search</c> (root spans only) -
/// the CLI-native equivalent of the dashboard's Trace List (<c>traces/+page.svelte</c> /
/// <c>TraceList.svelte</c>). Hand-rolled against the documented wire contract (see
/// <c>src/Flare.Api/Model/SpanFilter.cs</c>/<c>SpanDto.cs</c>) rather than a project
/// reference on Flare.Api - same "opaque process talking JSON over HTTP" boundary
/// <see cref="LogTailClient"/> already draws for the live-tail WebSocket. No live-tail
/// counterpart here (unlike `flare tail` for logs): the dashboard's own Traces state
/// (<c>state.svelte.ts</c>) is deliberately non-live too, so there's no live protocol to
/// mirror. Attribute filters (<c>SpanFilter.Attributes</c>) are exposed via the repeatable
/// <c>--attr</c>/<c>--attr-not</c>/<c>--attr-exists</c>/<c>--attr-absent</c> flags - see
/// <see cref="Internal.AttributeFlagParsing"/> - always against the default <c>Span</c>
/// bag (no <c>--attr-bag</c> flag yet).
/// </summary>
internal sealed class TracesCommand : AsyncCommand<TracesCommand.Settings>
{
    internal sealed class Settings : InstanceSettings
    {
        [CommandOption("-s|--service <NAME>")]
        [Description("Filter by exact service name. Repeatable.")]
        public string[] Service { get; init; } = [];

        [CommandOption("--status <STATUS>")]
        [Description("Filter by span status: ok, error, unset. Repeatable.")]
        public string[] Status { get; init; } = [];

        [CommandOption("--kind <KIND>")]
        [Description("Filter by root span kind: internal, server, client, producer, consumer. Repeatable.")]
        public string[] Kind { get; init; } = [];

        [CommandOption("--trace-id <ID>")]
        [Description("Exact lower-hex TraceId match.")]
        public string? TraceId { get; init; }

        [CommandOption("--attr <KEY=VALUE>")]
        [Description("Span attribute equals: key=value. Repeatable.")]
        public string[] Attr { get; init; } = [];

        [CommandOption("--attr-not <KEY=VALUE>")]
        [Description("Span attribute not-equals (absent also matches): key=value. Repeatable.")]
        public string[] AttrNot { get; init; } = [];

        [CommandOption("--attr-exists <KEY>")]
        [Description("Span attribute key is present, any value. Repeatable.")]
        public string[] AttrExists { get; init; } = [];

        [CommandOption("--attr-absent <KEY>")]
        [Description("Span attribute key is absent.")]
        public string[] AttrAbsent { get; init; } = [];

        [CommandOption("--min-duration <DURATION>")]
        [Description("Inclusive lower bound on trace duration, e.g. 500ms, 2s, 1.5m.")]
        public string? MinDuration { get; init; }

        [CommandOption("--max-duration <DURATION>")]
        [Description("Inclusive upper bound on trace duration, e.g. 500ms, 2s, 1.5m.")]
        public string? MaxDuration { get; init; }

        [CommandOption("--entry")]
        [Description("List each service's entry spans (no parent, or a parent in another service) instead of one root span per trace.")]
        public bool Entry { get; init; }

        [CommandOption("--span <SPEC>")]
        [Description("A structural span condition for --where: LETTER:key=value,... with keys service, name, status (ok/error/unset), min-duration. E.g. \"B:service=payment,status=error\". Repeatable.")]
        public string[] Span { get; init; } = [];

        [CommandOption("--where <EXPR>")]
        [Description("Only traces whose spans match this expression over the --span letters: A -> B (direct child), A => B (any descendant), AND, OR, NOT, parentheses.")]
        public string? Where { get; init; }

        [CommandOption("--since <RANGE>")]
        [Description("How far back to search: 15m, 1h, 6h, 24h, 7d. Default 1h.")]
        public string Since { get; init; } = "1h";

        [CommandOption("--limit <COUNT>")]
        [Description("Max traces to print. Default 20.")]
        public int Limit { get; init; } = 20;

        [CommandOption("--sort <KEY>")]
        [Description("Order by: time (default, newest first), duration (slowest first), spans (most spans first).")]
        public string Sort { get; init; } = "time";

        [CommandOption("--asc")]
        [Description("Reverse --sort to ascending: oldest, fastest, or fewest spans first.")]
        public bool Asc { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var instance = FlareHome.ResolveTarget(settings.InstanceName);

        if (!instance.IsInitialized)
        {
            AnsiConsole.MarkupLine($"[grey]Not initialized yet - run `{instance.StartHint}` first.[/]");
            return 1;
        }

        var statusCodes = new List<string>();
        foreach (var status in settings.Status)
        {
            if (!TryExpandStatus(status, out var code))
            {
                AnsiConsole.MarkupLine($"[red]✗[/] Unknown status '{Markup.Escape(status)}' - expected one of: ok, error, unset.");
                return 1;
            }

            statusCodes.Add(code);
        }

        var kinds = new List<byte>();
        foreach (var kind in settings.Kind)
        {
            if (!TryExpandKind(kind, out var number))
            {
                AnsiConsole.MarkupLine($"[red]✗[/] Unknown kind '{Markup.Escape(kind)}' - expected one of: internal, server, client, producer, consumer.");
                return 1;
            }

            kinds.Add(number);
        }

        ulong? minDurationNano = null;
        if (settings.MinDuration is not null && !TryParseDurationNano(settings.MinDuration, out minDurationNano))
        {
            AnsiConsole.MarkupLine($"[red]✗[/] Couldn't parse --min-duration '{Markup.Escape(settings.MinDuration)}' - expected e.g. 500ms, 2s, 1.5m.");
            return 1;
        }

        ulong? maxDurationNano = null;
        if (settings.MaxDuration is not null && !TryParseDurationNano(settings.MaxDuration, out maxDurationNano))
        {
            AnsiConsole.MarkupLine($"[red]✗[/] Couldn't parse --max-duration '{Markup.Escape(settings.MaxDuration)}' - expected e.g. 500ms, 2s, 1.5m.");
            return 1;
        }

        if (!TryParseSince(settings.Since, out var since))
        {
            AnsiConsole.MarkupLine($"[red]✗[/] Couldn't parse --since '{Markup.Escape(settings.Since)}' - expected e.g. 15m, 1h, 6h, 24h, 7d.");
            return 1;
        }

        if (!TryExpandSort(settings.Sort, out var sortBy))
        {
            AnsiConsole.MarkupLine($"[red]✗[/] Unknown --sort '{Markup.Escape(settings.Sort)}' - expected one of: time, duration, spans.");
            return 1;
        }

        if (!AttributeFlagParsing.TryParse(settings.Attr, settings.AttrNot, settings.AttrExists, settings.AttrAbsent, out var parsedAttrs, out var attrError))
        {
            AnsiConsole.MarkupLine($"[red]✗[/] {Markup.Escape(attrError)}");
            return 1;
        }

        TraceStructureWire? structure = null;
        if (settings.Where is not null || settings.Span.Length > 0)
        {
            if (!TryParseStructure(settings.Span, settings.Where, out structure, out var structureError))
            {
                AnsiConsole.MarkupLine($"[red]✗[/] {Markup.Escape(structureError)}");
                return 1;
            }
        }

        var to = DateTimeOffset.UtcNow;
        var from = to - since;

        var filter = new SpanFilterWire
        {
            From = from,
            To = to,
            RootSpansOnly = !settings.Entry,
            EntrySpansOnly = settings.Entry,
            Services = settings.Service.Length > 0 ? settings.Service : null,
            Kinds = kinds.Count > 0 ? kinds : null,
            StatusCodes = statusCodes.Count > 0 ? statusCodes : null,
            TraceId = string.IsNullOrWhiteSpace(settings.TraceId) ? null : settings.TraceId,
            MinDurationNano = minDurationNano,
            MaxDurationNano = maxDurationNano,
            Attributes = parsedAttrs.Count > 0
                ? parsedAttrs.Select(a => new SpanAttributeFilterWire { Key = a.Key, Value = a.Value, Operator = a.Operator }).ToList()
                : null,
            Structure = structure,
        };

        var port = instance.ReadEnvValue("FLARE_API_PORT", "8080");
        using var http = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };

        SpanSearchResponseWire? response;
        try
        {
            using var httpResponse = await http.PostAsJsonAsync(
                "/api/spans/search",
                new SpanSearchRequestWire { Filter = filter, PageSize = Math.Clamp(settings.Limit, 1, 500), SortBy = sortBy, SortAscending = settings.Asc },
                WireJsonOptions.Instance,
                cancellationToken);

            if (!httpResponse.IsSuccessStatusCode)
            {
                // A 400 is an invalid --where/--span; its ProblemDetails says why.
                var detail = httpResponse.StatusCode == System.Net.HttpStatusCode.BadRequest
                    ? await ProblemDetailAsync(httpResponse, cancellationToken)
                    : null;
                AnsiConsole.MarkupLine($"[red]✗[/] {Markup.Escape(detail ?? $"POST /api/spans/search failed: {(int)httpResponse.StatusCode} {httpResponse.ReasonPhrase}")}");
                return 1;
            }

            response = await httpResponse.Content.ReadFromJsonAsync<SpanSearchResponseWire>(WireJsonOptions.Instance, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] Couldn't reach the API on localhost:{port} - is `api` running? Check `flare status`.");
            return 1;
        }

        var traces = response?.Spans ?? [];
        if (traces.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]No traces match the current filters.[/]");
            return 0;
        }

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Time");
        table.AddColumn("Status");
        table.AddColumn("Service");
        table.AddColumn("Name");
        table.AddColumn("Duration");
        table.AddColumn(new TableColumn("Spans").RightAligned());
        table.AddColumn("Trace ID");

        foreach (var span in traces.Take(settings.Limit))
        {
            var time = span.StartTime.ToLocalTime().ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
            var (statusLabel, statusColor) = DescribeStatus(RolledUpStatusCode(span));

            table.AddRow(
                time,
                $"[{statusColor}]{statusLabel}[/]",
                Markup.Escape(span.ServiceName),
                Markup.Escape(span.Name),
                FormatDurationNano(span.DurationNano),
                (span.SpanCount ?? 1).ToString(CultureInfo.InvariantCulture),
                $"[grey]{Markup.Escape(span.TraceId)}[/]");
        }

        AnsiConsole.Write(table);

        if (response?.NextCursor is not null)
        {
            AnsiConsole.MarkupLine($"[grey]… more available - narrow --since/--service or raise --limit (shown: {Math.Min(traces.Count, settings.Limit)}).[/]");
        }

        return 0;
    }

    private static async Task<string?> ProblemDetailAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            return doc.RootElement.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.String ? detail.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// <c>--span</c>/<c>--where</c> into a <c>SpanFilter.Structure</c>. Only the spec syntax is
    /// checked here - the expression itself (and which letters it may use) is validated by
    /// the API, whose 400 message the caller prints.
    /// </summary>
    internal static bool TryParseStructure(IReadOnlyList<string> specs, string? where, out TraceStructureWire? structure, out string error)
    {
        structure = null;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(where))
        {
            error = "--span needs --where, e.g. --where \"A => B\".";
            return false;
        }

        if (specs.Count == 0)
        {
            error = "--where needs at least one --span condition, e.g. --span \"A:service=checkout\".";
            return false;
        }

        var conditions = new List<TraceSpanConditionWire>();
        foreach (var spec in specs)
        {
            var colon = spec.IndexOf(':');
            if (colon <= 0)
            {
                error = $"Couldn't parse --span '{spec}' - expected LETTER:key=value,..., e.g. A:service=checkout.";
                return false;
            }

            string? service = null, name = null, status = null;
            ulong? minDuration = null;
            foreach (var pair in spec[(colon + 1)..].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var eq = pair.IndexOf('=');
                var key = eq > 0 ? pair[..eq].Trim().ToLowerInvariant() : string.Empty;
                var value = eq > 0 ? pair[(eq + 1)..].Trim() : string.Empty;
                switch (key)
                {
                    case "service":
                        service = value;
                        break;
                    case "name":
                        name = value;
                        break;
                    case "status" when TryExpandStatus(value, out var code):
                        status = code;
                        break;
                    case "min-duration" when TryParseDurationNano(value, out var nanos):
                        minDuration = nanos;
                        break;
                    default:
                        error = $"Couldn't parse '{pair}' in --span '{spec}' - keys are service, name, status (ok/error/unset), min-duration (e.g. 500ms).";
                        return false;
                }
            }

            conditions.Add(new TraceSpanConditionWire { Name = spec[..colon].Trim(), ServiceName = service, SpanName = name, StatusCode = status, MinDurationNano = minDuration });
        }

        structure = new TraceStructureWire { Conditions = conditions, Expression = where };
        return true;
    }

    private static bool TryExpandStatus(string status, out string code)
    {
        switch (status.Trim().ToLowerInvariant())
        {
            case "ok":
                code = "STATUS_CODE_OK";
                return true;
            case "error":
                code = "STATUS_CODE_ERROR";
                return true;
            case "unset":
                code = "STATUS_CODE_UNSET";
                return true;
            default:
                code = "";
                return false;
        }
    }

    // Mirrors dashboard/src/lib/traces/status.ts's KIND_LABELS (OTel Span.SpanKind, spec-fixed 0-5).
    /// <summary>Maps <c>--sort</c>'s friendly names onto <c>SpanSortKey</c> member names (the wire's string-enum values).</summary>
    private static bool TryExpandSort(string sort, out string sortBy)
    {
        sortBy = sort.Trim().ToLowerInvariant() switch
        {
            "time" => "StartTime",
            "duration" => "Duration",
            "spans" => "SpanCount",
            _ => "",
        };
        return sortBy.Length > 0;
    }

    private static bool TryExpandKind(string kind, out byte number)
    {
        switch (kind.Trim().ToLowerInvariant())
        {
            case "unspecified":
                number = 0;
                return true;
            case "internal":
                number = 1;
                return true;
            case "server":
                number = 2;
                return true;
            case "client":
                number = 3;
                return true;
            case "producer":
                number = 4;
                return true;
            case "consumer":
                number = 5;
                return true;
            default:
                number = 0;
                return false;
        }
    }

    private static (string Label, string Color) DescribeStatus(string statusCode) => statusCode switch
    {
        "STATUS_CODE_OK" => ("OK", "green"),
        "STATUS_CODE_ERROR" => ("Error", "red"),
        _ => ("Unset", "grey"),
    };

    /// <summary>
    /// Mirrors dashboard/src/lib/traces/status.ts's rolledUpStatusCode: a root span's own
    /// <see cref="SpanDtoWire.StatusCode"/> only reflects the root, not the rest of the
    /// trace, so a trace whose root succeeded but has an erroring span deeper in the call
    /// chain would otherwise print as healthy. <see cref="SpanDtoWire.HasError"/> is the
    /// server-computed rollup across every span in the trace (see
    /// <c>Flare.Api/Query/SpanRollupQueryBuilder.cs</c>) - when true and the root itself
    /// didn't already fail, this reports STATUS_CODE_ERROR so DescribeStatus renders the
    /// same "Error" row it would for a directly-failing root.
    /// </summary>
    private static string RolledUpStatusCode(SpanDtoWire span) =>
        span.HasError == true && span.StatusCode != "STATUS_CODE_ERROR" ? "STATUS_CODE_ERROR" : span.StatusCode;

    // Inverse of dashboard/src/lib/traces/duration.ts's formatDurationNano - accepts a
    // bare number (nanoseconds) or a number with a us/ms/s/m unit suffix.
    internal static bool TryParseDurationNano(string text, out ulong? nanos)
    {
        nanos = null;
        var trimmed = text.Trim();
        var unitStart = trimmed.Length;
        while (unitStart > 0 && !char.IsDigit(trimmed[unitStart - 1]) && trimmed[unitStart - 1] != '.')
        {
            unitStart--;
        }

        var numberPart = trimmed[..unitStart];
        var unitPart = trimmed[unitStart..].Trim().ToLowerInvariant();

        if (!double.TryParse(numberPart, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || value < 0)
        {
            return false;
        }

        var multiplier = unitPart switch
        {
            "" or "ns" => 1d,
            "us" or "µs" => 1_000d,
            "ms" => 1_000_000d,
            "s" => 1_000_000_000d,
            "m" => 60d * 1_000_000_000d,
            _ => -1d,
        };

        if (multiplier < 0)
        {
            return false;
        }

        nanos = (ulong)Math.Round(value * multiplier);
        return true;
    }

    // Mirrors dashboard/src/lib/logs/time-range.ts's TIME_RANGE_PRESETS, but accepts any
    // magnitude (not just the five fixed presets) since a CLI flag doesn't need a
    // picklist. Internal (not private) - MetricsCommand/MetricCommand's own `--since`
    // reuses it too rather than duplicating the same grammar a third time.
    internal static bool TryParseSince(string text, out TimeSpan span) => WireFormat.TryParseSince(text, out span);

    // Mirrors dashboard/src/lib/traces/duration.ts's formatDurationNano exactly. Internal
    // (not private) - TraceCommand's waterfall reuses it for bar/axis labels too.
    internal static string FormatDurationNano(ulong durationNano) => WireFormat.FormatDurationNano(durationNano);
}

// ---- Wire DTOs - hand-mirror of Flare.Api's Model/SpanFilter.cs, Model/SpanDto.cs,
// Model/SpanSearchRequest.cs (see SpansJsonContext's camelCase-properties/PascalCase-
// string-enum-values convention). Keep in sync with those files by hand, same as
// dashboard/src/lib/traces-api.ts already does from the TypeScript side. -------------









