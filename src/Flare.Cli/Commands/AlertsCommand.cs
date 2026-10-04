using Flare.Mcp;
using System.ComponentModel;
using System.Globalization;
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
internal sealed class AlertsListCommand : AsyncCommand<AlertsListCommand.Settings>
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
                rule switch
                {
                    { ConditionKind: "Anomaly", AnomalyCondition: { } anomaly } => DescribeAnomaly(anomaly),
                    { ConditionKind: "SloBurnRate", SloCondition: { } slo } => $"burn >= {slo.BurnRateThreshold.ToString(CultureInfo.InvariantCulture)}x over {FormatWindow(slo.LongWindowSeconds)} and {FormatWindow(slo.ShortWindowSeconds)}",
                    _ => $"{comparator} {rule.Threshold.Count}",
                },
                FormatWindow(rule.WindowSeconds),
                DescribeChannel(rule),
                $"[grey]{rule.Id}[/]");
        }

        AnsiConsole.Write(table);
        return 0;
    }

    /// <summary>E.g. "|z| >= 3 vs 7 days" - an anomaly rule has no fixed threshold to show.</summary>
    private static string DescribeAnomaly(AnomalyConditionWire anomaly)
    {
        var z = anomaly.Direction switch
        {
            "Above" => "z >=",
            "Below" => "z <= -",
            _ => "|z| >=",
        };
        var unit = anomaly.Seasonality == "Weekly" ? "weeks" : "days";
        return $"{z}{(anomaly.Direction == "Below" ? "" : " ")}{anomaly.ZScoreThreshold.ToString(CultureInfo.InvariantCulture)} vs {anomaly.BaselinePeriods} {unit}";
    }

    private static string FormatWindow(int seconds) => seconds switch
    {
        >= 3600 when seconds % 3600 == 0 => $"{seconds / 3600}h",
        >= 60 when seconds % 60 == 0 => $"{seconds / 60}m",
        _ => $"{seconds}s",
    };

    private static string DescribeChannel(AlertRuleWire rule)
    {
        if (rule.ChannelIds.Count > 0)
        {
            return rule.ChannelIds.Count == 1 ? "1 channel" : $"{rule.ChannelIds.Count} channels";
        }

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

        if (!string.IsNullOrWhiteSpace(rule.PagerDutyRoutingKey))
        {
            return "PagerDuty";
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
    internal sealed class Settings : InstanceSettings
    {
        [CommandArgument(0, "<ID>")]
        [Description("The alert rule's id (see `flare alerts list`).")]
        public required Guid Id { get; init; }
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
        AnsiConsole.MarkupLine(result switch
        {
            { NoData: true } => $"[yellow]No data[/]: the condition matched nothing in the last {result.WindowSeconds}s (absent-data alerting)",
            { InsufficientData: true } =>
                $"[yellow]Insufficient data[/]: only {result.DataPointCount} data point(s) in the last {result.WindowSeconds}s - below the rule's minimum, so it can't fire",
            { ConditionKind: "Anomaly", ZScore: { } z, BaselineMean: { } mean } =>
                $"Observed: {FormatNumber(result.ObservedValue)} vs baseline mean {FormatNumber(mean)} (z = {z.ToString("+0.00;-0.00", CultureInfo.InvariantCulture)}, {result.BaselineSampleCount} baseline windows, window: {result.WindowSeconds}s)",
            { ConditionKind: "SloBurnRate" } =>
                $"Burn rate over the long window: {FormatNumber(result.ObservedValue)}x (window: {result.WindowSeconds}s; fires only if the short window is burning too)",
            { ConditionKind: "Anomaly" } =>
                $"[yellow]Not enough history[/]: only {result.BaselineSampleCount} baseline window(s) had data - an anomaly rule needs at least 3 before it can fire",
            _ => $"Observed count: {result.ObservedCount} (window: {result.WindowSeconds}s)",
        });
        AnsiConsole.MarkupLine($"[grey]Evaluated at {result.EvaluatedAt.ToLocalTime():HH:mm:ss.fff} - cooldown untouched, no notification sent.[/]");
        return 0;
    }

    private static string FormatNumber(double? value) => value?.ToString("0.###", CultureInfo.InvariantCulture) ?? "n/a";
}

/// <summary>
/// <c>flare alerts send-test &lt;ID&gt;</c> - actually sends a test notification through a
/// saved alert rule's configured channel via Flare.Api's <c>POST /api/alerts/{id}/send-test</c>.
/// Unlike <see cref="AlertsTestCommand"/> (a condition-only dry-run), this really notifies -
/// with <c>isTest: true</c> wording (see <c>AlertMessageFormatter</c>) so a real
/// Slack/webhook/email/Telegram/PagerDuty recipient doesn't mistake it for a real
/// incident - so a channel's own config (URL, bot token, SMTP address, PagerDuty routing
/// key) can be verified before relying on it for a real breach.
/// </summary>
internal sealed class AlertsSendTestCommand : AsyncCommand<AlertsSendTestCommand.Settings>
{
    internal sealed class Settings : InstanceSettings
    {
        [CommandArgument(0, "<ID>")]
        [Description("The alert rule's id (see `flare alerts list`).")]
        public required Guid Id { get; init; }
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

        AlertNotificationTestResultWire? result;
        try
        {
            using var httpResponse = await http.PostAsync($"/api/alerts/{settings.Id}/send-test", content: null, cancellationToken);

            if (httpResponse.StatusCode == HttpStatusCode.NotFound)
            {
                AnsiConsole.MarkupLine($"[red]✗[/] No alert rule with id {settings.Id}.");
                return 1;
            }

            if (!httpResponse.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[red]✗[/] POST /api/alerts/{settings.Id}/send-test failed: {(int)httpResponse.StatusCode} {httpResponse.ReasonPhrase}");
                return 1;
            }

            result = await httpResponse.Content.ReadFromJsonAsync<AlertNotificationTestResultWire>(WireJsonOptions.Instance, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] Couldn't reach the API on localhost:{port} - is `api` running? Check `flare status`.");
            return 1;
        }

        if (result is null)
        {
            AnsiConsole.MarkupLine("[red]✗[/] Empty response from /api/alerts/{id}/send-test.");
            return 1;
        }

        if (result.Success)
        {
            AnsiConsole.MarkupLine("[green]✓[/] Test notification sent.");
            return 0;
        }

        AnsiConsole.MarkupLine($"[red]✗[/] Send failed: {Markup.Escape(result.Error)}");
        return 1;
    }
}

/// <summary>
/// <c>flare alerts history &lt;ID&gt;</c> - recent fired/resolved events for one rule via
/// <c>GET /api/alerts/{id}/history</c>, including the AI incident summary
/// (<c>docs-internal/adr/0104-ai-incident-summary.md</c>) when the instance generated one.
/// </summary>
internal sealed class AlertsHistoryCommand : AsyncCommand<AlertsHistoryCommand.Settings>
{
    internal sealed class Settings : InstanceSettings
    {
        [CommandArgument(0, "<ID>")]
        [Description("The alert rule's id (see `flare alerts list`).")]
        public required Guid Id { get; init; }

        [CommandOption("--limit <COUNT>")]
        [Description("How many recent events to show. Default 20.")]
        public int Limit { get; init; } = 20;
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

        AlertHistoryResponseWire? response;
        try
        {
            using var httpResponse = await http.GetAsync($"/api/alerts/{settings.Id}/history?limit={Math.Clamp(settings.Limit, 1, 200)}", cancellationToken);

            if (httpResponse.StatusCode == HttpStatusCode.NotFound)
            {
                AnsiConsole.MarkupLine($"[red]✗[/] No alert rule with id {settings.Id}.");
                return 1;
            }

            if (!httpResponse.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[red]✗[/] GET /api/alerts/{settings.Id}/history failed: {(int)httpResponse.StatusCode} {httpResponse.ReasonPhrase}");
                return 1;
            }

            response = await httpResponse.Content.ReadFromJsonAsync<AlertHistoryResponseWire>(WireJsonOptions.Instance, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] Couldn't reach the API on localhost:{port} - is `api` running? Check `flare status`.");
            return 1;
        }

        var events = response?.Events ?? [];
        if (events.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]This rule has never fired.[/]");
            return 0;
        }

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("When (UTC)");
        table.AddColumn("Event");
        table.AddColumn("Observed");
        table.AddColumn("Threshold");
        table.AddColumn("Notification");
        foreach (var e in events.OrderByDescending(e => e.FiredAt))
        {
            table.AddRow(
                e.FiredAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                e.Resolved ? "resolved" : "fired",
                Markup.Escape(e.ObservedValue?.ToString("G4", CultureInfo.InvariantCulture) ?? e.ObservedCount.ToString(CultureInfo.InvariantCulture)),
                Markup.Escape(e.ThresholdValue?.ToString("G4", CultureInfo.InvariantCulture) ?? e.ThresholdCount.ToString(CultureInfo.InvariantCulture)),
                Markup.Escape(e.NotificationStatus));
        }

        AnsiConsole.Write(table);

        foreach (var e in events.Where(e => !string.IsNullOrEmpty(e.AiSummary)).OrderByDescending(e => e.FiredAt))
        {
            AnsiConsole.MarkupLine($"\n[bold]AI summary[/] [grey]({e.FiredAt.UtcDateTime:HH:mm:ss}Z, {Markup.Escape(e.AiModel)})[/]");
            AnsiConsole.WriteLine(e.AiSummary);
        }

        return 0;
    }
}

