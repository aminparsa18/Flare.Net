using System.ComponentModel;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flare.Cli.Internal;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Flare.Cli.Commands;

/// <summary>
/// <c>flare alerts list</c> - table of saved alert rules via Flare.Api's <c>GET /api/alerts</c>
/// (<c>src/Flare.Api/Endpoints/AlertEndpoints.cs</c>, member-or-admin auth when Flare's
/// opt-in auth is enabled). The CLI-native equivalent of the dashboard's Alerts page list.
/// </summary>
internal sealed class AlertsListCommand : AsyncCommand
{
    protected override async Task<int> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        if (!FlareHome.IsInitialized)
        {
            AnsiConsole.MarkupLine("[grey]Not initialized yet - run `flare start` first.[/]");
            return 1;
        }

        var port = FlareHome.ReadEnvValue("FLARE_API_PORT", "8080");
        using var http = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };

        AlertRuleListResponseWire? response;
        try
        {
            using var httpResponse = await http.GetAsync("/api/alerts", cancellationToken);

            if (!httpResponse.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[red]✗[/] GET /api/alerts failed: {(int)httpResponse.StatusCode} {httpResponse.ReasonPhrase}");
                return 1;
            }

            response = await httpResponse.Content.ReadFromJsonAsync<AlertRuleListResponseWire>(WireJsonOptions.Instance, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] Couldn't reach the API on localhost:{port} - is `api` running? Check `flare status`.");
            return 1;
        }

        var rules = response?.Rules ?? [];
        if (rules.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]No alert rules configured.[/]");
            return 0;
        }

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Name");
        table.AddColumn("Enabled");
        table.AddColumn("Threshold");
        table.AddColumn("Window");
        table.AddColumn("Channel");
        table.AddColumn("Id");

        foreach (var rule in rules)
        {
            var enabled = rule.Enabled ? "[green]✓[/]" : "[grey]✗[/]";
            var comparator = rule.Threshold.Comparator == "LessThan" ? "<" : ">=";
            table.AddRow(
                Markup.Escape(rule.Name),
                enabled,
                $"{comparator} {rule.Threshold.Count}",
                FormatWindow(rule.WindowSeconds),
                DescribeChannel(rule),
                $"[grey]{rule.Id}[/]");
        }

        AnsiConsole.Write(table);
        return 0;
    }

    private static string FormatWindow(int seconds) => seconds switch
    {
        >= 3600 when seconds % 3600 == 0 => $"{seconds / 3600}h",
        >= 60 when seconds % 60 == 0 => $"{seconds / 60}m",
        _ => $"{seconds}s",
    };

    private static string DescribeChannel(AlertRuleWire rule)
    {
        if (!string.IsNullOrWhiteSpace(rule.WebhookUrl))
        {
            return "Webhook";
        }

        if (!string.IsNullOrWhiteSpace(rule.TelegramBotToken) && !string.IsNullOrWhiteSpace(rule.TelegramChatId))
        {
            return "Telegram";
        }

        if (!string.IsNullOrWhiteSpace(rule.EmailTo))
        {
            return "Email";
        }

        return "[grey]none[/]";
    }
}

/// <summary>
/// <c>flare alerts test &lt;ID&gt;</c> - dry-run fire of a saved alert rule via Flare.Api's
/// <c>POST /api/alerts/{id}/test</c>. Evaluates the rule's condition/threshold against
/// current data - ignores cooldown, sends no notification (see the doc comment on
/// <c>AlertTestResult</c>, <c>src/Flare.Api/Model/AlertModels.cs</c>) - so it's safe to run
/// repeatedly without triggering a real Slack/webhook/email/Telegram message.
/// </summary>
internal sealed class AlertsTestCommand : AsyncCommand<AlertsTestCommand.Settings>
{
    internal sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<ID>")]
        [Description("The alert rule's id (see `flare alerts list`).")]
        public required Guid Id { get; init; }
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

        AlertTestResultWire? result;
        try
        {
            using var httpResponse = await http.PostAsync($"/api/alerts/{settings.Id}/test", content: null, cancellationToken);

            if (httpResponse.StatusCode == HttpStatusCode.NotFound)
            {
                AnsiConsole.MarkupLine($"[red]✗[/] No alert rule with id {settings.Id}.");
                return 1;
            }

            if (!httpResponse.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[red]✗[/] POST /api/alerts/{settings.Id}/test failed: {(int)httpResponse.StatusCode} {httpResponse.ReasonPhrase}");
                return 1;
            }

            result = await httpResponse.Content.ReadFromJsonAsync<AlertTestResultWire>(WireJsonOptions.Instance, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] Couldn't reach the API on localhost:{port} - is `api` running? Check `flare status`.");
            return 1;
        }

        if (result is null)
        {
            AnsiConsole.MarkupLine("[red]✗[/] Empty response from /api/alerts/{id}/test.");
            return 1;
        }

        var verdict = result.WouldFire ? "[green]yes[/]" : "[grey]no[/]";
        AnsiConsole.MarkupLine($"Would fire: {verdict}");
        AnsiConsole.MarkupLine($"Observed count: {result.ObservedCount} (window: {result.WindowSeconds}s)");
        AnsiConsole.MarkupLine($"[grey]Evaluated at {result.EvaluatedAt.ToLocalTime():HH:mm:ss.fff} - cooldown untouched, no notification sent.[/]");
        return 0;
    }
}

// ---- Wire DTOs - hand-mirror of Flare.Api's Model/AlertModels.cs (see AlertsJsonContext's
// camelCase-properties/PascalCase-string-enum-values convention). Condition (LogFilter) is
// deliberately omitted - not rendered by either command here yet. -------------

internal sealed class AlertThresholdWire
{
    public required ulong Count { get; init; }

    /// <summary>"GreaterThanOrEqual" | "LessThan".</summary>
    public string Comparator { get; init; } = "GreaterThanOrEqual";
}

internal sealed class AlertRuleWire
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public string Description { get; init; } = "";

    public bool Enabled { get; init; } = true;

    public required AlertThresholdWire Threshold { get; init; }

    public required int WindowSeconds { get; init; }

    public int CooldownSeconds { get; init; } = 300;

    public string WebhookUrl { get; init; } = "";

    public string TelegramBotToken { get; init; } = "";

    public string TelegramChatId { get; init; } = "";

    public string EmailTo { get; init; } = "";

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }
}

internal sealed class AlertRuleListResponseWire
{
    public List<AlertRuleWire> Rules { get; init; } = [];
}

internal sealed class AlertTestResultWire
{
    public required ulong ObservedCount { get; init; }

    public required bool WouldFire { get; init; }

    public required DateTimeOffset EvaluatedAt { get; init; }

    public required int WindowSeconds { get; init; }
}
