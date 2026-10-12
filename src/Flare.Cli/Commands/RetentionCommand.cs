using System.ComponentModel;
using System.Net.Http.Json;
using System.Text.Json;
using Flare.Cli.Internal;
using Flare.Mcp;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Flare.Cli.Commands;

/// <summary>
/// <c>flare retention show/set</c> - data retention via Flare.Api's <c>/api/retention</c>
/// (docs-internal/adr/0143-retention-ttl.md, 0144 cold storage, 0145 per-resource rules). Same
/// surface as Settings &gt; Retention in the dashboard. A change is applied by ClickHouse in the
/// background, so <c>set</c> waits for it by default.
/// </summary>
internal static class RetentionClient
{
    public static HttpClient Create(FlareInstance instance) =>
        new() { BaseAddress = new Uri($"http://localhost:{instance.ReadEnvValue("FLARE_API_PORT", "8080")}") };

    public static async Task<RetentionWire?> GetAsync(HttpClient http, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync("/api/retention", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"GET /api/retention failed: {(int)response.StatusCode} {response.ReasonPhrase}");
        }

        return await response.Content.ReadFromJsonAsync<RetentionWire>(WireJsonOptions.Instance, cancellationToken);
    }

    public static string Days(int? days) => days switch
    {
        null => "-",
        0 => "forever",
        _ => $"{days}d",
    };
}

internal sealed record RetentionRuleWire(string Attribute, string Value, int Days);

internal sealed record SignalRetentionWire(
    string Signal,
    int? ActualDays,
    int? ActualColdAfterDays,
    string ActualState,
    int? ExpectedDays,
    int? ExpectedColdAfterDays,
    List<RetentionRuleWire> ActualRules,
    List<RetentionRuleWire> ExpectedRules,
    string? Status,
    string? Error);

internal sealed record StorageDiskWire(string Name, string Type, long FreeBytes, long TotalBytes);

internal sealed record ColdStorageWire(bool Available, List<StorageDiskWire> Disks);

internal sealed record RetentionWire(List<SignalRetentionWire> Signals, ColdStorageWire ColdStorage);

internal sealed class RetentionShowCommand : AsyncCommand<RetentionShowCommand.Settings>
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

        using var http = RetentionClient.Create(instance);
        RetentionWire? retention;
        try
        {
            retention = await RetentionClient.GetAsync(http, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] {Markup.Escape(ex.Message)} - is `api` running? Check `flare status`.");
            return 1;
        }

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Signal");
        table.AddColumn("In ClickHouse");
        table.AddColumn("Cold after");
        table.AddColumn("Rules");
        table.AddColumn("Last change");

        foreach (var signal in retention?.Signals ?? [])
        {
            var actual = signal.ActualState switch
            {
                "custom" => "[yellow]custom TTL[/]",
                "none" => "no TTL",
                _ => RetentionClient.Days(signal.ActualDays),
            };
            var status = signal.Status switch
            {
                "pending" => "[yellow]applying…[/]",
                "failed" => $"[red]failed[/] {Markup.Escape(signal.Error ?? "")}",
                "success" => "[green]applied[/]",
                _ => "[grey]never set[/]",
            };
            var rules = signal.ActualRules.Count == 0
                ? "[grey]-[/]"
                : string.Join(", ", signal.ActualRules.Select(r => Markup.Escape($"{r.Attribute}={r.Value}:{RetentionClient.Days(r.Days)}")));
            table.AddRow(
                signal.Signal,
                actual,
                signal.ActualColdAfterDays is > 0 ? $"{signal.ActualColdAfterDays}d" : "[grey]-[/]",
                rules,
                status);
        }

        AnsiConsole.Write(table);

        if (retention?.ColdStorage.Available == true)
        {
            AnsiConsole.MarkupLine($"[grey]Cold storage: {string.Join(", ", retention.ColdStorage.Disks.Select(d => $"{d.Name} ({d.Type})"))}[/]");
        }
        else
        {
            AnsiConsole.MarkupLine("[grey]Cold storage: not configured.[/]");
        }

        return 0;
    }
}

/// <summary>
/// <c>flare retention set &lt;SIGNAL&gt; --days N</c>. Options you leave out keep the signal's
/// current cold-after and rules (a bare PUT would reset both), so <c>--days 14</c> alone changes
/// only the default. <c>--clear-rules</c> / <c>--cold-after 0</c> remove them.
/// </summary>
internal sealed class RetentionSetCommand : AsyncCommand<RetentionSetCommand.Settings>
{
    internal sealed class Settings : InstanceSettings
    {
        [CommandArgument(0, "<SIGNAL>")]
        [Description("logs, traces, metrics or profiles.")]
        public required string Signal { get; init; }

        [CommandOption("--days <DAYS>")]
        [Description("Keep this signal for DAYS days; 0 keeps it forever. With rules, this is the default for rows no rule matches.")]
        public int? Days { get; init; }

        [CommandOption("--cold-after <DAYS>")]
        [Description("Move data to cold storage after DAYS days; 0 turns tiering off. Needs cold storage configured.")]
        public int? ColdAfter { get; init; }