/// <summary>
/// <c>flare alerts export [--output FILE]</c> - writes every alert rule as the portable JSON
/// document <c>GET /api/alerts/export</c> serves (channels and SLOs by name, no ids or
/// credentials), to stdout or a file, for GitOps or moving rules between instances.
/// </summary>
internal sealed class AlertsExportCommand : AsyncCommand<AlertsExportCommand.Settings>
{
    internal sealed class Settings : InstanceSettings
    {
        [CommandOption("-o|--output <FILE>")]
        [Description("Write the export to this file instead of stdout.")]
        public string? Output { get; init; }
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
        try
        {
            using var response = await http.GetAsync("/api/alerts/export", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[red]✗[/] GET /api/alerts/export failed: {(int)response.StatusCode} {response.ReasonPhrase}");
                return 1;
            }

            var document = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
            var json = JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true });
            if (settings.Output is { Length: > 0 } path)
            {
                await File.WriteAllTextAsync(path, json + Environment.NewLine, cancellationToken);
                AnsiConsole.MarkupLine($"[green]✓[/] Exported {document.GetProperty("rules").GetArrayLength()} rule(s) to {Markup.Escape(path)}");
            }
            else
            {
                Console.Out.WriteLine(json);
            }

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
/// <c>flare alerts import FILE [--dry-run]</c> - posts a document from <c>flare alerts export</c>
/// to <c>POST /api/alerts/import</c> and prints the per-rule create/skip/error summary. Rules
/// whose name already exists are skipped, never overwritten; exit code is 1 if any rule errored.
/// </summary>
internal sealed class AlertsImportCommand : AsyncCommand<AlertsImportCommand.Settings>
{
    internal sealed class Settings : InstanceSettings
    {
        [CommandArgument(0, "<FILE>")]
        [Description("JSON file produced by `flare alerts export`.")]
        public string File { get; init; } = "";

