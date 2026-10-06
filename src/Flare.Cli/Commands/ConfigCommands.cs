using System.ComponentModel;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Flare.Cli.Internal;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Flare.Cli.Commands;

/// <summary>
/// Shared plumbing for <c>flare config export</c>/<c>apply</c>: the same list/create/update REST
/// endpoints the dashboard uses, read and written as raw JSON so every resource family is one code
/// path (the per-family differences live in <see cref="ConfigSync"/>).
/// </summary>
internal sealed class ConfigApi(HttpClient http, CancellationToken cancellationToken)
{
    public async Task<JsonNode?> GetAsync(string path)
    {
        using var response = await http.GetAsync(path, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new ConfigApiException($"GET {path} failed: {await DescribeAsync(response)}");
        }

        return await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken);
    }

    /// <summary>Sends <paramref name="body"/>; returns null on success, otherwise a one-line reason.</summary>
    public async Task<(JsonNode? Result, string? Error)> SendAsync(HttpMethod method, string path, JsonNode body)
    {
        using var request = new HttpRequestMessage(method, path)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return (null, await DescribeAsync(response));
        }

        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        return (text.Length > 0 ? JsonNode.Parse(text) : null, null);
    }

    public async Task<List<JsonObject>> ListAsync(string path, string property) =>
        [.. ((await GetAsync(path))?[property] as JsonArray ?? []).OfType<JsonObject>()];

    private static async Task<string> DescribeAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        try
        {
            if (JsonNode.Parse(text)?["detail"]?.GetValue<string>() is { Length: > 0 } detail)
            {
                return $"{(int)response.StatusCode} {detail}";
            }
        }
        catch (JsonException)
        {
        }

        return $"{(int)response.StatusCode} {response.ReasonPhrase}";
    }
}

internal sealed class ConfigApiException(string message) : Exception(message);

/// <summary>
/// <c>flare config export [--output FILE]</c> - writes the instance's alert rules, notification
/// channels, SLOs, pipeline rules, metric attribute rules, maintenance windows and ingest-key limits
/// as one portable JSON document for git or another instance. References are by name, ids and
/// timestamps are dropped, and credentials become <c>${ENV_VAR}</c> placeholders - never values.
/// </summary>
internal sealed class ConfigExportCommand : AsyncCommand<ConfigExportCommand.Settings>
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
        var api = new ConfigApi(http, cancellationToken);
        try
        {
            var alertNames = (await api.ListAsync("/api/alerts", "rules"))
                .ToDictionary(r => r["id"]!.GetValue<string>(), r => r["name"]!.GetValue<string>());

            var document = new JsonObject { ["version"] = ConfigSync.CurrentVersion };
            var counts = new List<string>();
            void Add(string section, IEnumerable<JsonObject> items)
            {
                var array = new JsonArray([.. items.OrderBy(i => ConfigSync.NameOf(i, section), StringComparer.OrdinalIgnoreCase)]);
                if (array.Count > 0)
                {
                    document[section] = array;
                    counts.Add($"{array.Count} {section}");
                }
            }

            foreach (var kind in ConfigSync.RestKinds)
            {
                Add(kind.Section, (await api.ListAsync(kind.ListPath, kind.ListProperty)).Select(i => ConfigSync.ToExportItem(kind, i, alertNames)));
            }

            Add(ConfigSync.AlertsSection, ((await api.GetAsync("/api/alerts/export"))?["rules"] as JsonArray ?? []).OfType<JsonObject>().Select(i => (JsonObject)i.DeepClone()));
            Add(ConfigSync.WindowsSection, (await api.ListAsync(ConfigSync.Windows.ListPath, ConfigSync.Windows.ListProperty)).Select(i => ConfigSync.ToExportItem(ConfigSync.Windows, i, alertNames)));
            Add(ConfigSync.IngestKeysSection, (await api.ListAsync("/api/ingest-keys", "keys")).Select(ConfigSync.ToExportKey).OfType<JsonObject>());

            var json = document.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            if (settings.Output is { Length: > 0 } path)
            {
                await File.WriteAllTextAsync(path, json + Environment.NewLine, cancellationToken);
                AnsiConsole.MarkupLine($"[green]✓[/] Exported {Markup.Escape(string.Join(", ", counts))} to {Markup.Escape(path)}");
                AnsiConsole.MarkupLine("[grey]Credentials are ${ENV_VAR} placeholders - set them in the environment when you run `flare config apply`.[/]");
            }
            else
            {
                Console.Out.WriteLine(json);
            }

            return 0;
        }
        catch (ConfigApiException ex)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] {Markup.Escape(ex.Message)}");
            return 1;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] Couldn't reach the API on localhost:{port} - is `api` running? Check `flare status`.");
            return 1;
        }
    }
}

