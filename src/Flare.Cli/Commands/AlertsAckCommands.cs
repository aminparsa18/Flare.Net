using System.ComponentModel;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flare.Cli.Internal;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Flare.Cli.Commands;

/// <summary>Settings shared by <c>flare alerts ack|snooze|unack</c>.</summary>
internal class AlertsAckSettings : InstanceSettings
{
    [CommandArgument(0, "<ID>")]
    [Description("The alert rule's id (see `flare alerts list`).")]
    public required Guid Id { get; init; }
}

/// <summary>
/// Shared request/response handling for the ack family (ADR-0124): <c>POST /api/alerts/{id}/ack</c>,
/// <c>POST /api/alerts/{id}/snooze</c> and <c>DELETE /api/alerts/{id}/ack</c>.
/// </summary>
internal static class AlertsAckRunner
{
    public static async Task<int> RunAsync(AlertsAckSettings settings, HttpMethod method, string path, object? body, Func<JsonElement, string> describe, CancellationToken cancellationToken)
    {
        var instance = FlareHome.ResolveTarget(settings.InstanceName);

        if (!instance.IsInitialized)
        {
            AnsiConsole.MarkupLine($"[grey]Not initialized yet - run `{instance.StartHint}` first.[/]");
            return 1;
        }

        var port = instance.ReadEnvValue("FLARE_API_PORT", "8080");
        using var http = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        try
        {
            using var response = await http.SendAsync(request, cancellationToken);

            switch (response.StatusCode)
            {
                case HttpStatusCode.NotFound:
                    AnsiConsole.MarkupLine($"[red]✗[/] No alert rule with id {settings.Id}.");
                    return 1;
                case HttpStatusCode.Conflict:
                    AnsiConsole.MarkupLine("[yellow]![/] That rule isn't firing, so there is nothing to acknowledge.");
                    return 1;
            }

            if (!response.IsSuccessStatusCode)
            {
                var detail = await ReadProblemDetailAsync(response, cancellationToken);
                AnsiConsole.MarkupLine($"[red]✗[/] {method} {path} failed: {(int)response.StatusCode} {response.ReasonPhrase}{detail}");
                return 1;
            }

            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            AnsiConsole.MarkupLine($"[green]✓[/] {Markup.Escape(describe(document.RootElement))}");
            return 0;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] Couldn't reach the API on localhost:{port} - is `api` running? Check `flare status`.");
            return 1;
        }
    }

    private static async Task<string> ReadProblemDetailAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            return document.RootElement.TryGetProperty("detail", out var detail) && detail.GetString() is { Length: > 0 } text ? $" - {Markup.Escape(text)}" : "";
        }
        catch (JsonException)
        {
            return "";
        }
    }
}

/// <summary>
/// <c>flare alerts ack &lt;ID&gt; [--note TEXT]</c> - acknowledges the rule's current incident, which
/// silences re-notifications and stops escalation until the rule resolves (ADR-0124/0125).
/// </summary>
internal sealed class AlertsAckCommand : AsyncCommand<AlertsAckCommand.Settings>
{
    internal sealed class Settings : AlertsAckSettings
    {
        [CommandOption("--note <TEXT>")]
        [Description("Optional note recorded with the acknowledgement (max 500 characters).")]
        public string? Note { get; init; }
    }

    protected override Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken) =>
        AlertsAckRunner.RunAsync(settings, HttpMethod.Post, $"/api/alerts/{settings.Id}/ack", new { note = settings.Note }, _ => "Acknowledged. Re-notifications and escalation stop until the rule resolves.", cancellationToken);
}

/// <summary>
/// <c>flare alerts snooze &lt;ID&gt; --minutes N [--note TEXT]</c> - mutes re-notifications for the
/// rule's current incident for N minutes. A snooze does not stop escalation (ADR-0125).
/// </summary>
internal sealed class AlertsSnoozeCommand : AsyncCommand<AlertsSnoozeCommand.Settings>
{
    internal sealed class Settings : AlertsAckSettings
    {
        [CommandOption("--minutes <MINUTES>")]
        [Description("How long to snooze, in minutes (1 to 10080).")]
        public required int Minutes { get; init; }

        [CommandOption("--note <TEXT>")]
        [Description("Optional note recorded with the snooze (max 500 characters).")]
        public string? Note { get; init; }
    }

    protected override Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken) =>
        AlertsAckRunner.RunAsync(
            settings,
            HttpMethod.Post,
            $"/api/alerts/{settings.Id}/snooze",
            new { snoozeMinutes = settings.Minutes, note = settings.Note },
            root => root.TryGetProperty("ack", out var ack) && ack.ValueKind == JsonValueKind.Object && ack.TryGetProperty("snoozedUntil", out var until) && until.ValueKind == JsonValueKind.String
                ? $"Snoozed until {until.GetDateTimeOffset().ToLocalTime():yyyy-MM-dd HH:mm zzz}."
                : $"Snoozed for {settings.Minutes} minutes.",
            cancellationToken);
}

/// <summary>
/// <c>flare alerts unack &lt;ID&gt;</c> - clears the rule's ack or snooze so the incident
/// notifies (and can escalate) again.
/// </summary>
internal sealed class AlertsUnackCommand : AsyncCommand<AlertsAckSettings>
{
    protected override Task<int> ExecuteAsync(CommandContext context, AlertsAckSettings settings, CancellationToken cancellationToken) =>
        AlertsAckRunner.RunAsync(settings, HttpMethod.Delete, $"/api/alerts/{settings.Id}/ack", null, _ => "Cleared. The incident notifies and can escalate again.", cancellationToken);
}