        [CommandOption("--rule <RULE>")]
        [Description("Per-resource rule ATTRIBUTE=VALUE:DAYS, e.g. deployment.environment=dev:7. Repeatable; first match wins. Replaces the existing rules.")]
        public string[] Rules { get; init; } = [];

        [CommandOption("--clear-rules")]
        [Description("Remove all per-resource rules.")]
        public bool ClearRules { get; init; }

        [CommandOption("--no-wait")]
        [Description("Return as soon as the API accepts the change instead of waiting for ClickHouse to apply it.")]
        public bool NoWait { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var instance = FlareHome.ResolveTarget(settings.InstanceName);
        if (!instance.IsInitialized)
        {
            AnsiConsole.MarkupLine($"[grey]Not initialized yet - run `{instance.StartHint}` first.[/]");
            return 1;
        }

        if (settings.Days is null && settings.ColdAfter is null && settings.Rules.Length == 0 && !settings.ClearRules)
        {
            AnsiConsole.MarkupLine("[red]✗[/] Nothing to change - pass --days, --cold-after, --rule or --clear-rules.");
            return 1;
        }

        var rules = new List<RetentionRuleWire>();
        foreach (var text in settings.Rules)
        {
            if (!TryParseRule(text, out var rule))
            {
                AnsiConsole.MarkupLine($"[red]✗[/] Bad --rule '{Markup.Escape(text)}': expected ATTRIBUTE=VALUE:DAYS, e.g. deployment.environment=dev:7.");
                return 1;
            }

            rules.Add(rule);
        }

        using var http = RetentionClient.Create(instance);
        try
        {
            var current = (await RetentionClient.GetAsync(http, cancellationToken))?.Signals
                .FirstOrDefault(s => string.Equals(s.Signal, settings.Signal, StringComparison.OrdinalIgnoreCase));
            if (current is null)
            {
                AnsiConsole.MarkupLine($"[red]✗[/] Unknown signal '{Markup.Escape(settings.Signal)}'. Expected logs, traces, metrics or profiles.");
                return 1;
            }

            var days = settings.Days ?? current.ExpectedDays ?? current.ActualDays;
            if (days is null)
            {
                AnsiConsole.MarkupLine("[red]✗[/] This signal has no retention yet - pass --days.");
                return 1;
            }

            var coldAfter = settings.ColdAfter ?? current.ExpectedColdAfterDays ?? current.ActualColdAfterDays ?? 0;
            var keptRules = settings.ClearRules ? [] : settings.Rules.Length > 0 ? rules : current.ExpectedRules.Count > 0 ? current.ExpectedRules : current.ActualRules;
            var name = current.Signal;

            var body = new
            {
                signals = new Dictionary<string, int> { [name] = days.Value },
                coldAfterDays = new Dictionary<string, int> { [name] = coldAfter },
                rules = new Dictionary<string, List<RetentionRuleWire>> { [name] = keptRules },
            };

            using var put = await http.PutAsJsonAsync("/api/retention", body, WireJsonOptions.Instance, cancellationToken);
            if (!put.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[red]✗[/] {Markup.Escape(await ProblemDetailAsync(put, cancellationToken))}");
                return 1;
            }

            if (settings.NoWait)
            {
                AnsiConsole.MarkupLine($"[green]✓[/] Retention change for {name} accepted; ClickHouse is applying it. Check `flare retention show`.");
                return 0;
            }

            return await WaitAsync(http, name, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] {Markup.Escape(ex.Message)} - is `api` running? Check `flare status`.");
            return 1;
        }
    }

    private static async Task<int> WaitAsync(HttpClient http, string signal, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddMinutes(2);
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            var state = (await RetentionClient.GetAsync(http, cancellationToken))?.Signals
                .FirstOrDefault(s => string.Equals(s.Signal, signal, StringComparison.OrdinalIgnoreCase));
            switch (state?.Status)
            {
                case "success":
                    AnsiConsole.MarkupLine($"[green]✓[/] Retention for {signal} is now {RetentionClient.Days(state.ActualDays)}.");
                    return 0;
                case "failed":
                    AnsiConsole.MarkupLine($"[red]✗[/] ClickHouse rejected the change: {Markup.Escape(state.Error ?? "unknown error")}");
                    return 1;
            }
        }

        AnsiConsole.MarkupLine("[yellow]Still applying after 2 minutes.[/] Check `flare retention show`.");
        return 1;
    }

    /// <summary>ATTRIBUTE=VALUE:DAYS - split on the first '=' and the last ':', so values may contain ':'.</summary>
    internal static bool TryParseRule(string text, out RetentionRuleWire rule)
    {
        rule = null!;
        var eq = text.IndexOf('=');
        var colon = text.LastIndexOf(':');
        if (eq <= 0 || colon <= eq + 1 || !int.TryParse(text[(colon + 1)..], out var days) || days < 0)
        {
            return false;
        }

        rule = new RetentionRuleWire(text[..eq], text[(eq + 1)..colon], days);
        return true;
    }

    private static async Task<string> ProblemDetailAsync(HttpResponseMessage response, CancellationToken cancellationToken)
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
            // Not a problem-details body.
        }

        return $"PUT /api/retention failed: {(int)response.StatusCode} {response.ReasonPhrase}";
    }
}
