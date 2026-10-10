using System.ComponentModel;
using System.Net.Http.Json;
using System.Text.Json;
using Flare.Cli.Internal;
using Flare.Mcp;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Flare.Cli.Commands;

/// <summary>
/// <c>flare releases list/mark/delete</c> - release markers via Flare.Api's <c>/api/releases</c>
/// (<c>src/Flare.Api/Endpoints/ReleaseEndpoints.cs</c>, docs-internal/adr/0182-release-tracking.md).
/// A deploy pipeline against a remote Flare calls the same endpoint with a personal access token
/// (docs/how-to/track-releases.md); this is the surface for the standing local stack.
/// </summary>
internal sealed record ReleaseWire(string Id, string Service, string Version, string Commit, string Url, string Notes, DateTimeOffset DeployedAt, string CreatedBy, long? NewErrorCount);

internal sealed record ReleaseListWire(List<ReleaseWire> Releases);

internal static class ReleasesClient
{
    public static HttpClient? TryCreate(string? instanceName, out string? failure)
    {
        var instance = FlareHome.ResolveTarget(instanceName);
        if (!instance.IsInitialized)
        {
            failure = $"Not initialized yet - run `{instance.StartHint}` first.";
            return null;
        }

        failure = null;
        return new HttpClient { BaseAddress = new Uri($"http://localhost:{instance.ReadEnvValue("FLARE_API_PORT", "8080")}") };
    }

    public static async Task<string> ProblemAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            if (doc.RootElement.TryGetProperty("detail", out var detail) && detail.GetString() is { Length: > 0 } text)
            {
                return text;
            }
        }
        catch (JsonException)
        {
            // not a problem+json body
        }

        return $"{(int)response.StatusCode} {response.ReasonPhrase}";
    }
}

internal sealed class ReleasesListCommand : AsyncCommand<ReleasesListCommand.Settings>
{
    internal sealed class Settings : InstanceSettings
    {
        [CommandOption("--service <SERVICE>")]
        [Description("Only this service's releases; also shows how many new error groups each version introduced.")]
        public string? Service { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        using var http = ReleasesClient.TryCreate(settings.InstanceName, out var failure);
        if (http is null)
        {
            AnsiConsole.MarkupLine($"[grey]{Markup.Escape(failure!)}[/]");
            return 1;
        }

        ReleaseListWire? response;
        try
        {
            var url = "/api/releases" + (string.IsNullOrEmpty(settings.Service) ? "" : $"?service={Uri.EscapeDataString(settings.Service)}");
            using var httpResponse = await http.GetAsync(url, cancellationToken);
            if (!httpResponse.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[red]✗[/] {Markup.Escape(await ReleasesClient.ProblemAsync(httpResponse, cancellationToken))}");
                return 1;
            }

            response = await httpResponse.Content.ReadFromJsonAsync<ReleaseListWire>(WireJsonOptions.Instance, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] {Markup.Escape(ex.Message)} - is `api` running? Check `flare status`.");
            return 1;
        }

        var releases = response?.Releases ?? [];
        if (releases.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]No releases marked. Use `flare releases mark`.[/]");
            return 0;
        }

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Service");
        table.AddColumn("Version");
        table.AddColumn("Deployed");
        table.AddColumn("Commit");
        table.AddColumn("New errors");
        foreach (var release in releases)
        {
            table.AddRow(
                Markup.Escape(release.Service),
                Markup.Escape(release.Version),
                release.DeployedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                Markup.Escape(release.Commit.Length > 8 ? release.Commit[..8] : release.Commit),
                release.NewErrorCount?.ToString() ?? "[grey]-[/]");
        }

        AnsiConsole.Write(table);
        return 0;
    }
}

internal sealed class ReleasesMarkCommand : AsyncCommand<ReleasesMarkCommand.Settings>
{
    internal sealed class Settings : InstanceSettings
    {
        [CommandArgument(0, "<SERVICE>")]
        [Description("The service's name (its service.name).")]
        public required string Service { get; init; }

        [CommandArgument(1, "<VERSION>")]
        [Description("The version, exactly as the service reports it in service.version.")]
        public required string Version { get; init; }

        [CommandOption("--commit <SHA>")]
        [Description("The commit that was deployed.")]
        public string? Commit { get; init; }

        [CommandOption("--url <URL>")]
        [Description("A link to the commit, pull request or pipeline run.")]
        public string? Url { get; init; }

        [CommandOption("--notes <TEXT>")]
        [Description("Free-form release notes.")]
        public string? Notes { get; init; }

        [CommandOption("--deployed-at <TIME>")]
        [Description("When it went out (ISO 8601). Defaults to now.")]
        public DateTimeOffset? DeployedAt { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        using var http = ReleasesClient.TryCreate(settings.InstanceName, out var failure);
        if (http is null)
        {
            AnsiConsole.MarkupLine($"[grey]{Markup.Escape(failure!)}[/]");
            return 1;
        }

        try
        {
            using var httpResponse = await http.PutAsJsonAsync("/api/releases", new
            {
                service = settings.Service,
                version = settings.Version,
                commit = settings.Commit ?? "",
                url = settings.Url ?? "",
                notes = settings.Notes ?? "",
                deployedAt = settings.DeployedAt,
            }, WireJsonOptions.Instance, cancellationToken);
            if (!httpResponse.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[red]✗[/] {Markup.Escape(await ReleasesClient.ProblemAsync(httpResponse, cancellationToken))}");
                return 1;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] {Markup.Escape(ex.Message)} - is `api` running? Check `flare status`.");
            return 1;
        }

        AnsiConsole.MarkupLine($"[green]✓[/] Marked {Markup.Escape(settings.Service)} {Markup.Escape(settings.Version)} as a release.");
        return 0;
    }
}

internal sealed class ReleasesDeleteCommand : AsyncCommand<ReleasesDeleteCommand.Settings>
{
    internal sealed class Settings : InstanceSettings
    {
        [CommandArgument(0, "<SERVICE>")]
        public required string Service { get; init; }

        [CommandArgument(1, "<VERSION>")]
        public required string Version { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        using var http = ReleasesClient.TryCreate(settings.InstanceName, out var failure);
        if (http is null)
        {
            AnsiConsole.MarkupLine($"[grey]{Markup.Escape(failure!)}[/]");
            return 1;
        }

        try
        {
            var url = $"/api/releases?service={Uri.EscapeDataString(settings.Service)}&version={Uri.EscapeDataString(settings.Version)}";
            using var httpResponse = await http.DeleteAsync(url, cancellationToken);
            if (httpResponse.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                AnsiConsole.MarkupLine($"[red]✗[/] {Markup.Escape(settings.Service)} {Markup.Escape(settings.Version)} isn't marked as a release.");
                return 1;
            }

            if (!httpResponse.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[red]✗[/] {Markup.Escape(await ReleasesClient.ProblemAsync(httpResponse, cancellationToken))}");
                return 1;
            }
        }
        catch (HttpRequestException ex)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] {Markup.Escape(ex.Message)} - is `api` running? Check `flare status`.");
            return 1;
        }

        AnsiConsole.MarkupLine("[green]✓[/] Release marker removed (telemetry is untouched).");
        return 0;
    }
}
