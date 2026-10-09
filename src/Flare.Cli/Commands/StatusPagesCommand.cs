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
/// <c>flare status-pages list/create/update/delete</c> - CRUD for public status pages via Flare.Api's
/// <c>/api/status-pages</c> (<c>src/Flare.Api/Endpoints/StatusPageEndpoints.cs</c>). A page is off until
/// <c>--enabled true</c>. See <c>docs-internal/adr/0158-status-pages.md</c>.
/// </summary>
internal sealed class StatusPagesListCommand : AsyncCommand<StatusPagesListCommand.Settings>
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

        StatusPageListResponseWire? response;
        try
        {
            using var httpResponse = await http.GetAsync("/api/status-pages", cancellationToken);
            if (!httpResponse.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[red]✗[/] GET /api/status-pages failed: {(int)httpResponse.StatusCode} {httpResponse.ReasonPhrase}");
                return 1;
            }

            response = await httpResponse.Content.ReadFromJsonAsync<StatusPageListResponseWire>(WireJsonOptions.Instance, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] Couldn't reach the API on localhost:{port} - is `api` running? Check `flare status`.");
            return 1;
        }

        var pages = response?.Pages ?? [];
        if (pages.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]No status pages configured.[/]");
            return 0;
        }

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Slug");
        table.AddColumn("Title");
        table.AddColumn("Published");
        table.AddColumn("Components");
        table.AddColumn("Id");

        foreach (var page in pages)
        {
            table.AddRow(
                Markup.Escape(page.Slug),
                Markup.Escape(page.Title),
                page.Enabled ? "[green]yes[/]" : "[grey]no[/]",
                page.Components.Count.ToString(),
                $"[grey]{page.Id}[/]");
        }

        AnsiConsole.Write(table);
        return 0;
    }
}

/// <summary>
/// <c>flare status-pages create &lt;SLUG&gt; --title T</c> via <c>POST /api/status-pages</c>. Validation (slug shape,
/// uniqueness, components pointing at real monitors/SLOs) is the API's; its detail is surfaced as is.
/// </summary>
internal sealed class StatusPagesCreateCommand : AsyncCommand<StatusPagesCreateCommand.Settings>
{
    internal sealed class Settings : InstanceSettings
    {
        [CommandArgument(0, "<SLUG>")]
        [Description("The page's URL segment (served at /status/<slug>): lowercase letters, digits and hyphens.")]
        public required string Slug { get; init; }

        [CommandOption("--title <TITLE>")]
        [Description("The page heading. Required.")]
        public string? Title { get; init; }

        [CommandOption("--description <DESCRIPTION>")]
        public string? Description { get; init; }

        [CommandOption("--component <COMPONENT>")]
        [Description("A row as 'Display name=monitor:<id>' or 'Display name=slo:<id>'. Repeat for several. On update, replaces all existing components.")]
        public string[]? Components { get; init; }

        [CommandOption("--subscriber <CHANNEL_ID>")]
        [Description("A notification channel id (see `flare notification-channels list`) to tell about every incident on this page. Repeat for several. Webhook, Slack, Telegram, email, Teams or Discord channels only.")]
        public string[]? Subscribers { get; init; }

        [CommandOption("--enabled <BOOL>")]
        [Description("true publishes the page to anyone with the URL. Defaults to false.")]
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

        if (string.IsNullOrWhiteSpace(settings.Title))
        {
            AnsiConsole.MarkupLine("[red]✗[/] --title is required.");
            return 1;
        }

        List<StatusPageComponentWire>? components = null;
        if (settings.Components is { Length: > 0 } && !StatusPageFormat.TryParseComponents(settings.Components, out components, out var error))
        {
            AnsiConsole.MarkupLine($"[red]✗[/] {Markup.Escape(error)}");
            return 1;
        }

        if (!StatusPageFormat.TryParseSubscribers(settings.Subscribers, out var subscribers, out var subscriberError))
        {
            AnsiConsole.MarkupLine($"[red]✗[/] {Markup.Escape(subscriberError)}");
            return 1;
        }

        var request = new StatusPageRequestWire
        {
            Slug = settings.Slug,
            Title = settings.Title,
            Description = settings.Description,
            Enabled = settings.Enabled,
            Components = components,
            SubscriberChannelIds = subscribers,
        };

        var port = instance.ReadEnvValue("FLARE_API_PORT", "8080");
        using var http = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };

        StatusPageWire? page;
        try
        {
            using var httpResponse = await http.PostAsJsonAsync("/api/status-pages", request, WireJsonOptions.Instance, cancellationToken);
            if (!httpResponse.IsSuccessStatusCode)
            {
                var detail = await NotificationChannelTypeParsing.TryReadProblemDetailAsync(httpResponse, cancellationToken);
                AnsiConsole.MarkupLine($"[red]✗[/] POST /api/status-pages failed: {(int)httpResponse.StatusCode} {Markup.Escape(detail ?? httpResponse.ReasonPhrase ?? "")}");
                return 1;
            }

            page = await httpResponse.Content.ReadFromJsonAsync<StatusPageWire>(WireJsonOptions.Instance, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] Couldn't reach the API on localhost:{port} - is `api` running? Check `flare status`.");
            return 1;
        }

        if (page is null)
        {
            AnsiConsole.MarkupLine("[red]✗[/] Empty response from /api/status-pages.");
            return 1;
        }

        AnsiConsole.MarkupLine($"[green]✓[/] Created status page [bold]{Markup.Escape(page.Slug)}[/] (id: {page.Id}){(page.Enabled ? "" : " - not published yet; pass --enabled true to publish")}.");
        return 0;
    }
}

/// <summary>
/// <c>flare status-pages update &lt;ID&gt; [options]</c> via <c>PUT /api/status-pages/{id}</c>, which replaces the
/// whole page. Fetches the existing one first and overrides only the options passed, so
/// `update &lt;ID&gt; --enabled true` publishes a page without re-typing the rest.
/// </summary>
internal sealed class StatusPagesUpdateCommand : AsyncCommand<StatusPagesUpdateCommand.Settings>
{
    internal sealed class Settings : InstanceSettings
    {
        [CommandArgument(0, "<ID>")]
        [Description("The page's id (see `flare status-pages list`).")]
        public required Guid Id { get; init; }

        [CommandOption("--slug <SLUG>")]
        public string? Slug { get; init; }

        [CommandOption("--title <TITLE>")]
        public string? Title { get; init; }

        [CommandOption("--description <DESCRIPTION>")]
        public string? Description { get; init; }

        [CommandOption("--component <COMPONENT>")]
        [Description("A row as 'Display name=monitor:<id>' or 'Display name=slo:<id>'. Repeat for several. Replaces all existing components.")]
        public string[]? Components { get; init; }

        [CommandOption("--subscriber <CHANNEL_ID>")]
        [Description("A notification channel id to tell about every incident on this page. Repeat for several. Replaces all existing subscribers; omit to leave them as they are.")]
        public string[]? Subscribers { get; init; }

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

        List<StatusPageComponentWire>? components = null;
        if (settings.Components is { Length: > 0 } && !StatusPageFormat.TryParseComponents(settings.Components, out components, out var error))
        {
            AnsiConsole.MarkupLine($"[red]✗[/] {Markup.Escape(error)}");
            return 1;
        }

        if (!StatusPageFormat.TryParseSubscribers(settings.Subscribers, out var subscribers, out var subscriberError))
        {
            AnsiConsole.MarkupLine($"[red]✗[/] {Markup.Escape(subscriberError)}");
            return 1;
        }

        var port = instance.ReadEnvValue("FLARE_API_PORT", "8080");
        using var http = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };

        try
        {
            using var getResponse = await http.GetAsync($"/api/status-pages/{settings.Id}", cancellationToken);
            if (getResponse.StatusCode == HttpStatusCode.NotFound)
            {
                AnsiConsole.MarkupLine($"[red]✗[/] No status page with id {settings.Id}.");
                return 1;
            }

            if (!getResponse.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[red]✗[/] GET /api/status-pages/{settings.Id} failed: {(int)getResponse.StatusCode} {getResponse.ReasonPhrase}");
                return 1;
            }

            var existing = await getResponse.Content.ReadFromJsonAsync<StatusPageWire>(WireJsonOptions.Instance, cancellationToken);
            if (existing is null)
            {
                AnsiConsole.MarkupLine("[red]✗[/] Empty response from /api/status-pages.");
                return 1;
            }

            var request = new StatusPageRequestWire
            {
                Slug = settings.Slug ?? existing.Slug,
                Title = settings.Title ?? existing.Title,
                Description = settings.Description ?? existing.Description,
                Enabled = settings.Enabled ?? existing.Enabled,
                Components = components ?? existing.Components,
                SubscriberChannelIds = subscribers,
            };

            using var putResponse = await http.PutAsJsonAsync($"/api/status-pages/{settings.Id}", request, WireJsonOptions.Instance, cancellationToken);
            if (!putResponse.IsSuccessStatusCode)
            {
                var detail = await NotificationChannelTypeParsing.TryReadProblemDetailAsync(putResponse, cancellationToken);
                AnsiConsole.MarkupLine($"[red]✗[/] PUT /api/status-pages/{settings.Id} failed: {(int)putResponse.StatusCode} {Markup.Escape(detail ?? putResponse.ReasonPhrase ?? "")}");
                return 1;
            }

            AnsiConsole.MarkupLine($"[green]✓[/] Updated status page [bold]{Markup.Escape(request.Slug)}[/].");
            return 0;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] Couldn't reach the API on localhost:{port} - is `api` running? Check `flare status`.");
            return 1;
        }
    }
}

