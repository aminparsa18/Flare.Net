using Flare.Mcp;
using System.ComponentModel;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flare.Cli.Internal;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Flare.Cli.Commands;

/// <summary>
/// <c>flare synthetic-monitors list/create/update/delete</c> - CRUD for scheduled probes via Flare.Api's
/// <c>/api/synthetic-monitors</c> (<c>src/Flare.Api/Endpoints/SyntheticMonitorEndpoints.cs</c>). The list shows
/// each monitor's latest result. See <c>docs-internal/adr/0128-synthetic-monitoring.md</c>.
/// </summary>
internal sealed class SyntheticMonitorsListCommand : AsyncCommand<SyntheticMonitorsListCommand.Settings>
{
    internal sealed class Settings : InstanceSettings
    {
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var instance = FlareHome.ResolveTarget(settings.InstanceName);
        if (!instance.IsInitialized)
        {
            AnsiConsole.MarkupLine($"[grey]Not initialized yet - run `{instance.StartHint}` first.[/]");
            return 1;
        }

        var port = instance.ReadEnvValue("FLARE_API_PORT", "8080");
        using var http = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };

        SyntheticMonitorListResponseWire? response;
        try
        {
            using var httpResponse = await http.GetAsync("/api/synthetic-monitors", cancellationToken);
            if (!httpResponse.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[red]✗[/] GET /api/synthetic-monitors failed: {(int)httpResponse.StatusCode} {httpResponse.ReasonPhrase}");
                return 1;
            }

            response = await httpResponse.Content.ReadFromJsonAsync<SyntheticMonitorListResponseWire>(WireJsonOptions.Instance, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] Couldn't reach the API on localhost:{port} - is `api` running? Check `flare status`.");
            return 1;
        }

        var monitors = response?.Monitors ?? [];
        if (monitors.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]No synthetic monitors configured.[/]");
            return 0;
        }

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Name");
        table.AddColumn("Type");
        table.AddColumn("Target");
        table.AddColumn("Every");
        table.AddColumn("Enabled");
        table.AddColumn("Locations");
        table.AddColumn("Latest");
        table.AddColumn("Id");

        foreach (var monitor in monitors)
        {
            table.AddRow(
                Markup.Escape(monitor.Name),
                monitor.Kind,
                Markup.Escape(monitor.Target),
                SyntheticMonitorFormat.Interval(monitor.IntervalSeconds),
                monitor.Enabled ? "yes" : "[grey]no[/]",
                monitor.Locations.Count == 0 ? "[grey]all[/]" : Markup.Escape(string.Join(", ", monitor.Locations)),
                SyntheticMonitorFormat.Latest(monitor),
                $"[grey]{monitor.Id}[/]");
        }

        AnsiConsole.Write(table);
        return 0;
    }
}

/// <summary>
/// <c>flare synthetic-monitors create &lt;NAME&gt; --target &lt;TARGET&gt;</c> via <c>POST /api/synthetic-monitors</c>.
/// Validation (target shape per kind, interval/timeout bounds) is the API's; its 400 detail is surfaced as is.
/// </summary>
internal sealed class SyntheticMonitorsCreateCommand : AsyncCommand<SyntheticMonitorsCreateCommand.Settings>
{
    internal sealed class Settings : InstanceSettings
    {
        [CommandArgument(0, "<NAME>")]
        [Description("A label for this monitor, e.g. 'checkout-health'. Also the `monitor` attribute on its metrics.")]
        public required string Name { get; init; }

        [CommandOption("--target <TARGET>")]
        [Description("Http: an absolute http(s) URL. Tcp: host:port. Tls: host or host:port. Required.")]
        public string? Target { get; init; }

        [CommandOption("--kind <KIND>")]
        [Description("http, tcp, or tls. Defaults to http.")]
        public string? Kind { get; init; }

        [CommandOption("--description <DESCRIPTION>")]
        public string? Description { get; init; }

        [CommandOption("--method <METHOD>")]
        [Description("Http only: GET, HEAD, POST or OPTIONS. Defaults to GET.")]
        public string? Method { get; init; }