        [CommandOption("--dry-run")]
        [Description("Report what would be created or skipped without creating anything.")]
        public bool DryRun { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var instance = FlareHome.ResolveTarget(settings.InstanceName);
        if (!instance.IsInitialized)
        {
            AnsiConsole.MarkupLine($"[grey]Not initialized yet - run `{instance.StartHint}` first.[/]");
            return 1;
        }

        if (!System.IO.File.Exists(settings.File))
        {
            AnsiConsole.MarkupLine($"[red]✗[/] File not found: {Markup.Escape(settings.File)}");
            return 1;
        }

        var port = instance.ReadEnvValue("FLARE_API_PORT", "8080");
        using var http = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
        try
        {
            using var body = new StringContent(await System.IO.File.ReadAllTextAsync(settings.File, cancellationToken), System.Text.Encoding.UTF8, "application/json");
            using var response = await http.PostAsync($"/api/alerts/import?dryRun={(settings.DryRun ? "true" : "false")}", body, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[red]✗[/] POST /api/alerts/import failed: {(int)response.StatusCode} {response.ReasonPhrase}");
                return 1;
            }

            var result = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
            var table = new Table().Border(TableBorder.Rounded);
            table.AddColumn("Rule");
            table.AddColumn("Outcome");
            table.AddColumn("Detail");
            foreach (var item in result.GetProperty("items").EnumerateArray())
            {
                var outcome = item.GetProperty("outcome").GetString();
                var color = outcome switch { "Create" => "green", "Error" => "red", _ => "grey" };
                table.AddRow(
                    Markup.Escape(item.GetProperty("name").GetString() ?? ""),
                    $"[{color}]{outcome}[/]",
                    Markup.Escape(item.TryGetProperty("message", out var message) ? message.GetString() ?? "" : ""));
            }

            AnsiConsole.Write(table);
            var created = result.GetProperty("created").GetInt32();
            var errors = result.GetProperty("errors").GetInt32();
            var verb = settings.DryRun ? "would create" : "created";
            AnsiConsole.MarkupLine($"{verb} {created}, skipped {result.GetProperty("skipped").GetInt32()}, {errors} error(s){(settings.DryRun ? " [grey](dry run)[/]" : "")}");
            return errors > 0 ? 1 : 0;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] Couldn't reach the API on localhost:{port} - is `api` running? Check `flare status`.");
            return 1;
        }
    }
}

// ---- Wire DTOs - hand-mirror of Flare.Api's Model/AlertModels.cs (see AlertsJsonContext's
// camelCase-properties/PascalCase-string-enum-values convention). Condition (LogFilter) is
// deliberately omitted - not rendered by either command here yet. -------------





internal sealed class AlertTestResultWire
{
    public required ulong ObservedCount { get; init; }

