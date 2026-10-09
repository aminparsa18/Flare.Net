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
/// <c>flare alert-templates list/create/update/delete</c> - CRUD for shared alert notification templates via
/// Flare.Api's <c>/api/alert-templates</c> (<c>src/Flare.Api/Endpoints/AlertTemplateEndpoints.cs</c>). A rule
/// picks one by name in <c>flare alerts import</c>; the one flagged default applies to rules that pick none.
/// See <c>docs-internal/adr/0148-shared-alert-notification-templates.md</c>.
/// </summary>
internal sealed class AlertTemplatesListCommand : AsyncCommand<AlertTemplatesListCommand.Settings>
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

        List<AlertTemplateWire>? templates;
        try
        {
            using var httpResponse = await http.GetAsync("/api/alert-templates", cancellationToken);
            if (!httpResponse.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[red]✗[/] GET /api/alert-templates failed: {(int)httpResponse.StatusCode} {httpResponse.ReasonPhrase}");
                return 1;
            }

            templates = await httpResponse.Content.ReadFromJsonAsync<List<AlertTemplateWire>>(WireJsonOptions.Instance, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] Couldn't reach the API on localhost:{port} - is `api` running? Check `flare status`.");
            return 1;
        }

        if (templates is null or { Count: 0 })
        {
            AnsiConsole.MarkupLine("[grey]No alert notification templates configured.[/]");
            return 0;
        }

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Name");
        table.AddColumn("Default");
        table.AddColumn("Title");
        table.AddColumn("Channel bodies");
        table.AddColumn("Id");

        foreach (var template in templates.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
        {
            table.AddRow(
                Markup.Escape(template.Name),
                template.IsDefault ? "[green]yes[/]" : "[grey]no[/]",
                Markup.Escape(template.TitleTemplate.Length == 0 ? "-" : template.TitleTemplate),
                template.ChannelBodies.Count == 0 ? "[grey]-[/]" : Markup.Escape(string.Join(", ", template.ChannelBodies.Keys.Order())),
                $"[grey]{template.Id}[/]");
        }

        AnsiConsole.Write(table);
        return 0;
    }
}

/// <summary>
/// <c>flare alert-templates create &lt;NAME&gt;</c> via <c>POST /api/alert-templates</c>. A template needs at
/// least one text; placeholder syntax and length caps are validated by the API and its 400 detail is shown as is.
/// </summary>
internal sealed class AlertTemplatesCreateCommand : AsyncCommand<AlertTemplatesCreateCommand.Settings>
{
    internal sealed class Settings : InstanceSettings
    {
        [CommandArgument(0, "<NAME>")]
        [Description("A label for this template, e.g. 'pager-short'. Unique, case-insensitive.")]
        public required string Name { get; init; }

        [CommandOption("--description <DESCRIPTION>")]
        public string? Description { get; init; }

        [CommandOption("--title <TEXT>")]
        [Description("Notification title ({{placeholder}} syntax). Empty keeps each channel's built-in subject.")]
        public string? Title { get; init; }

        [CommandOption("--body <TEXT>")]
        [Description("Body of a fired notification. Empty keeps the built-in wording.")]
        public string? Body { get; init; }

        [CommandOption("--resolved-body <TEXT>")]
        [Description("Body of a resolved notification. Empty falls back to --body.")]
        public string? ResolvedBody { get; init; }

        [CommandOption("--channel-body <TYPE=TEXT>")]
        [Description("Fired-body override for one channel type, e.g. 'Telegram=short text'. Repeat for several. On update, replaces all existing overrides.")]
        public string[]? ChannelBodies { get; init; }

        [CommandOption("--default <BOOL>")]
        [Description("Make this the instance default for rules that pick no template (any other default is cleared): true or false. Defaults to false.")]
        public bool? IsDefault { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var instance = FlareHome.ResolveTarget(settings.InstanceName);
        if (!instance.IsInitialized)
        {
            AnsiConsole.MarkupLine($"[grey]Not initialized yet - run `{instance.StartHint}` first.[/]");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(settings.Name))
        {
            AnsiConsole.MarkupLine("[red]✗[/] NAME can't be empty.");
            return 1;
        }

        if (!AlertTemplateFormat.TryParseChannelBodies(settings.ChannelBodies, out var channelBodies, out var error))
        {
            AnsiConsole.MarkupLine($"[red]✗[/] {Markup.Escape(error)}");
            return 1;
        }

        var request = new AlertTemplateRequestWire
        {
            Name = settings.Name,
            Description = settings.Description,
            IsDefault = settings.IsDefault,
            TitleTemplate = settings.Title,
            BodyTemplate = settings.Body,
            ResolvedBodyTemplate = settings.ResolvedBody,
            ChannelBodies = channelBodies,
        };

        var port = instance.ReadEnvValue("FLARE_API_PORT", "8080");
        using var http = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };

        AlertTemplateWire? template;
        try
        {
            using var httpResponse = await http.PostAsJsonAsync("/api/alert-templates", request, WireJsonOptions.Instance, cancellationToken);
            if (!httpResponse.IsSuccessStatusCode)
            {
                var detail = await NotificationChannelTypeParsing.TryReadProblemDetailAsync(httpResponse, cancellationToken);
                AnsiConsole.MarkupLine($"[red]✗[/] POST /api/alert-templates failed: {(int)httpResponse.StatusCode} {Markup.Escape(detail ?? httpResponse.ReasonPhrase ?? "")}");
                return 1;
            }

            template = await httpResponse.Content.ReadFromJsonAsync<AlertTemplateWire>(WireJsonOptions.Instance, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] Couldn't reach the API on localhost:{port} - is `api` running? Check `flare status`.");
            return 1;
        }

        if (template is null)
        {
            AnsiConsole.MarkupLine("[red]✗[/] Empty response from /api/alert-templates.");
            return 1;
        }

        AnsiConsole.MarkupLine($"[green]✓[/] Created template [bold]{Markup.Escape(template.Name)}[/] (id: {template.Id}).");
        return 0;
    }
}

/// <summary>
/// <c>flare alert-templates update &lt;ID&gt; [options]</c> via <c>PUT /api/alert-templates/{id}</c>, which
/// replaces the whole template. Fetches the existing one first and overrides only the options passed, so
/// `update &lt;ID&gt; --default true` doesn't re-type the texts. Pass an empty value (`--body ""`) to clear a text.
/// </summary>
internal sealed class AlertTemplatesUpdateCommand : AsyncCommand<AlertTemplatesUpdateCommand.Settings>
{
    internal sealed class Settings : InstanceSettings
    {
        [CommandArgument(0, "<ID>")]
        [Description("The template's id (see `flare alert-templates list`).")]
        public required Guid Id { get; init; }

        // Not --name: that is InstanceSettings' -n|--name (target instance).
        [CommandOption("--rename <NAME>")]
        [Description("A new label for the template.")]
        public string? Rename { get; init; }

        [CommandOption("--description <DESCRIPTION>")]
        public string? Description { get; init; }

        [CommandOption("--title <TEXT>")]
        public string? Title { get; init; }

        [CommandOption("--body <TEXT>")]
        public string? Body { get; init; }

        [CommandOption("--resolved-body <TEXT>")]
        public string? ResolvedBody { get; init; }

        [CommandOption("--channel-body <TYPE=TEXT>")]
        [Description("Fired-body override for one channel type, e.g. 'Telegram=short text'. Repeat for several. Replaces all existing overrides.")]
        public string[]? ChannelBodies { get; init; }

        [CommandOption("--default <BOOL>")]
        [Description("true or false.")]
        public bool? IsDefault { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var instance = FlareHome.ResolveTarget(settings.InstanceName);
        if (!instance.IsInitialized)
        {
            AnsiConsole.MarkupLine($"[grey]Not initialized yet - run `{instance.StartHint}` first.[/]");
            return 1;
        }

        if (!AlertTemplateFormat.TryParseChannelBodies(settings.ChannelBodies, out var channelBodies, out var error))
        {
            AnsiConsole.MarkupLine($"[red]✗[/] {Markup.Escape(error)}");
            return 1;
        }

        var port = instance.ReadEnvValue("FLARE_API_PORT", "8080");
        using var http = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };

        try
        {
            using var getResponse = await http.GetAsync($"/api/alert-templates/{settings.Id}", cancellationToken);
            if (getResponse.StatusCode == HttpStatusCode.NotFound)
            {
                AnsiConsole.MarkupLine($"[red]✗[/] No alert template with id {settings.Id}.");
                return 1;
            }

            if (!getResponse.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[red]✗[/] GET /api/alert-templates/{settings.Id} failed: {(int)getResponse.StatusCode} {getResponse.ReasonPhrase}");
                return 1;
            }

            var existing = await getResponse.Content.ReadFromJsonAsync<AlertTemplateWire>(WireJsonOptions.Instance, cancellationToken);
            if (existing is null)
            {
                AnsiConsole.MarkupLine("[red]✗[/] Empty response from /api/alert-templates.");
                return 1;
            }

            var request = new AlertTemplateRequestWire
            {
                Name = settings.Rename ?? existing.Name,
                Description = settings.Description ?? existing.Description,
                IsDefault = settings.IsDefault ?? existing.IsDefault,
                TitleTemplate = settings.Title ?? existing.TitleTemplate,
                BodyTemplate = settings.Body ?? existing.BodyTemplate,
                ResolvedBodyTemplate = settings.ResolvedBody ?? existing.ResolvedBodyTemplate,
                ChannelBodies = channelBodies ?? existing.ChannelBodies,
            };

            using var putResponse = await http.PutAsJsonAsync($"/api/alert-templates/{settings.Id}", request, WireJsonOptions.Instance, cancellationToken);
            if (!putResponse.IsSuccessStatusCode)
            {
                var detail = await NotificationChannelTypeParsing.TryReadProblemDetailAsync(putResponse, cancellationToken);
                AnsiConsole.MarkupLine($"[red]✗[/] PUT /api/alert-templates/{settings.Id} failed: {(int)putResponse.StatusCode} {Markup.Escape(detail ?? putResponse.ReasonPhrase ?? "")}");
                return 1;
            }

            AnsiConsole.MarkupLine($"[green]✓[/] Updated template [bold]{Markup.Escape(request.Name)}[/].");
            return 0;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] Couldn't reach the API on localhost:{port} - is `api` running? Check `flare status`.");
            return 1;
        }
    }
}

/// <summary>
/// <c>flare alert-templates delete &lt;ID&gt;</c> via <c>DELETE /api/alert-templates/{id}</c>. The API refuses
/// (409) while alert rules still reference the template and names them in its detail.
/// </summary>
internal sealed class AlertTemplatesDeleteCommand : AsyncCommand<AlertTemplatesDeleteCommand.Settings>
{
    internal sealed class Settings : InstanceSettings
    {
        [CommandArgument(0, "<ID>")]
        [Description("The template's id (see `flare alert-templates list`).")]
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

            if (!AnsiConsole.Confirm($"Delete alert template [bold]{settings.Id}[/]? Continue?", defaultValue: false))
            {
                AnsiConsole.MarkupLine("[grey]Aborted - nothing was removed.[/]");
                return 1;
            }
        }

        var port = instance.ReadEnvValue("FLARE_API_PORT", "8080");
        using var http = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
        try
        {
            using var response = await http.DeleteAsync($"/api/alert-templates/{settings.Id}", cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                AnsiConsole.MarkupLine($"[red]✗[/] No alert template with id {settings.Id}.");
                return 1;
            }

            if (!response.IsSuccessStatusCode)
            {
                var detail = await NotificationChannelTypeParsing.TryReadProblemDetailAsync(response, cancellationToken);
                AnsiConsole.MarkupLine($"[red]✗[/] DELETE /api/alert-templates/{settings.Id} failed: {(int)response.StatusCode} {Markup.Escape(detail ?? response.ReasonPhrase ?? "")}");
                return 1;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] Couldn't reach the API on localhost:{port} - is `api` running? Check `flare status`.");
            return 1;
        }

        AnsiConsole.MarkupLine($"[green]✓[/] Deleted template {settings.Id}.");
        return 0;
    }
}

internal static class AlertTemplateFormat
{
    /// <summary>
    /// Parses repeated <c>TYPE=TEXT</c> values into the API's channel-body map. Null input (option not passed)
    /// yields a null map so update can tell "unchanged" from "replace". The type names are the API's to
    /// validate; it lists the supported ones in its 400.
    /// </summary>
    public static bool TryParseChannelBodies(string[]? values, out Dictionary<string, string>? bodies, out string error)
    {
        bodies = null;
        error = "";
        if (values is not { Length: > 0 })
        {
            return true;
        }

        bodies = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            var separator = value.IndexOf('=');
            if (separator <= 0)
            {
                error = $"--channel-body '{value}' must look like TYPE=TEXT, e.g. Telegram=short text.";
                return false;
            }

            bodies[value[..separator].Trim()] = value[(separator + 1)..];
        }

        return true;
    }
}

// ---- Wire DTOs - hand-mirror of Flare.Api's Model/AlertTemplateModels.cs. ----

internal sealed class AlertTemplateWire
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public string Description { get; init; } = "";

    public bool IsDefault { get; init; }

    public string TitleTemplate { get; init; } = "";

    public string BodyTemplate { get; init; } = "";

    public string ResolvedBodyTemplate { get; init; } = "";

    public Dictionary<string, string> ChannelBodies { get; init; } = [];
}

internal sealed class AlertTemplateRequestWire
{
    public required string Name { get; init; }

    public string? Description { get; init; }

    public bool? IsDefault { get; init; }

    public string? TitleTemplate { get; init; }

    public string? BodyTemplate { get; init; }

    public string? ResolvedBodyTemplate { get; init; }

    public Dictionary<string, string>? ChannelBodies { get; init; }
}
