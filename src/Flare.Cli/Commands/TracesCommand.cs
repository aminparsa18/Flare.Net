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
/// mirror.
/// </summary>
internal sealed class TracesCommand : AsyncCommand<TracesCommand.Settings>
{
    internal sealed class Settings : CommandSettings
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

        [CommandOption("--min-duration <DURATION>")]
        [Description("Inclusive lower bound on trace duration, e.g. 500ms, 2s, 1.5m.")]
        public string? MinDuration { get; init; }

        [CommandOption("--max-duration <DURATION>")]
        [Description("Inclusive upper bound on trace duration, e.g. 500ms, 2s, 1.5m.")]
        public string? MaxDuration { get; init; }

        [CommandOption("--since <RANGE>")]
        [Description("How far back to search: 15m, 1h, 6h, 24h, 7d. Default 1h.")]
        public string Since { get; init; } = "1h";

        [CommandOption("-n|--limit <COUNT>")]
        [Description("Max traces to print. Default 20.")]
        public int Limit { get; init; } = 20;
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (!FlareHome.IsInitialized)
        {
            AnsiConsole.MarkupLine("[grey]Not initialized yet - run `flare start` first.[/]");
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

        var to = DateTimeOffset.UtcNow;
        var from = to - since;

        var filter = new SpanFilterWire
        {
            From = from,
            To = to,
            RootSpansOnly = true,
            Services = settings.Service.Length > 0 ? settings.Service : null,
            Kinds = kinds.Count > 0 ? kinds : null,
            StatusCodes = statusCodes.Count > 0 ? statusCodes : null,
            TraceId = string.IsNullOrWhiteSpace(settings.TraceId) ? null : settings.TraceId,
            MinDurationNano = minDurationNano,
            MaxDurationNano = maxDurationNano,
        };

        var port = FlareHome.ReadEnvValue("FLARE_API_PORT", "8080");
        using var http = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };

        SpanSearchResponseWire? response;
        try
        {
            using var httpResponse = await http.PostAsJsonAsync(
                "/api/spans/search",
                new SpanSearchRequestWire { Filter = filter, PageSize = Math.Clamp(settings.Limit, 1, 500) },
                WireJsonOptions.Instance,
                cancellationToken);

            if (!httpResponse.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[red]✗[/] POST /api/spans/search failed: {(int)httpResponse.StatusCode} {httpResponse.ReasonPhrase}");
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
            var (statusLabel, statusColor) = DescribeStatus(span.StatusCode);

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

    // Inverse of dashboard/src/lib/traces/duration.ts's formatDurationNano - accepts a
    // bare number (nanoseconds) or a number with a us/ms/s/m unit suffix.
    private static bool TryParseDurationNano(string text, out ulong? nanos)
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
    internal static bool TryParseSince(string text, out TimeSpan span)
    {
        span = TimeSpan.Zero;
        var trimmed = text.Trim().ToLowerInvariant();
        if (trimmed.Length < 2)
        {
            return false;
        }

        var unit = trimmed[^1];
        var numberPart = trimmed[..^1];
        if (!double.TryParse(numberPart, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || value <= 0)
        {
            return false;
        }

        span = unit switch
        {
            's' => TimeSpan.FromSeconds(value),
            'm' => TimeSpan.FromMinutes(value),
            'h' => TimeSpan.FromHours(value),
            'd' => TimeSpan.FromDays(value),
            _ => TimeSpan.Zero,
        };

        return span > TimeSpan.Zero;
    }

    // Mirrors dashboard/src/lib/traces/duration.ts's formatDurationNano exactly. Internal
    // (not private) - TraceCommand's waterfall reuses it for bar/axis labels too.
    internal static string FormatDurationNano(ulong durationNano)
    {
        var ms = durationNano / 1_000_000d;
        if (ms < 1)
        {
            return $"{Math.Round(durationNano / 1_000d)}µs";
        }

        if (ms < 1_000)
        {
            return ms < 10 ? $"{ms:F2}ms" : $"{ms:F1}ms";
        }

        return $"{ms / 1_000:F2}s";
    }
}

// ---- Wire DTOs - hand-mirror of Flare.Api's Model/SpanFilter.cs, Model/SpanDto.cs,
// Model/SpanSearchRequest.cs (see SpansJsonContext's camelCase-properties/PascalCase-
// string-enum-values convention). Keep in sync with those files by hand, same as
// dashboard/src/lib/traces-api.ts already does from the TypeScript side. -------------

internal sealed class SpanFilterWire
{
    public DateTimeOffset? From { get; init; }

    public DateTimeOffset? To { get; init; }

    public IReadOnlyList<string>? Services { get; init; }

    public IReadOnlyList<byte>? Kinds { get; init; }

    public IReadOnlyList<string>? StatusCodes { get; init; }

    public string? TraceId { get; init; }

    public bool RootSpansOnly { get; init; }

    public ulong? MinDurationNano { get; init; }

    public ulong? MaxDurationNano { get; init; }
}

internal sealed class SpanSearchRequestWire
{
    public SpanFilterWire? Filter { get; init; }

    public string? Cursor { get; init; }

    public int? PageSize { get; init; }
}

internal sealed class SpanDtoWire
{
    public required string TraceId { get; init; }

    // Empty string = root span - same "empty string means absent" convention traces-api.ts
    // documents. Not `required`: SpanCount is search-only and SpanId/ParentSpanId/EndTime/
    // Kind are unused by TracesCommand's flat list, so leaving them defaultable keeps this
    // one DTO shared by both the search (SpanSearchResponseWire) and get-trace
    // (TraceDtoWire) responses without either caller needing fields it doesn't read.
    public string SpanId { get; init; } = "";

    public string ParentSpanId { get; init; } = "";

    public required string ServiceName { get; init; }

    public required string Name { get; init; }

    /// <summary>OTel SpanKind: 0=unspecified, 1=internal, 2=server, 3=client, 4=producer, 5=consumer.</summary>
    public int Kind { get; init; }

    public DateTimeOffset StartTime { get; init; }

    public DateTimeOffset EndTime { get; init; }

    public ulong DurationNano { get; init; }

    public required string StatusCode { get; init; }

    public int? SpanCount { get; init; }
}

internal sealed class SpanSearchResponseWire
{
    public List<SpanDtoWire> Spans { get; init; } = [];

    public string? NextCursor { get; init; }
}

/// <summary>Wire shape of <c>GET /api/traces/{traceId}</c>'s response (<c>TraceDto</c> - see traces-api.ts).</summary>
internal sealed class TraceDtoWire
{
    public required string TraceId { get; init; }

    /// <summary>Ascending by StartTime - same ordering guarantee traces-api.ts documents.</summary>
    public List<SpanDtoWire> Spans { get; init; } = [];
}

internal static class WireJsonOptions
{
    public static readonly JsonSerializerOptions Instance = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
}