    public required bool WouldFire { get; init; }

    public required DateTimeOffset EvaluatedAt { get; init; }

    public required int WindowSeconds { get; init; }

    /// <summary>True when the rule fired on absent data (<c>AlertRule.NoDataWindowSeconds</c>) - <see cref="WindowSeconds"/> is then the no-data window. Absent from older servers, where it deserializes as false.</summary>
    public bool NoData { get; init; }

    /// <summary>Absent from older servers, where it reads as "LogCount".</summary>
    public string ConditionKind { get; init; } = "LogCount";

    public double? ObservedValue { get; init; }

    /// <summary>True when a metric rule's window had fewer points than its <c>MinDataPoints</c>. Absent from older servers, where it deserializes as false.</summary>
    public bool InsufficientData { get; init; }

    /// <summary>Set only when the rule has <c>MinDataPoints</c> enabled.</summary>
    public ulong? DataPointCount { get; init; }

    /// <summary>Anomaly rules only: null when there wasn't enough history to score (see <see cref="BaselineSampleCount"/>).</summary>
    public double? BaselineMean { get; init; }

    public double? ZScore { get; init; }

    public int BaselineSampleCount { get; init; }
}

internal sealed class AlertNotificationTestResultWire
{
    public required bool Success { get; init; }

    public int StatusCode { get; init; }

    public string Error { get; init; } = "";
}
