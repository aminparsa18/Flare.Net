using Flare.Mcp;
using System.ComponentModel;
using System.Net.Http.Json;
using System.Text.Json;
using Flare.Cli.Internal;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Flare.Cli.Commands;

/// <summary>
/// <c>flare apikey create &lt;NAME&gt;</c> - mints a new ingest API key via Flare.Api's
/// <c>POST /api/ingest-keys</c> (<c>src/Flare.Api/Endpoints/IngestApiKeyEndpoints.cs</c>,
/// admin-only auth when Flare's opt-in auth is enabled) - scripted/CI OTLP setup without
/// clicking through the dashboard's Settings page. These keys authenticate OTLP-emitting
/// apps/collectors calling <c>Flare.Ingest</c>, unrelated to the CLI's own (currently
/// absent) auth story. Registered as a branch (<c>"apikey"</c> -&gt; <c>"create"</c>) rather
/// than a flat command, matching <c>AlertsCommand</c>'s shape and leaving room for a future
/// `apikey list`/`apikey revoke` (the API already exposes GET/DELETE, not scoped here).
/// </summary>
internal sealed class ApiKeyCreateCommand : AsyncCommand<ApiKeyCreateCommand.Settings>
{
    internal sealed class Settings : InstanceSettings
    {
        [CommandArgument(0, "<NAME>")]
        [Description("A label for this key, e.g. 'ci' or 'staging-collector'.")]
        public required string Name { get; init; }

        [CommandOption("--origin <ORIGIN>")]
        [Description("Restrict the key to this browser origin, e.g. https://app.example.com. Repeatable.")]
        public string[] Origins { get; init; } = [];

        [CommandOption("--service <NAME>")]
        [Description("Restrict the key to this service.name. Repeatable.")]
        public string[] Services { get; init; } = [];
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

        var port = instance.ReadEnvValue("FLARE_API_PORT", "8080");
        using var http = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };

        CreateIngestApiKeyResponseWire? response;
        try
        {
            using var httpResponse = await http.PostAsJsonAsync(
                "/api/ingest-keys",
                new CreateIngestApiKeyRequestWire { Name = settings.Name },
                WireJsonOptions.Instance,
                cancellationToken);

            if (!httpResponse.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[red]✗[/] POST /api/ingest-keys failed: {(int)httpResponse.StatusCode} {httpResponse.ReasonPhrase}");
                return 1;
            }

            response = await httpResponse.Content.ReadFromJsonAsync<CreateIngestApiKeyResponseWire>(WireJsonOptions.Instance, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] Couldn't reach the API on localhost:{port} - is `api` running? Check `flare status`.");
            return 1;
        }

        if (response is null)
        {
            AnsiConsole.MarkupLine("[red]✗[/] Empty response from /api/ingest-keys.");
            return 1;
        }

        AnsiConsole.MarkupLine($"[green]✓[/] Created ingest key [bold]{Markup.Escape(response.Key.Name)}[/] (id: {response.Key.Id}, created: {response.Key.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}).");
        if (settings.Origins.Length > 0 || settings.Services.Length > 0)
        {
            if (await ApiKeyScope.ApplyAsync(http, response.Key.Id, settings.Origins, settings.Services, cancellationToken) is { } scopeError)
            {
                AnsiConsole.MarkupLine($"[red]✗[/] The key was created but could not be restricted: {Markup.Escape(scopeError)} Fix it with `flare apikey scope {Markup.Escape(response.Key.Name)}`.");
                return 1;
            }
            AnsiConsole.MarkupLine("[green]✓[/] Restricted the key; changes reach Flare.Ingest within 30 seconds.");
        }
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[bold yellow]{Markup.Escape(response.RawKey)}[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[grey]Copy this now - Flare never stores or shows the raw key again.[/]");
        return 0;
    }
}

/// <summary>
/// <c>flare apikey scope &lt;NAME&gt;</c> - restricts an existing ingest key to browser origins and/or
/// service names (ADR-0149, ADR-0150) via <c>PUT /api/ingest-keys/{id}/origins|services</c>. Each list
/// given replaces that key's current list; a list not mentioned is left alone, and
/// <c>--clear-origins</c>/<c>--clear-services</c> lift a restriction.
/// </summary>
internal sealed class ApiKeyScopeCommand : AsyncCommand<ApiKeyScopeCommand.Settings>
{
    internal sealed class Settings : InstanceSettings
    {
        [CommandArgument(0, "<NAME>")]
        [Description("Name of an active ingest key.")]
        public required string Name { get; init; }

        [CommandOption("--origin <ORIGIN>")]
        [Description("Browser origin the key may be used from. Repeatable; replaces the current list.")]
        public string[] Origins { get; init; } = [];

