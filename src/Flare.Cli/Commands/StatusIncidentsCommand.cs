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
/// <c>flare status-pages incidents list/open/update/delete</c> - the written incidents on a status page via
/// Flare.Api's <c>/api/status-pages/{id}/incidents</c> (<c>src/Flare.Api/Endpoints/StatusPageEndpoints.cs</c>).
/// See <c>docs-internal/adr/0159-status-page-incidents.md</c> and <c>0160-status-incident-components.md</c>.
/// </summary>
internal static class StatusIncidentCli
{
    public static readonly string[] Statuses = ["Investigating", "Identified", "Monitoring", "Resolved"];

    /// <summary>Normalises a user-typed status to its canonical casing; null when it is not one.</summary>
    public static string? NormalizeStatus(string? value) =>
        Statuses.FirstOrDefault(s => string.Equals(s, value?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Sends one request to the instance's API and prints failures. Returns the response body deserialized as
    /// <typeparamref name="T"/> (default for an empty body), or sets <paramref name="exit"/> non-zero on failure.
    /// </summary>
    public static async Task<(T? Body, int Exit)> SendAsync<T>(string? instanceName, HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        var instance = FlareHome.ResolveTarget(instanceName);
        if (!instance.IsInitialized)
        {
            AnsiConsole.MarkupLine($"[grey]Not initialized yet - run `{instance.StartHint}` first.[/]");
            return (default, 1);
        }

        var port = instance.ReadEnvValue("FLARE_API_PORT", "8080");
        using var http = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
        try
        {
            using var request = new HttpRequestMessage(method, path);
            if (body is not null)
            {
                request.Content = JsonContent.Create(body, options: WireJsonOptions.Instance);
            }

            using var response = await http.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                AnsiConsole.MarkupLine($"[red]✗[/] Not found: {Markup.Escape(path)} (check the page and incident ids).");
                return (default, 1);
            }

            if (!response.IsSuccessStatusCode)
            {
                var detail = await NotificationChannelTypeParsing.TryReadProblemDetailAsync(response, cancellationToken);
                AnsiConsole.MarkupLine($"[red]✗[/] {method} {Markup.Escape(path)} failed: {(int)response.StatusCode} {Markup.Escape(detail ?? response.ReasonPhrase ?? "")}");
                return (default, 1);
            }

            if (response.StatusCode == HttpStatusCode.NoContent)
            {
                return (default, 0);
            }

            return (await response.Content.ReadFromJsonAsync<T>(WireJsonOptions.Instance, cancellationToken), 0);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] Couldn't reach the API on localhost:{port} - is `api` running? Check `flare status`.");
            return (default, 1);
        }
    }
}

/// <summary><c>flare status-pages incidents list &lt;PAGE_ID&gt;</c>.</summary>
internal sealed class StatusIncidentsListCommand : AsyncCommand<StatusIncidentsListCommand.Settings>
{
    internal sealed class Settings : InstanceSettings
    {
        [CommandArgument(0, "<PAGE_ID>")]
        [Description("The page's id (see `flare status-pages list`).")]
        public required Guid PageId { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var (response, exit) = await StatusIncidentCli.SendAsync<StatusIncidentListResponseWire>(
            settings.InstanceName, HttpMethod.Get, $"/api/status-pages/{settings.PageId}/incidents", null, cancellationToken);
        if (exit != 0)
        {
            return exit;
        }

        var incidents = response?.Incidents ?? [];
        if (incidents.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]No incidents on this page.[/]");
            return 0;
        }

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Title");
        table.AddColumn("Status");
        table.AddColumn("Updates");
        table.AddColumn("Components");
        table.AddColumn("Id");
        foreach (var incident in incidents)
        {
            table.AddRow(
                Markup.Escape(incident.Title),
                incident.Status == "Resolved" ? "[grey]Resolved[/]" : $"[yellow]{Markup.Escape(incident.Status)}[/]",
                incident.Updates.Count.ToString(),
                incident.Components.Count.ToString(),
                $"[grey]{incident.Id}[/]");
        }

