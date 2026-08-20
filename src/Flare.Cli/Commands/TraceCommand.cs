using System.ComponentModel;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flare.Cli.Internal;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Flare.Cli.Commands;

/// <summary>
/// Renders one trace as a text waterfall via Flare.Api's <c>GET /api/traces/{traceId}</c> -
/// the CLI-native equivalent of the dashboard's trace-detail page
/// (<c>traces/[traceId]/+page.svelte</c> / <c>TraceWaterfall.svelte</c>). Same
/// hand-mirrored-DTO/no-Flare.Api-reference boundary <see cref="TracesCommand"/> already
/// draws; reuses its <c>SpanDtoWire</c>/<c>WireJsonOptions</c>/<c>FormatDurationNano</c>
/// rather than duplicating them. Tree-building and bar-positioning math are a direct port
/// of TraceWaterfall.svelte's own `rows`/`barStyle` derivations. No critical-path
/// highlighting or service-map here yet - deliberately out of scope for this first pass
/// (see docs/cli.md's traces entry).
/// </summary>
internal sealed class TraceCommand : AsyncCommand<TraceCommand.Settings>
{
    private const int LabelWidth = 46;
    private const int BarWidth = 36;

    internal sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<TRACE_ID>")]
        [Description("Exact lower-hex TraceId to render.")]
        public required string TraceId { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (!FlareHome.IsInitialized)
        {
            AnsiConsole.MarkupLine("[grey]Not initialized yet - run `flare start` first.[/]");
            return 1;
        }

        var port = FlareHome.ReadEnvValue("FLARE_API_PORT", "8080");
        using var http = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };

        TraceDtoWire? trace;
        try
        {
            using var httpResponse = await http.GetAsync($"/api/traces/{Uri.EscapeDataString(settings.TraceId)}", cancellationToken);

            if (httpResponse.StatusCode == HttpStatusCode.NotFound)
            {
                AnsiConsole.MarkupLine($"[grey]No spans found for trace {Markup.Escape(settings.TraceId)}.[/]");
                return 0;
            }

            if (!httpResponse.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[red]✗[/] GET /api/traces/{Markup.Escape(settings.TraceId)} failed: {(int)httpResponse.StatusCode} {httpResponse.ReasonPhrase}");
                return 1;
            }

            trace = await httpResponse.Content.ReadFromJsonAsync<TraceDtoWire>(WireJsonOptions.Instance, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] Couldn't reach the API on localhost:{port} - is `api` running? Check `flare status`.");
            return 1;
        }

        var spans = trace?.Spans ?? [];
        if (spans.Count == 0)
        {
            AnsiConsole.MarkupLine($"[grey]No spans found for trace {Markup.Escape(settings.TraceId)}.[/]");
            return 0;
        }

        var rows = BuildRows(spans);

        var traceStartMs = spans.Min(s => s.StartTime).ToUnixTimeMilliseconds();
        var traceEndMs = spans.Max(s => s.EndTime).ToUnixTimeMilliseconds();
        var totalMs = Math.Max(1, traceEndMs - traceStartMs);

        var serviceCount = spans.Select(s => s.ServiceName).Distinct().Count();
        var errorCount = spans.Count(s => s.StatusCode == "STATUS_CODE_ERROR");

        AnsiConsole.MarkupLine($"[bold]Trace {Markup.Escape(trace!.TraceId)}[/]");
        var errorSuffix = errorCount > 0 ? $" [red]· {errorCount} error(s)[/]" : "";
        AnsiConsole.MarkupLine(
            $"[grey]{spans.Count} span(s) · {serviceCount} service(s) · {TracesCommand.FormatDurationNano((ulong)(totalMs * 1_000_000))} total · started {spans.Min(s => s.StartTime).ToLocalTime():HH:mm:ss.fff}[/]{errorSuffix}");
        AnsiConsole.WriteLine();

        AnsiConsole.MarkupLine($"{"Span".PadRight(LabelWidth)} {BuildAxisHeader(totalMs)}");
        AnsiConsole.MarkupLine(new string('─', LabelWidth + 1 + BarWidth + 1 + 8));

        foreach (var (span, depth) in rows)
        {
            AnsiConsole.MarkupLine(BuildRowMarkup(span, depth, traceStartMs, totalMs));
        }