/// <summary>
/// <c>flare config apply -f FILE [--dry-run]</c> - makes the instance match a <c>flare config export</c>
/// file: missing resources are created and existing ones (matched by name) updated, in dependency
/// order (channels and SLOs, then alert rules, then maintenance windows). Resources absent from the
/// file are left alone - there is no prune. <c>${VAR}</c> in any string is read from the environment.
/// Exit code is 1 if any entry errored.
/// </summary>
internal sealed class ConfigApplyCommand : AsyncCommand<ConfigApplyCommand.Settings>
{
    internal sealed class Settings : InstanceSettings
    {
        [CommandOption("-f|--file <FILE>")]
        [Description("Config file produced by `flare config export`.")]
        public string File { get; init; } = "";

        [CommandOption("--dry-run")]
        [Description("Report what would be created or updated without changing anything.")]
        public bool DryRun { get; init; }
    }

    private static readonly string[] KnownSections =
    [
        .. ConfigSync.RestKinds.Select(k => k.Section),
        ConfigSync.AlertsSection,
        ConfigSync.WindowsSection,
        ConfigSync.IngestKeysSection,
    ];

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (settings.File.Length == 0)
        {
            AnsiConsole.MarkupLine("[red]✗[/] Pass the config file with -f FILE.");
            return 1;
        }

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

