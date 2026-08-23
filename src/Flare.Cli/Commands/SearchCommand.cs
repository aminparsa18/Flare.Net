using System.ComponentModel;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Flare.Cli.Internal;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Flare.Cli.Commands;

/// <summary>
/// One-shot log search via Flare.Api's <c>POST /api/logs/search</c> - the CLI-native
/// equivalent of the dashboard's Logs Explorer, same relationship <see cref="TracesCommand"/>
/// has to the Trace List. Hand-rolled against the documented wire contract (see
/// <c>src/Flare.Api/Model/LogFilter.cs</c>/<c>LogSearchRequest.cs</c>/<c>LogEventDto.cs</c>)
/// rather than a project reference on Flare.Api - same boundary <see cref="TracesCommand"/>
/// and <see cref="LogTailClient"/> already draw. No live-tail counterpart here - that's
/// `flare tail`; this is the one-shot query equivalent, same pairing `traces`/no-live-tail
/// already establishes. Attribute filters (<c>LogFilter.Attributes</c>) aren't exposed yet -
/// planned as a follow-up, same posture <see cref="TraceCommand"/> takes on critical-path
/// highlighting.
/// </summary>
internal sealed class SearchCommand : AsyncCommand<SearchCommand.Settings>
{
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
        [Description("Exact Drain cluster id match (see the dashboard's Patterns view).")]
        public string? PatternId { get; init; }

        [CommandOption("--search <TEXT>")]
        [Description("Case-insensitive substring match against the log body.")]
        public string? Search { get; init; }

        [CommandOption("--since <RANGE>")]
        [Description("How far back to search: 15m, 1h, 6h, 24h, 7d. Default 1h.")]
        public string Since { get; init; } = "1h";

        [CommandOption("--limit <COUNT>")]
        [Description("Max events to print. Default 20.")]
        public int Limit { get; init; } = 20;
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var instance = FlareHome.ResolveTarget(settings.InstanceName);

        if (!instance.IsInitialized)
        {
            AnsiConsole.MarkupLine($"[grey]Not initialized yet - run `{instance.StartHint}` first.[/]");
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

        LogSearchResponseWire? response;
        try
        {
            using var httpResponse = await http.PostAsJsonAsync(
                "/api/logs/search",
                new LogSearchRequestWire { Filter = filter, PageSize = Math.Clamp(settings.Limit, 1, 500) },
                WireJsonOptions.Instance,
                cancellationToken);

            if (!httpResponse.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[red]✗[/] POST /api/logs/search failed: {(int)httpResponse.StatusCode} {httpResponse.ReasonPhrase}");
                return 1;
            }

            response = await httpResponse.Content.ReadFromJsonAsync<LogSearchResponseWire>(WireJsonOptions.Instance, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] Couldn't reach the API on localhost:{port} - is `api` running? Check `flare status`.");
            return 1;
        }

        var events = response?.Events ?? [];
        if (events.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]No log events match the current filters.[/]");
            return 0;
        }

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Time");
        table.AddColumn("Level");
        table.AddColumn("Service");
        table.AddColumn("Message");
        table.AddColumn("Trace ID");

        foreach (var e in events.Take(settings.Limit))
        {
            var time = e.Timestamp.ToLocalTime().ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
            var label = SeverityLevels.LabelFor(e.SeverityNumber);
            var color = SeverityLevels.ColorFor(e.SeverityNumber);

            table.AddRow(
                time,
                $"[{color}]{label}[/]",
                Markup.Escape(e.ServiceName),
                Markup.Escape(Truncate(e.Body, 80)),
                $"[grey]{Markup.Escape(e.TraceId)}[/]");
        }

        AnsiConsole.Write(table);

        if (response?.NextCursor is not null)
        {
            AnsiConsole.MarkupLine($"[grey]… more available - narrow --since/--service or raise --limit (shown: {Math.Min(events.Count, settings.Limit)}).[/]");
        }

        return 0;
    }

    private static string Truncate(string text, int maxLength) =>
        text.Length <= maxLength ? text : string.Concat(text.AsSpan(0, maxLength - 1), "…");
}

// ---- Wire DTOs - hand-mirror of Flare.Api's Model/LogFilter.cs, Model/LogSearchRequest.cs,
// Model/LogEventDto.cs (see LogsJsonContext's camelCase-properties convention). Kept
// `internal` (not `private`) so ExportCommand reuses these directly rather than
// duplicating them - same reuse ExportCommand/TraceCommand already establish for their
// own sibling command's DTOs. Keep in sync with those files by hand, same as
// dashboard/src/lib/logs-api.ts already does from the TypeScript side. -------------

internal sealed class LogFilterWire
{
    public DateTimeOffset? From { get; init; }

    public DateTimeOffset? To { get; init; }

    public IReadOnlyList<string>? Services { get; init; }

    public IReadOnlyList<byte>? SeverityNumbers { get; init; }

    public string? TraceId { get; init; }

    public string? SpanId { get; init; }

    public string? PatternId { get; init; }

    public string? Search { get; init; }
}

internal sealed class LogSearchRequestWire
{
    public LogFilterWire? Filter { get; init; }

    public string? Cursor { get; init; }

    public int? PageSize { get; init; }
}

/// <summary>
/// Full field set mirroring <c>LogEventDto</c>, even though <see cref="SearchCommand"/>'s
/// own table only renders a subset - <see cref="ExportCommand"/> needs the rest
/// (EventName/LogAttributes/etc.) and reuses this same type rather than a second
/// near-duplicate DTO.
/// </summary>
internal sealed class LogEventDtoWire
{
    public required Guid EventId { get; init; }

    public required DateTimeOffset Timestamp { get; init; }

    public required string SeverityText { get; init; }

    public required byte SeverityNumber { get; init; }

    public required string ServiceName { get; init; }

    public required string Body { get; init; }

    public string TraceId { get; init; } = "";

    public string SpanId { get; init; } = "";

    public string EventName { get; init; } = "";

    public Dictionary<string, string> LogAttributes { get; init; } = [];
}

internal sealed class LogSearchResponseWire
{
    public List<LogEventDtoWire> Events { get; init; } = [];

    public string? NextCursor { get; init; }
}

// No local WireJsonOptions here - reuses TracesCommand's internal
// Flare.Cli.Commands.WireJsonOptions (identical JsonSerializerDefaults.Web + camelCase
// shape), same reuse TraceCommand already makes of TracesCommand's DTOs/helpers.
