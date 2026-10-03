using Flare.Mcp;
using System.ComponentModel;
using System.Net.Http.Json;
using System.Text.Json;
using Flare.Cli.Internal;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Flare.Cli.Commands;

/// <summary>
/// Lists discoverable metrics via Flare.Api's <c>POST /api/metrics/names</c> - the
/// CLI-native equivalent of the dashboard's Metric Picker sidebar
/// (<c>metrics/+page.svelte</c> / <c>MetricPicker.svelte</c>). Same hand-mirrored-DTO/
/// no-Flare.Api-reference boundary <see cref="TracesCommand"/> already draws (see
/// <c>src/Flare.Api/Model/MetricModels.cs</c>/<c>Json/MetricsJsonContext.cs</c>).
/// </summary>
internal sealed class MetricsCommand : AsyncCommand<MetricsCommand.Settings>
{
    internal sealed class Settings : InstanceSettings
    {
        [CommandOption("-s|--service <NAME>")]
        [Description("Filter by exact service name. Repeatable.")]
        public string[] Service { get; init; } = [];

        [CommandOption("--since <RANGE>")]
        [Description("How far back to look for metrics: 15m, 1h, 6h, 24h, 7d. Default 1h.")]
        public string Since { get; init; } = "1h";

        [CommandOption("--limit <COUNT>")]
        [Description("Max metrics to print. Default 50.")]
        public int Limit { get; init; } = 50;
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var instance = FlareHome.ResolveTarget(settings.InstanceName);

        if (!instance.IsInitialized)
        {
            AnsiConsole.MarkupLine($"[grey]Not initialized yet - run `{instance.StartHint}` first.[/]");
            return 1;
        }

        if (!TracesCommand.TryParseSince(settings.Since, out var since))
        {
            AnsiConsole.MarkupLine($"[red]✗[/] Couldn't parse --since '{Markup.Escape(settings.Since)}' - expected e.g. 15m, 1h, 6h, 24h, 7d.");
            return 1;
        }

        var to = DateTimeOffset.UtcNow;
        var from = to - since;

        var port = instance.ReadEnvValue("FLARE_API_PORT", "8080");
        using var http = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };

        MetricNamesResponseWire? response;
        try
        {
            using var httpResponse = await http.PostAsJsonAsync(
                "/api/metrics/names",
                new MetricNamesRequestWire { From = from, To = to, Services = settings.Service.Length > 0 ? settings.Service : null },
                MetricsWireJsonOptions.Instance,
                cancellationToken);

            if (!httpResponse.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[red]✗[/] POST /api/metrics/names failed: {(int)httpResponse.StatusCode} {httpResponse.ReasonPhrase}");
                return 1;
            }

            response = await httpResponse.Content.ReadFromJsonAsync<MetricNamesResponseWire>(MetricsWireJsonOptions.Instance, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] Couldn't reach the API on localhost:{port} - is `api` running? Check `flare status`.");
            return 1;
        }

        var metrics = response?.Metrics ?? [];
        if (metrics.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]No metrics found for the current filters.[/]");
            return 0;
        }

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Name");
        table.AddColumn("Service");
        table.AddColumn("Type");
        table.AddColumn("Unit");
        table.AddColumn(new TableColumn("Series").RightAligned());

        foreach (var metric in metrics.Take(settings.Limit))
        {
            table.AddRow(
                Markup.Escape(metric.MetricName),
                Markup.Escape(metric.ServiceName),
                MetricTypeMarkup(metric.Type),
                Markup.Escape(metric.Unit ?? ""),
                metric.SeriesCount.ToString());
        }

        AnsiConsole.Write(table);

        if (metrics.Count > settings.Limit)
        {
            AnsiConsole.MarkupLine($"[grey]… {metrics.Count - settings.Limit} more not shown - narrow --service/--since or raise --limit.[/]");
        }

        return 0;
    }

    // Mirrors MetricPicker.svelte's TYPE_BADGE_VARIANT, translated to a terminal color.
    private static string MetricTypeMarkup(string type) => type switch
    {
        "Gauge" => "[cyan]Gauge[/]",
        "Sum" => "[yellow]Sum[/]",
        "Histogram" => "[magenta]Histogram[/]",
        "ExponentialHistogram" => "[magenta]Exp. Histogram[/]",
        _ => Markup.Escape(type),
    };
}

// ---- Wire DTOs - hand-mirror of Flare.Api's Model/MetricModels.cs (see
// MetricsJsonContext's camelCase-properties/PascalCase-string-enum-values convention -
// MetricPointType serializes as "Gauge"/"Sum"/"Histogram"/"ExponentialHistogram" verbatim). Kept as plain
// strings here (not a C# enum) for the same reason SpanDtoWire.StatusCode is a string -
// this file never needs to do anything with a type value beyond compare/display it.
// Shared by MetricsCommand and MetricCommand (metrics/names + metrics/query DTOs live
// together since MetricCommand's own name-resolution step also calls /api/metrics/names).









