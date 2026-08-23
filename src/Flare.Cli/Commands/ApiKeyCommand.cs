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
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var instance = FlareHome.Resolve(settings.InstanceName);

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
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[bold yellow]{Markup.Escape(response.RawKey)}[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[grey]Copy this now - Flare never stores or shows the raw key again.[/]");
        return 0;
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
}

internal sealed class CreateIngestApiKeyResponseWire
{
    public required IngestApiKeyDtoWire Key { get; init; }

    public required string RawKey { get; init; }
}