        AnsiConsole.Write(table);
        return 0;
    }
}

/// <summary><c>flare status-pages incidents open &lt;PAGE_ID&gt; --title T --message M</c>.</summary>
internal sealed class StatusIncidentsOpenCommand : AsyncCommand<StatusIncidentsOpenCommand.Settings>
{
    internal sealed class Settings : InstanceSettings
    {
        [CommandArgument(0, "<PAGE_ID>")]
        [Description("The page's id (see `flare status-pages list`).")]
        public required Guid PageId { get; init; }

        [CommandOption("--title <TITLE>")]
        [Description("What is happening. Required.")]
        public string? Title { get; init; }

        [CommandOption("--message <MESSAGE>")]
        [Description("The first update's text, shown publicly. Required.")]
        public string? Message { get; init; }

        [CommandOption("--status <STATUS>")]
        [Description("Investigating (default), Identified, Monitoring or Resolved.")]
        public string? Status { get; init; }

        [CommandOption("--component <ID>")]
        [Description("The monitor or SLO id of a page component this affects. Repeat for several.")]
        public Guid[]? Components { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.Title) || string.IsNullOrWhiteSpace(settings.Message))
        {
            AnsiConsole.MarkupLine("[red]✗[/] --title and --message are required.");
            return 1;
        }

        string? status = null;
        if (settings.Status is not null && (status = StatusIncidentCli.NormalizeStatus(settings.Status)) is null)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] --status must be one of {string.Join(", ", StatusIncidentCli.Statuses)}.");
            return 1;
        }

        var request = new StatusIncidentRequestWire { Title = settings.Title, Message = settings.Message, Status = status, Components = settings.Components?.ToList() };
        var (incident, exit) = await StatusIncidentCli.SendAsync<StatusIncidentWire>(
            settings.InstanceName, HttpMethod.Post, $"/api/status-pages/{settings.PageId}/incidents", request, cancellationToken);
        if (exit != 0)
        {
            return exit;
        }

        AnsiConsole.MarkupLine($"[green]✓[/] Opened incident [bold]{Markup.Escape(incident?.Title ?? settings.Title)}[/] (id: {incident?.Id}).");
        return 0;
    }
}

/// <summary><c>flare status-pages incidents update &lt;PAGE_ID&gt; &lt;INCIDENT_ID&gt; --status S --message M</c>.</summary>
internal sealed class StatusIncidentsUpdateCommand : AsyncCommand<StatusIncidentsUpdateCommand.Settings>
{
    internal sealed class Settings : InstanceSettings
    {
        [CommandArgument(0, "<PAGE_ID>")]
        [Description("The page's id (see `flare status-pages list`).")]
        public required Guid PageId { get; init; }

        [CommandArgument(1, "<INCIDENT_ID>")]
        [Description("The incident's id (see `flare status-pages incidents list`).")]
        public required Guid IncidentId { get; init; }

        [CommandOption("--status <STATUS>")]
        [Description("Investigating, Identified, Monitoring or Resolved. Required.")]
        public string? Status { get; init; }

        [CommandOption("--message <MESSAGE>")]
        [Description("The update's text, shown publicly. Required.")]
        public string? Message { get; init; }

        [CommandOption("--component <ID>")]
        [Description("Replaces the affected components with these monitor or SLO ids. Repeat for several; omit to leave them unchanged.")]
        public Guid[]? Components { get; init; }