        [CommandOption("--service <NAME>")]
        [Description("service.name the key may write for. Repeatable; replaces the current list.")]
        public string[] Services { get; init; } = [];

        [CommandOption("--clear-origins")]
        [Description("Remove the origin restriction.")]
        public bool ClearOrigins { get; init; }

        [CommandOption("--clear-services")]
        [Description("Remove the service restriction.")]
        public bool ClearServices { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var instance = FlareHome.ResolveTarget(settings.InstanceName);
        if (!instance.IsInitialized)
        {
            AnsiConsole.MarkupLine($"[grey]Not initialized yet - run `{instance.StartHint}` first.[/]");
            return 1;
        }

        var setOrigins = settings.Origins.Length > 0;
        var setServices = settings.Services.Length > 0;
        if (!(setOrigins || settings.ClearOrigins || setServices || settings.ClearServices)
            || setOrigins && settings.ClearOrigins || setServices && settings.ClearServices)
        {
            AnsiConsole.MarkupLine("[red]✗[/] Give --origin/--service to set a list, or --clear-origins/--clear-services to lift one (not both for the same list).");
            return 1;
        }

        var port = instance.ReadEnvValue("FLARE_API_PORT", "8080");
        using var http = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };

        try
        {
            var list = await http.GetFromJsonAsync<IngestApiKeyListResponseWire>("/api/ingest-keys", WireJsonOptions.Instance, cancellationToken);
            var key = list?.Keys.FirstOrDefault(k => k.IsActive && string.Equals(k.Name, settings.Name, StringComparison.OrdinalIgnoreCase));
            if (key is null)
            {
                AnsiConsole.MarkupLine($"[red]✗[/] No active ingest key named '{Markup.Escape(settings.Name)}'.");
                return 1;
            }

            if (await ApiKeyScope.ApplyAsync(http, key.Id, setOrigins || settings.ClearOrigins ? settings.Origins : null,
                    setServices || settings.ClearServices ? settings.Services : null, cancellationToken) is { } error)
            {
                AnsiConsole.MarkupLine($"[red]✗[/] {Markup.Escape(error)}");
                return 1;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] Couldn't reach the API on localhost:{port} - is `api` running? Check `flare status`.");
            return 1;
        }

        AnsiConsole.MarkupLine($"[green]✓[/] Updated [bold]{Markup.Escape(settings.Name)}[/]; changes reach Flare.Ingest within 30 seconds.");
        return 0;
    }
}

internal static class ApiKeyScope
{
    /// <summary>PUTs whichever of the two lists is non-null (an empty one clears). Returns an error message, or null on success.</summary>
    public static async Task<string?> ApplyAsync(HttpClient http, Guid id, IReadOnlyList<string>? origins, IReadOnlyList<string>? services, CancellationToken cancellationToken)
    {
        if (origins is not null && await PutAsync(http, $"/api/ingest-keys/{id}/origins", new SetOriginsWire { Origins = origins }, cancellationToken) is { } originError)
        {
            return originError;
        }

        return services is not null
            ? await PutAsync(http, $"/api/ingest-keys/{id}/services", new SetServicesWire { Services = services }, cancellationToken)
            : null;
    }

    private static async Task<string?> PutAsync<T>(HttpClient http, string path, T body, CancellationToken cancellationToken)
    {
        using var response = await http.PutAsJsonAsync(path, body, WireJsonOptions.Instance, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return null;
        }

        var detail = await response.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            using var doc = JsonDocument.Parse(detail);
            if (doc.RootElement.TryGetProperty("detail", out var d) && d.GetString() is { Length: > 0 } text)
            {
                return text;
            }
        }
        catch (JsonException)
        {
        }

        return $"PUT {path} failed: {(int)response.StatusCode} {response.ReasonPhrase}";
    }
}

// ---- Wire DTOs - hand-mirror of Flare.Api's Model/IngestApiKeyModels.cs (see
// IngestApiKeysJsonContext's camelCase-properties convention). -------------

internal sealed class CreateIngestApiKeyRequestWire
{
    public required string Name { get; init; }
}

internal sealed class IngestApiKeyDtoWire
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? RevokedAt { get; init; }

    public bool IsActive { get; init; }

    public string[] AllowedOrigins { get; init; } = [];

    public string[] AllowedServices { get; init; } = [];
}

internal sealed class IngestApiKeyListResponseWire
{
    public required List<IngestApiKeyDtoWire> Keys { get; init; }
}

internal sealed class SetOriginsWire
{
    public required IReadOnlyList<string> Origins { get; init; }
}

internal sealed class SetServicesWire
{
    public required IReadOnlyList<string> Services { get; init; }
}

internal sealed class CreateIngestApiKeyResponseWire
{
    public required IngestApiKeyDtoWire Key { get; init; }

    public required string RawKey { get; init; }
}