        JsonObject document;
        try
        {
            document = JsonNode.Parse(await System.IO.File.ReadAllTextAsync(settings.File, cancellationToken)) as JsonObject
                ?? throw new JsonException("Top level must be an object.");
        }
        catch (JsonException ex)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] {Markup.Escape(settings.File)} isn't valid JSON: {Markup.Escape(ex.Message)}");
            return 1;
        }

        if (document["version"]?.GetValue<int>() != ConfigSync.CurrentVersion)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] Unsupported config version (expected {ConfigSync.CurrentVersion}).");
            return 1;
        }

        var unknown = document.Select(p => p.Key).Where(k => k != "version" && !KnownSections.Contains(k)).ToList();
        if (unknown.Count > 0)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] Unknown section(s): {Markup.Escape(string.Join(", ", unknown))}. Known: {Markup.Escape(string.Join(", ", KnownSections))}.");
            return 1;
        }

        var port = instance.ReadEnvValue("FLARE_API_PORT", "8080");
        using var http = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
        var api = new ConfigApi(http, cancellationToken);
        try
        {
            var rows = await ApplyAsync(api, document, settings.DryRun);

            var table = new Table().Border(TableBorder.Rounded);
            table.AddColumn("Section");
            table.AddColumn("Name");
            table.AddColumn("Outcome");
            table.AddColumn("Detail");
            foreach (var row in rows)
            {
                var color = row.Outcome switch { ConfigOutcome.Create => "green", ConfigOutcome.Update => "yellow", ConfigOutcome.Error => "red", _ => "grey" };
                table.AddRow(Markup.Escape(row.Section), Markup.Escape(row.Name), $"[{color}]{row.Outcome}[/]", Markup.Escape(row.Detail ?? ""));
            }

            AnsiConsole.Write(table);
            int Count(ConfigOutcome o) => rows.Count(r => r.Outcome == o);
            var (verbC, verbU) = settings.DryRun ? ("would create", "would update") : ("created", "updated");
            AnsiConsole.MarkupLine($"{verbC} {Count(ConfigOutcome.Create)}, {verbU} {Count(ConfigOutcome.Update)}, unchanged {Count(ConfigOutcome.Unchanged)}, skipped {Count(ConfigOutcome.Skip)}, {Count(ConfigOutcome.Error)} error(s){(settings.DryRun ? " [grey](dry run)[/]" : "")}");
            return Count(ConfigOutcome.Error) > 0 ? 1 : 0;
        }
        catch (ConfigApiException ex)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] {Markup.Escape(ex.Message)}");
            return 1;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] Couldn't reach the API on localhost:{port} - is `api` running? Check `flare status`.");
            return 1;
        }
    }

    private static List<JsonObject> Section(JsonObject document, string section) =>
        [.. (document[section] as JsonArray ?? []).OfType<JsonObject>()];

    private static async Task<List<ConfigPlanItem>> ApplyAsync(ConfigApi api, JsonObject document, bool dryRun)
    {
        static string? Env(string name) => Environment.GetEnvironmentVariable(name);

        var rows = new List<ConfigPlanItem>();
        var empty = new Dictionary<string, string>();
        var pendingChannels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pendingSlos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var existingChannels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var existingSlos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var kind in ConfigSync.RestKinds)
        {
            var desired = Section(document, kind.Section);
            if (desired.Count == 0)
            {
                continue;
            }

            var existing = await FetchExistingAsync(api, kind, empty);
            if (kind.Section == "notificationChannels")
            {
                existingChannels.UnionWith(existing.Select(e => e.Name));
            }
            else if (kind.Section == "slos")
            {
                existingSlos.UnionWith(existing.Select(e => e.Name));
            }

            var plan = await ApplyKindAsync(api, kind, desired, existing, empty, Env, dryRun, allowPendingRules: false);
            rows.AddRange(plan);
            var created = plan.Where(p => p.Outcome == ConfigOutcome.Create).Select(p => p.Name);
            (kind.Section == "notificationChannels" ? pendingChannels : kind.Section == "slos" ? pendingSlos : []).UnionWith(created);
        }

        var pendingAlerts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var alerts = Section(document, ConfigSync.AlertsSection);
        if (alerts.Count > 0)
        {
            var alertRows = await ApplyAlertsAsync(api, alerts, dryRun, existingChannels, pendingChannels, existingSlos, pendingSlos, Env);
            rows.AddRange(alertRows);
            pendingAlerts.UnionWith(alertRows.Where(r => r.Outcome == ConfigOutcome.Create).Select(r => r.Name));
        }

        var windows = Section(document, ConfigSync.WindowsSection);
        if (windows.Count > 0)
        {
            var alertList = await api.ListAsync("/api/alerts", "rules");
            var alertIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var a in alertList)
            {
                alertIds.TryAdd(a["name"]!.GetValue<string>(), a["id"]!.GetValue<string>());
            }

            var alertNamesById = alertIds.ToDictionary(p => p.Value, p => p.Key);
            var existing = await FetchExistingAsync(api, ConfigSync.Windows, alertNamesById);
            foreach (var name in pendingAlerts)
            {
                alertIds.TryAdd(name, Guid.Empty.ToString());
            }

            rows.AddRange(await ApplyKindAsync(api, ConfigSync.Windows, windows, existing, alertIds, Env, dryRun, allowPendingRules: false));
        }

        var keys = Section(document, ConfigSync.IngestKeysSection);
        if (keys.Count > 0)
        {
            var existing = (await api.ListAsync("/api/ingest-keys", "keys"))
                .Select(k => (Key: k, Export: ConfigSync.ToExportKey(k)))
                .Where(k => k.Export is not null)
                .Select(k => new ConfigExisting(k.Key["id"]!.GetValue<string>(), k.Key["name"]!.GetValue<string>(), k.Key, k.Export!))
                .ToList();
            foreach (var item in ConfigSync.PlanKeys(keys, existing))
            {
                var result = item;
                if (item.Outcome == ConfigOutcome.Update && !dryRun)
                {
                    var desired = keys.First(k => string.Equals(ConfigSync.NameOf(k, ConfigSync.IngestKeysSection), item.Name, StringComparison.OrdinalIgnoreCase));
                    var (_, error) = await api.SendAsync(HttpMethod.Put, $"/api/ingest-keys/{item.ExistingId}/limits", ConfigSync.KeyLimitsBody(desired));
                    result = error is null ? item : item with { Outcome = ConfigOutcome.Error, Detail = error };
                }

                rows.Add(result);
            }
        }

        return rows;
    }

    private static async Task<List<ConfigExisting>> FetchExistingAsync(ConfigApi api, ConfigKind kind, IReadOnlyDictionary<string, string> alertNamesById) =>
        [.. (await api.ListAsync(kind.ListPath, kind.ListProperty))
            .Select(i => new ConfigExisting(i["id"]!.GetValue<string>(), i["name"]!.GetValue<string>(), i, ConfigSync.ToExportItem(kind, i, alertNamesById)))];

    /// <summary>Plans one REST-shaped kind and, unless <paramref name="dryRun"/>, performs the creates and updates.</summary>
    private static async Task<List<ConfigPlanItem>> ApplyKindAsync(
        ConfigApi api,
        ConfigKind kind,
        List<JsonObject> desired,
        List<ConfigExisting> existing,
        IReadOnlyDictionary<string, string> alertIdsByName,
        Func<string, string?> env,
        bool dryRun,
        bool allowPendingRules)
    {
        var byName = ConfigSync.ByName(existing);
        var plan = ConfigSync.Plan(kind, desired, existing, env);
        for (var i = 0; i < plan.Count; i++)
        {
            var item = plan[i];
            if (item.Outcome is not (ConfigOutcome.Create or ConfigOutcome.Update))
            {
                continue;
            }

            var source = desired.First(d => string.Equals(ConfigSync.NameOf(d, kind.Section), item.Name, StringComparison.OrdinalIgnoreCase));
            byName.TryGetValue(item.Name, out var current);
            var (body, _, buildError) = ConfigSync.BuildBody(kind, source, current, env, alertIdsByName, allowPendingRules);
            if (buildError is not null)
            {
                plan[i] = item with { Outcome = ConfigOutcome.Error, Detail = buildError };
                continue;
            }

            if (dryRun)
            {
                continue;
            }

            var (_, error) = item.Outcome == ConfigOutcome.Create
                ? await api.SendAsync(HttpMethod.Post, kind.ListPath, body!)
                : await api.SendAsync(HttpMethod.Put, $"{kind.ItemPath}/{item.ExistingId}", body!);
            if (error is not null)
            {
                plan[i] = item with { Outcome = ConfigOutcome.Error, Detail = error };
            }
        }

        return plan;
    }

    /// <summary>
    /// Alert rules go through the server's by-name import (<c>update=true</c>): it resolves channel and
    /// SLO names to ids. Only new or changed rules are sent; on a dry run, a rule that only fails
    /// because the channel/SLO it names is still pending creation in this same file counts as a create.
    /// </summary>
    private static async Task<List<ConfigPlanItem>> ApplyAlertsAsync(
        ConfigApi api,
        List<JsonObject> desired,
        bool dryRun,
        HashSet<string> existingChannels,
        HashSet<string> pendingChannels,
        HashSet<string> existingSlos,
        HashSet<string> pendingSlos,
        Func<string, string?> env)
    {
        var existingItems = ((await api.GetAsync("/api/alerts/export"))?["rules"] as JsonArray ?? []).OfType<JsonObject>()
            .Select(i => new ConfigExisting("", ConfigSync.NameOf(i, ConfigSync.AlertsSection), i, i))
            .ToList();
        var plan = ConfigSync.Plan(ConfigSync.Alerts, desired, existingItems, env);
        var sendNames = plan.Where(p => p.Outcome is ConfigOutcome.Create or ConfigOutcome.Update).Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (sendNames.Count == 0)
        {
            return plan;
        }

        // The server only knows the instance's current channels/SLOs, so channels created earlier in
        // this run (non-dry) are visible to it; on a dry run they aren't, hence the pending sets.
        var toSend = desired.Where(d => sendNames.Contains(ConfigSync.NameOf(d, ConfigSync.AlertsSection))).Select(d => (JsonNode)d.DeepClone()).ToArray();
        var payload = new JsonObject { ["version"] = 1, ["rules"] = new JsonArray(toSend) };
        var (result, error) = await api.SendAsync(HttpMethod.Post, $"/api/alerts/import?dryRun={(dryRun ? "true" : "false")}&update=true", payload);
        if (error is not null)
        {
            throw new ConfigApiException($"POST /api/alerts/import failed: {error}");
        }

        var outcomes = (result?["items"] as JsonArray ?? []).OfType<JsonObject>()
            .GroupBy(i => i["name"]?.GetValue<string>() ?? "", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < plan.Count; i++)
        {
            if (!outcomes.TryGetValue(plan[i].Name, out var served))
            {
                continue;
            }

            var message = served["message"]?.GetValue<string>();
            var outcome = Enum.Parse<ConfigOutcome>(served["outcome"]!.GetValue<string>());
            if (dryRun && outcome == ConfigOutcome.Error && message is not null && message.StartsWith("Unknown", StringComparison.Ordinal))
            {
                var source = desired.First(d => string.Equals(ConfigSync.NameOf(d, ConfigSync.AlertsSection), plan[i].Name, StringComparison.OrdinalIgnoreCase));
                var channels = (source["channels"] as JsonArray ?? []).Select(n => n?.GetValue<string>() ?? "");
                var slo = source["sloName"]?.GetValue<string>();
                var unresolvable = channels.Where(c => !existingChannels.Contains(c) && !pendingChannels.Contains(c)).ToList();
                if (slo is not null && !existingSlos.Contains(slo) && !pendingSlos.Contains(slo))
                {
                    unresolvable.Add(slo);
                }

                if (unresolvable.Count == 0)
                {
                    plan[i] = plan[i] with { Detail = "channel/SLO is created earlier in this file" };
                    continue;
                }
            }

            plan[i] = plan[i] with { Outcome = outcome, Detail = message ?? plan[i].Detail };
        }

        return plan;
    }
}