        [CommandOption("--clear-components")]
        [Description("Clears the affected components.")]
        public bool ClearComponents { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var status = StatusIncidentCli.NormalizeStatus(settings.Status);
        if (status is null || string.IsNullOrWhiteSpace(settings.Message))
        {
            AnsiConsole.MarkupLine($"[red]✗[/] --status ({string.Join(", ", StatusIncidentCli.Statuses)}) and --message are required.");
            return 1;
        }

        if (settings.ClearComponents && settings.Components is { Length: > 0 })
        {
            AnsiConsole.MarkupLine("[red]✗[/] Pass --component or --clear-components, not both.");
            return 1;
        }

        var components = settings.ClearComponents ? [] : settings.Components?.ToList();
        var request = new StatusIncidentUpdateRequestWire { Status = status, Message = settings.Message, Components = components };
        var (_, exit) = await StatusIncidentCli.SendAsync<StatusIncidentWire>(
            settings.InstanceName, HttpMethod.Post, $"/api/status-pages/{settings.PageId}/incidents/{settings.IncidentId}/updates", request, cancellationToken);
        if (exit != 0)
        {
            return exit;
        }

        AnsiConsole.MarkupLine($"[green]✓[/] Posted a {Markup.Escape(status)} update to incident {settings.IncidentId}.");
        return 0;
    }
}

/// <summary><c>flare status-pages incidents delete &lt;PAGE_ID&gt; &lt;INCIDENT_ID&gt;</c>.</summary>
internal sealed class StatusIncidentsDeleteCommand : AsyncCommand<StatusIncidentsDeleteCommand.Settings>
{
    internal sealed class Settings : InstanceSettings
    {
        [CommandArgument(0, "<PAGE_ID>")]
        [Description("The page's id (see `flare status-pages list`).")]
        public required Guid PageId { get; init; }

        [CommandArgument(1, "<INCIDENT_ID>")]
        [Description("The incident's id (see `flare status-pages incidents list`).")]
        public required Guid IncidentId { get; init; }

        [CommandOption("-y|--yes")]
        [Description("Skip the interactive confirmation prompt. Required for non-interactive use.")]
        public bool Yes { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (!settings.Yes)
        {
            if (!AnsiConsole.Profile.Capabilities.Interactive)
            {
                AnsiConsole.MarkupLine("[red]Refusing to delete without --yes on a non-interactive invocation.[/]");
                return 1;
            }

            if (!AnsiConsole.Confirm($"Delete incident [bold]{settings.IncidentId}[/] and its updates? It disappears from the public page. Continue?", defaultValue: false))
            {
                AnsiConsole.MarkupLine("[grey]Aborted - nothing was removed.[/]");
                return 1;
            }
        }

        var (_, exit) = await StatusIncidentCli.SendAsync<object>(
            settings.InstanceName, HttpMethod.Delete, $"/api/status-pages/{settings.PageId}/incidents/{settings.IncidentId}", null, cancellationToken);
        if (exit == 0)
        {
            AnsiConsole.MarkupLine($"[green]✓[/] Deleted incident {settings.IncidentId}.");
        }

        return exit;
    }
}

// ---- Wire DTOs - hand-mirror of Flare.Api's Model/StatusPageModels.cs (camelCase JSON; enums as strings). ----

internal sealed class StatusIncidentWire
{
    public required Guid Id { get; init; }

    public required string Title { get; init; }

    public string Status { get; init; } = "Investigating";

    public List<StatusIncidentUpdateWire> Updates { get; init; } = [];

    public List<Guid> Components { get; init; } = [];
}

internal sealed class StatusIncidentUpdateWire
{
    public string Status { get; init; } = "";

    public string Message { get; init; } = "";
}

internal sealed class StatusIncidentListResponseWire
{
    public List<StatusIncidentWire> Incidents { get; init; } = [];
}

internal sealed class StatusIncidentRequestWire
{
    public required string Title { get; init; }

    public required string Message { get; init; }

    public string? Status { get; init; }

    public List<Guid>? Components { get; init; }
}

internal sealed class StatusIncidentUpdateRequestWire
{
    public required string Status { get; init; }

    public required string Message { get; init; }

    public List<Guid>? Components { get; init; }
}