        [CommandOption("--expected-status <CODE>")]
        [Description("Http only: the status that counts as up. 0 (default) means any 2xx or 3xx.")]
        public int? ExpectedStatus { get; init; }

        [CommandOption("--header <HEADER>")]
        [Description("Http only: a request header as 'Name: value'. Repeat for several. On update, replaces all existing headers.")]
        public string[]? Headers { get; init; }

        [CommandOption("--body <BODY>")]
        [Description("Http POST only: the request body.")]
        public string? Body { get; init; }

        [CommandOption("--body-contains <TEXT>")]
        [Description("Http only: up only when the response body contains this text (case-sensitive).")]
        public string? BodyContains { get; init; }

        [CommandOption("--body-not-contains <TEXT>")]
        [Description("Http only: up only when the response body does not contain this text.")]
        public string? BodyNotContains { get; init; }

        [CommandOption("--location <LOCATION>")]
        [Description("A probe location that runs this monitor (matches a worker's Synthetic:Location). Repeat for several; none means every location. On update, replaces all existing locations.")]
        public string[]? Locations { get; init; }

        [CommandOption("--interval <SECONDS>")]
        [Description("Seconds between probes, 10 to 86400. Defaults to 60.")]
        public int? IntervalSeconds { get; init; }

        [CommandOption("--timeout <SECONDS>")]
        [Description("Seconds before a probe counts as failed, 1 to 120, not above the interval. Defaults to 10.")]
        public int? TimeoutSeconds { get; init; }

        [CommandOption("--enabled <BOOL>")]
        [Description("true or false. Defaults to true.")]
        public bool? Enabled { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var instance = FlareHome.ResolveTarget(settings.InstanceName);
        if (!instance.IsInitialized)
        {
            AnsiConsole.MarkupLine($"[grey]Not initialized yet - run `{instance.StartHint}` first.[/]");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(settings.Name) || string.IsNullOrWhiteSpace(settings.Target))
        {
            AnsiConsole.MarkupLine("[red]✗[/] NAME and --target are required.");
            return 1;
        }

        string? kind = null;
        if (settings.Kind is not null && (kind = SyntheticMonitorFormat.NormalizeKind(settings.Kind)) is null)
        {
            AnsiConsole.MarkupLine("[red]✗[/] --kind must be one of: http, tcp, tls.");
            return 1;
        }

        var request = new SyntheticMonitorRequestWire
        {
            Name = settings.Name,
            Target = settings.Target,
            Description = settings.Description,
            Kind = kind,
            Method = settings.Method,
            ExpectedStatus = settings.ExpectedStatus,
            RequestHeaders = settings.Headers is { Length: > 0 } ? string.Join('\n', settings.Headers) : null,
            RequestBody = settings.Body,
            BodyContains = settings.BodyContains,
            BodyNotContains = settings.BodyNotContains,
            Locations = settings.Locations is { Length: > 0 } ? settings.Locations : null,
            IntervalSeconds = settings.IntervalSeconds,
            TimeoutSeconds = settings.TimeoutSeconds,
            Enabled = settings.Enabled,
        };

        var port = instance.ReadEnvValue("FLARE_API_PORT", "8080");
        using var http = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };

        SyntheticMonitorWire? monitor;
        try
        {
            using var httpResponse = await http.PostAsJsonAsync("/api/synthetic-monitors", request, WireJsonOptions.Instance, cancellationToken);
            if (!httpResponse.IsSuccessStatusCode)
            {
                var detail = await NotificationChannelTypeParsing.TryReadProblemDetailAsync(httpResponse, cancellationToken);
                AnsiConsole.MarkupLine($"[red]✗[/] POST /api/synthetic-monitors failed: {(int)httpResponse.StatusCode} {Markup.Escape(detail ?? httpResponse.ReasonPhrase ?? "")}");
                return 1;
            }

            monitor = await httpResponse.Content.ReadFromJsonAsync<SyntheticMonitorWire>(WireJsonOptions.Instance, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] Couldn't reach the API on localhost:{port} - is `api` running? Check `flare status`.");
            return 1;
        }