/// <summary><c>flare status-pages delete &lt;ID&gt;</c> via <c>DELETE /api/status-pages/{id}</c>.</summary>
internal sealed class StatusPagesDeleteCommand : AsyncCommand<StatusPagesDeleteCommand.Settings>
{
    internal sealed class Settings : InstanceSettings
    {
        [CommandArgument(0, "<ID>")]
        [Description("The page's id (see `flare status-pages list`).")]
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

            if (!AnsiConsole.Confirm($"Delete status page [bold]{settings.Id}[/]? Its public URL stops working. Continue?", defaultValue: false))
            {
                AnsiConsole.MarkupLine("[grey]Aborted - nothing was removed.[/]");
                return 1;
            }
        }

        var port = instance.ReadEnvValue("FLARE_API_PORT", "8080");
        using var http = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
        try
        {
            using var response = await http.DeleteAsync($"/api/status-pages/{settings.Id}", cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                AnsiConsole.MarkupLine($"[red]✗[/] No status page with id {settings.Id}.");
                return 1;
            }

            if (!response.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[red]✗[/] DELETE /api/status-pages/{settings.Id} failed: {(int)response.StatusCode} {response.ReasonPhrase}");
                return 1;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] Couldn't reach the API on localhost:{port} - is `api` running? Check `flare status`.");
            return 1;
        }

        AnsiConsole.MarkupLine($"[green]✓[/] Deleted status page {settings.Id}.");
        return 0;
    }
}

internal static class StatusPageFormat
{
    /// <summary>Parses <c>Name=monitor:&lt;guid&gt;</c> / <c>Name=slo:&lt;guid&gt;</c> into wire components.</summary>
    /// <summary>Parses repeated <c>--subscriber</c> channel ids; null (none given) stays null so an update leaves subscribers alone.</summary>
    public static bool TryParseSubscribers(IEnumerable<string>? values, out List<Guid>? ids, out string error)
    {
        ids = null;
        error = "";
        if (values is null)
        {
            return true;
        }

        var parsed = new List<Guid>();
        foreach (var value in values)
        {
            if (!Guid.TryParse(value, out var id))
            {
                error = $"--subscriber '{value}' is not a channel id (a GUID).";
                return false;
            }

            parsed.Add(id);
        }

        ids = parsed;
        return true;
    }

    public static bool TryParseComponents(IEnumerable<string> values, out List<StatusPageComponentWire> components, out string error)
    {
        components = [];
        error = "";
        foreach (var value in values)
        {
            var eq = value.LastIndexOf('=');
            var colon = value.LastIndexOf(':');
            var kind = eq > 0 && colon > eq ? value[(eq + 1)..colon].Trim().ToLowerInvariant() : "";
            if (eq <= 0 || kind is not ("monitor" or "slo") || !Guid.TryParse(value[(colon + 1)..].Trim(), out var id))
            {
                error = $"--component '{value}' must look like 'Display name=monitor:<id>' or 'Display name=slo:<id>'.";
                return false;
            }

            components.Add(new StatusPageComponentWire
            {
                Name = value[..eq].Trim(),
                Kind = kind == "monitor" ? "Monitor" : "Slo",
                RefId = id,
            });
        }

        return true;
    }
}

// ---- Wire DTOs - hand-mirror of Flare.Api's Model/StatusPageModels.cs (camelCase JSON; enums as strings). ----

internal sealed class StatusPageComponentWire
{
    public required string Name { get; init; }

    public required string Kind { get; init; }

    public required Guid RefId { get; init; }
}

internal sealed class StatusPageWire
{
    public required Guid Id { get; init; }

    public required string Slug { get; init; }

    public required string Title { get; init; }

    public string Description { get; init; } = "";

    public bool Enabled { get; init; }

    public List<StatusPageComponentWire> Components { get; init; } = [];

    public List<Guid> SubscriberChannelIds { get; init; } = [];
}

internal sealed class StatusPageListResponseWire
{
    public List<StatusPageWire> Pages { get; init; } = [];
}

internal sealed class StatusPageRequestWire
{
    public required string Slug { get; init; }

    public required string Title { get; init; }

    public string? Description { get; init; }

    public bool? Enabled { get; init; }

    public List<StatusPageComponentWire>? Components { get; init; }

    /// <summary>Null leaves an existing page's subscribers untouched; an empty list clears them.</summary>
    public List<Guid>? SubscriberChannelIds { get; init; }
}