        return 0;
    }

    private sealed record WaterfallRow(SpanDtoWire Span, int Depth);

    /// <summary>
    /// Parent-before-children render order with a depth per row - direct port of
    /// TraceWaterfall.svelte's `rows` $derived (see its own remarks for why an
    /// unresolvable/self-referencing ParentSpanId is treated as a root rather than
    /// dropped, and why `visited` guards against a cyclic parent chain).
    /// </summary>
    private static List<WaterfallRow> BuildRows(List<SpanDtoWire> spans)
    {
        var spanIds = new HashSet<string>(spans.Select(s => s.SpanId));
        var byParent = new Dictionary<string, List<SpanDtoWire>>();
        foreach (var span in spans)
        {
            var parentKey = !string.IsNullOrEmpty(span.ParentSpanId) && spanIds.Contains(span.ParentSpanId) ? span.ParentSpanId : "";
            if (!byParent.TryGetValue(parentKey, out var siblings))
            {
                siblings = [];
                byParent[parentKey] = siblings;
            }

            siblings.Add(span);
        }

        foreach (var siblings in byParent.Values)
        {
            siblings.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));
        }

        var result = new List<WaterfallRow>();
        var visited = new HashSet<string>();

        void Visit(SpanDtoWire span, int depth)
        {
            if (!visited.Add(span.SpanId))
            {
                return;
            }

            result.Add(new WaterfallRow(span, depth));
            if (byParent.TryGetValue(span.SpanId, out var children))
            {
                foreach (var child in children)
                {
                    Visit(child, depth + 1);
                }
            }
        }

        foreach (var root in byParent.GetValueOrDefault("", []))
        {
            Visit(root, 0);
        }

        return result;
    }

    // Mirrors TraceWaterfall.svelte's KIND_LABELS, abbreviated to fit a fixed-width column.
    private static string KindTag(int kind) => kind switch
    {
        1 => "INT",
        2 => "SRV",
        3 => "CLI",
        4 => "PRD",
        5 => "CNS",
        _ => "UNS",
    };

    // Mirrors TraceWaterfall.svelte's barColorClass.
    private static string StatusColor(string statusCode) => statusCode switch
    {
        "STATUS_CODE_ERROR" => "red",
        "STATUS_CODE_OK" => "green",
        _ => "grey",
    };

    private static string BuildRowMarkup(SpanDtoWire span, int depth, long traceStartMs, long totalMs)
    {
        var indent = new string(' ', depth * 2);
        var name = string.IsNullOrEmpty(span.Name) ? "—" : span.Name;
        var plainLabel = $"{indent}{KindTag(span.Kind)} {name} · {span.ServiceName}";
        if (plainLabel.Length > LabelWidth)
        {
            plainLabel = plainLabel[..Math.Max(0, LabelWidth - 1)] + "…";
        }

        plainLabel = plainLabel.PadRight(LabelWidth);

        var color = StatusColor(span.StatusCode);
        var bar = BuildBar(span, traceStartMs, totalMs);
        var duration = TracesCommand.FormatDurationNano(span.DurationNano).PadLeft(8);

        return $"{Markup.Escape(plainLabel)} [{color}]{Markup.Escape(bar)}[/] [grey]{duration}[/]";
    }

    /// <summary>Port of TraceWaterfall.svelte's `barStyle` (left offset % / width %), quantized to <see cref="BarWidth"/> characters instead of CSS percentages.</summary>
    private static string BuildBar(SpanDtoWire span, long traceStartMs, long totalMs)
    {
        var startOffsetMs = span.StartTime.ToUnixTimeMilliseconds() - traceStartMs;
        var durationMs = Math.Max(span.EndTime.ToUnixTimeMilliseconds() - span.StartTime.ToUnixTimeMilliseconds(), 0);

        var leftFrac = (double)startOffsetMs / totalMs;
        // Same "minimum visible width" rationale as barStyle's 0.5% floor - a near-zero
        // duration span still renders at least one character instead of vanishing.
        var widthFrac = Math.Max((double)durationMs / totalMs, 1.0 / BarWidth);

        var left = Math.Clamp((int)Math.Round(leftFrac * BarWidth), 0, BarWidth - 1);
        var width = Math.Clamp((int)Math.Round(widthFrac * BarWidth), 1, BarWidth - left);

        var chars = new char[BarWidth];
        Array.Fill(chars, ' ');
        for (var i = left; i < left + width; i++)
        {
            chars[i] = '█';
        }

        return new string(chars);
    }

    /// <summary>Port of TraceWaterfall.svelte's tick-header row (0/25/50/75/100% of <paramref name="totalMs"/>), quantized to <see cref="BarWidth"/> characters.</summary>
    private static string BuildAxisHeader(long totalMs)
    {
        var chars = new char[BarWidth];
        Array.Fill(chars, ' ');

        double[] fractions = [0, 0.25, 0.5, 0.75, 1.0];
        for (var i = 0; i < fractions.Length; i++)
        {
            var label = TracesCommand.FormatDurationNano((ulong)Math.Round(fractions[i] * totalMs * 1_000_000));
            // Last tick right-aligns to the column's end (same as the CSS's -translate-x-full
            // on the final tick) so it doesn't overflow past the bar column.
            var pos = i == fractions.Length - 1
                ? Math.Max(0, BarWidth - label.Length)
                : (int)Math.Round(fractions[i] * BarWidth);

            for (var j = 0; j < label.Length && pos + j < BarWidth; j++)
            {
                chars[pos + j] = label[j];
            }
        }

        return new string(chars);
    }
}