        if (monitor is null)
        {
            AnsiConsole.MarkupLine("[red]✗[/] Empty response from /api/synthetic-monitors.");
            return 1;
        }

        AnsiConsole.MarkupLine($"[green]✓[/] Created {monitor.Kind} monitor [bold]{Markup.Escape(monitor.Name)}[/] (id: {monitor.Id}).");
        return 0;
    }
}

/// <summary>
/// <c>flare synthetic-monitors update &lt;ID&gt; [options]</c> via <c>PUT /api/synthetic-monitors/{id}</c>, which
/// replaces the whole monitor. Fetches the existing one first and overrides only the options passed, so
/// `update &lt;ID&gt; --enabled false` pauses a monitor without re-typing the rest.
/// </summary>
internal sealed class SyntheticMonitorsUpdateCommand : AsyncCommand<SyntheticMonitorsUpdateCommand.Settings>
{
    internal sealed class Settings : InstanceSettings
    {
        [CommandArgument(0, "<ID>")]
        [Description("The monitor's id (see `flare synthetic-monitors list`).")]
        public required Guid Id { get; init; }

        [CommandOption("--name <NAME>")]
        public string? Name { get; init; }

        [CommandOption("--target <TARGET>")]
        public string? Target { get; init; }

        [CommandOption("--kind <KIND>")]
        [Description("http, tcp, or tls.")]
        public string? Kind { get; init; }

        [CommandOption("--description <DESCRIPTION>")]
        public string? Description { get; init; }

        [CommandOption("--method <METHOD>")]
        public string? Method { get; init; }

        [CommandOption("--expected-status <CODE>")]
        public int? ExpectedStatus { get; init; }

        [CommandOption("--header <HEADER>")]
        [Description("Http only: a request header as 'Name: value'. Repeat for several. On update, replaces all existing headers.")]
        public string[]? Headers { get; init; }

        [CommandOption("--body <BODY>")]
        [Description("Http POST only: the request body.")]
        public string? Body { get; init; }

        [CommandOption("--body-contains <TEXT>")]
        [Description("Http only: up only when the response body contains this text (case-sensitive).")]
        public string? BodyContains { get; init; }

        [CommandOption("--body-not-contains <TEXT>")]
        [Description("Http only: up only when the response body does not contain this text.")]
        public string? BodyNotContains { get; init; }

        [CommandOption("--location <LOCATION>")]
        [Description("A probe location that runs this monitor (matches a worker's Synthetic:Location). Repeat for several; none means every location. On update, replaces all existing locations.")]
        public string[]? Locations { get; init; }

        [CommandOption("--interval <SECONDS>")]
        public int? IntervalSeconds { get; init; }

        [CommandOption("--timeout <SECONDS>")]
        public int? TimeoutSeconds { get; init; }

        [CommandOption("--enabled <BOOL>")]
        [Description("true or false.")]
        public bool? Enabled { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var instance = FlareHome.ResolveTarget(settings.InstanceName);
        if (!instance.IsInitialized)
        {
            AnsiConsole.MarkupLine($"[grey]Not initialized yet - run `{instance.StartHint}` first.[/]");
            return 1;
        }

        string? kind = null;
        if (settings.Kind is not null && (kind = SyntheticMonitorFormat.NormalizeKind(settings.Kind)) is null)
        {
            AnsiConsole.MarkupLine("[red]✗[/] --kind must be one of: http, tcp, tls.");
            return 1;
        }

        var port = instance.ReadEnvValue("FLARE_API_PORT", "8080");
        using var http = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };

        try
        {
            using var getResponse = await http.GetAsync($"/api/synthetic-monitors/{settings.Id}", cancellationToken);
            if (getResponse.StatusCode == HttpStatusCode.NotFound)
            {
                AnsiConsole.MarkupLine($"[red]✗[/] No synthetic monitor with id {settings.Id}.");
                return 1;
            }

            if (!getResponse.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[red]✗[/] GET /api/synthetic-monitors/{settings.Id} failed: {(int)getResponse.StatusCode} {getResponse.ReasonPhrase}");
                return 1;
            }

            var existing = await getResponse.Content.ReadFromJsonAsync<SyntheticMonitorWire>(WireJsonOptions.Instance, cancellationToken);
            if (existing is null)
            {
                AnsiConsole.MarkupLine("[red]✗[/] Empty response from /api/synthetic-monitors.");
                return 1;
            }

            var request = new SyntheticMonitorRequestWire
            {
                Name = settings.Name ?? existing.Name,
                Target = settings.Target ?? existing.Target,
                Description = settings.Description ?? existing.Description,
                Kind = kind ?? existing.Kind,
                Method = settings.Method ?? existing.Method,
                ExpectedStatus = settings.ExpectedStatus ?? existing.ExpectedStatus,
                RequestHeaders = settings.Headers is { Length: > 0 } ? string.Join('\n', settings.Headers) : existing.RequestHeaders,
                RequestBody = settings.Body ?? existing.RequestBody,
                BodyContains = settings.BodyContains ?? existing.BodyContains,
                BodyNotContains = settings.BodyNotContains ?? existing.BodyNotContains,
                Locations = settings.Locations is { Length: > 0 } ? settings.Locations : existing.Locations,
                IntervalSeconds = settings.IntervalSeconds ?? existing.IntervalSeconds,
                TimeoutSeconds = settings.TimeoutSeconds ?? existing.TimeoutSeconds,
                Enabled = settings.Enabled ?? existing.Enabled,
            };

            using var putResponse = await http.PutAsJsonAsync($"/api/synthetic-monitors/{settings.Id}", request, WireJsonOptions.Instance, cancellationToken);
            if (!putResponse.IsSuccessStatusCode)
            {
                var detail = await NotificationChannelTypeParsing.TryReadProblemDetailAsync(putResponse, cancellationToken);
                AnsiConsole.MarkupLine($"[red]✗[/] PUT /api/synthetic-monitors/{settings.Id} failed: {(int)putResponse.StatusCode} {Markup.Escape(detail ?? putResponse.ReasonPhrase ?? "")}");
                return 1;
            }

            AnsiConsole.MarkupLine($"[green]✓[/] Updated monitor [bold]{Markup.Escape(request.Name)}[/].");
            return 0;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] Couldn't reach the API on localhost:{port} - is `api` running? Check `flare status`.");
            return 1;
        }
    }
}

/// <summary><c>flare synthetic-monitors delete &lt;ID&gt;</c> via <c>DELETE /api/synthetic-monitors/{id}</c>.</summary>
internal sealed class SyntheticMonitorsDeleteCommand : AsyncCommand<SyntheticMonitorsDeleteCommand.Settings>
{
    internal sealed class Settings : InstanceSettings
    {
        [CommandArgument(0, "<ID>")]
        [Description("The monitor's id (see `flare synthetic-monitors list`).")]
        public required Guid Id { get; init; }

        [CommandOption("-y|--yes")]
        [Description("Skip the interactive confirmation prompt. Required for non-interactive use.")]
        public bool Yes { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var instance = FlareHome.ResolveTarget(settings.InstanceName);
        if (!instance.IsInitialized)
        {
            AnsiConsole.MarkupLine($"[grey]Not initialized yet - run `{instance.StartHint}` first.[/]");
            return 1;
        }

        if (!settings.Yes)
        {
            if (!AnsiConsole.Profile.Capabilities.Interactive)
            {
                AnsiConsole.MarkupLine("[red]Refusing to delete without --yes on a non-interactive invocation.[/]");
                return 1;
            }

            if (!AnsiConsole.Confirm($"Delete synthetic monitor [bold]{settings.Id}[/]? Its probes stop; stored results stay. Continue?", defaultValue: false))
            {
                AnsiConsole.MarkupLine("[grey]Aborted - nothing was removed.[/]");
                return 1;
            }
        }

        var port = instance.ReadEnvValue("FLARE_API_PORT", "8080");
        using var http = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
        try
        {
            using var response = await http.DeleteAsync($"/api/synthetic-monitors/{settings.Id}", cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                AnsiConsole.MarkupLine($"[red]✗[/] No synthetic monitor with id {settings.Id}.");
                return 1;
            }

            if (!response.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[red]✗[/] DELETE /api/synthetic-monitors/{settings.Id} failed: {(int)response.StatusCode} {response.ReasonPhrase}");
                return 1;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] Couldn't reach the API on localhost:{port} - is `api` running? Check `flare status`.");
            return 1;
        }

        AnsiConsole.MarkupLine($"[green]✓[/] Deleted monitor {settings.Id}.");
        return 0;
    }
}

internal static class SyntheticMonitorFormat
{
    public static string? NormalizeKind(string kind) => kind.Trim().ToLowerInvariant() switch
    {
        "http" => "Http",
        "tcp" => "Tcp",
        "tls" => "Tls",
        _ => null,
    };

    public static string Interval(int seconds) => seconds switch
    {
        _ when seconds % 3600 == 0 => $"{seconds / 3600} h",
        _ when seconds % 60 == 0 => $"{seconds / 60} min",
        _ => $"{seconds} s",
    };

    public static string Latest(SyntheticMonitorWire monitor)
    {
        // Several locations: one entry each ("eu up 42 ms, us down"), so a regional outage is visible.
        if (monitor.LocationStatuses.Count > 1)
        {
            return string.Join(", ", monitor.LocationStatuses.Select(l => $"{Markup.Escape(l.Location)} {Status(l.Status)}"));
        }

        return Status(monitor.Latest);
    }

    private static string Status(SyntheticMonitorStatusWire? latest)
    {
        if (latest is null)
        {
            return "[grey]no data[/]";
        }

        var state = latest.Up ? "[green]up[/]" : "[red]down[/]";
        return latest.DurationMs is { } ms ? $"{state} {Math.Round(ms)} ms" : state;
    }
}

// ---- Wire DTOs - hand-mirror of Flare.Api's Model/SyntheticMonitorModels.cs (camelCase JSON; enums as strings). ----

internal sealed class SyntheticMonitorStatusWire
{
    public bool Up { get; init; }

    public double? DurationMs { get; init; }
}

internal sealed class SyntheticMonitorWire
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public string Description { get; init; } = "";

    public bool Enabled { get; init; } = true;

    public string Kind { get; init; } = "Http";

    public required string Target { get; init; }

    public string Method { get; init; } = "GET";

    public int ExpectedStatus { get; init; }

    public string RequestHeaders { get; init; } = "";

    public string RequestBody { get; init; } = "";

    public string BodyContains { get; init; } = "";

    public string BodyNotContains { get; init; } = "";

    public int IntervalSeconds { get; init; } = 60;

    public int TimeoutSeconds { get; init; } = 10;

    public List<string> Locations { get; init; } = [];

    public SyntheticMonitorStatusWire? Latest { get; init; }

    public List<SyntheticLocationStatusWire> LocationStatuses { get; init; } = [];
}

internal sealed class SyntheticLocationStatusWire
{
    public string Location { get; init; } = "";

    public SyntheticMonitorStatusWire? Status { get; init; }
}

internal sealed class SyntheticMonitorListResponseWire
{
    public List<SyntheticMonitorWire> Monitors { get; init; } = [];
}

internal sealed class SyntheticMonitorRequestWire
{
    public required string Name { get; init; }

    public required string Target { get; init; }

    public string? Description { get; init; }

    public string? Kind { get; init; }

    public string? Method { get; init; }

    public int? ExpectedStatus { get; init; }

    public string? RequestHeaders { get; init; }

    public string? RequestBody { get; init; }

    public string? BodyContains { get; init; }

    public string? BodyNotContains { get; init; }

    public int? IntervalSeconds { get; init; }

    public int? TimeoutSeconds { get; init; }

    public IReadOnlyList<string>? Locations { get; init; }

    public bool? Enabled { get; init; }
}
