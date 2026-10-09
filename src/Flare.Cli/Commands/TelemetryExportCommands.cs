using System.ComponentModel;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flare.Cli.Internal;
using Flare.Mcp;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Flare.Cli.Commands;

/// <summary>
/// <c>flare forwarding list/create/update/delete/status</c> and <c>flare archive show/set/reset/status</c> - the
/// managed telemetry export surface of ADR-0157 (<c>/api/forwarding/*</c>, <c>/api/archive/*</c>), the same
/// settings as Settings &gt; Telemetry export in the dashboard. Header values and S3 keys come back masked; an
/// update that leaves them alone keeps the stored value, so <c>update</c> and <c>set</c> only change what an
/// option was passed for.
/// </summary>
internal static class TelemetryExportClient
{
    public const string ValidSignals = "logs, traces, metrics";

    /// <summary>Resolves the target instance, or prints why not and returns null.</summary>
    public static HttpClient? Create(string? instanceName)
    {
        var instance = FlareHome.ResolveTarget(instanceName);
        if (!instance.IsInitialized)
        {
            AnsiConsole.MarkupLine($"[grey]Not initialized yet - run `{instance.StartHint}` first.[/]");
            return null;
        }

        return new HttpClient { BaseAddress = new Uri($"http://localhost:{instance.ReadEnvValue("FLARE_API_PORT", "8080")}") };
    }

    /// <summary>Runs one API call; on a non-2xx or unreachable API prints the error and returns null.</summary>
    public static async Task<HttpResponseMessage?> SendAsync(HttpClient http, HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(method, path);
            if (body is not null)
            {
                request.Content = JsonContent.Create(body, options: WireJsonOptions.Instance);
            }

            var response = await http.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return response;
            }

            var detail = await NotificationChannelTypeParsing.TryReadProblemDetailAsync(response, cancellationToken);
            AnsiConsole.MarkupLine($"[red]✗[/] {method} {path} failed: {(int)response.StatusCode} {Markup.Escape(detail ?? response.ReasonPhrase ?? "")}");
            response.Dispose();
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] {Markup.Escape(ex.Message)} - is `api` running? Check `flare status`.");
            return null;
        }
    }

    public static async Task<T?> GetAsync<T>(HttpClient http, string path, CancellationToken cancellationToken)
        where T : class
    {
        using var response = await SendAsync(http, HttpMethod.Get, path, null, cancellationToken);
        return response is null ? null : await response.Content.ReadFromJsonAsync<T>(WireJsonOptions.Instance, cancellationToken);
    }

    /// <summary>Normalizes <c>--signal</c> values to the API's enum names; null when one is unknown.</summary>
    public static List<string>? ParseSignals(IEnumerable<string>? values)
    {
        var signals = new List<string>();
        foreach (var value in values ?? [])
        {
            switch (value.Trim().ToLowerInvariant())
            {
                case "logs": signals.Add("Logs"); break;
                case "traces": signals.Add("Traces"); break;
                case "metrics": signals.Add("Metrics"); break;
                default:
                    AnsiConsole.MarkupLine($"[red]✗[/] Unknown signal '{Markup.Escape(value)}' - use {ValidSignals}.");
                    return null;
            }
        }

        return signals.Distinct().ToList();
    }

    public static string SignalList(List<string> signals) => signals.Count == 0 ? "all" : string.Join(", ", signals.Select(s => s.ToLowerInvariant()));

    public static bool ConfirmDelete(bool yes, string what)
    {
        if (yes)
        {
            return true;
        }

        if (!AnsiConsole.Profile.Capabilities.Interactive)
        {
            AnsiConsole.MarkupLine("[red]Refusing to delete without --yes on a non-interactive invocation.[/]");
            return false;
        }

        if (AnsiConsole.Confirm($"Delete {what}? Continue?", defaultValue: false))
        {
            return true;
        }

        AnsiConsole.MarkupLine("[grey]Aborted - nothing was removed.[/]");
        return false;
    }

    public static string Ago(DateTimeOffset? at) => at is null ? "-" : at.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

    /// <summary>Parses repeated <c>NAME=VALUE</c> header options; null (after printing why) when one is malformed.</summary>
    public static Dictionary<string, string>? ParseHeaders(IEnumerable<string>? values)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values ?? [])
        {
            var split = value.IndexOf('=');
            if (split <= 0)
            {
                AnsiConsole.MarkupLine($"[red]✗[/] Header '{Markup.Escape(value)}' must look like NAME=VALUE.");
                return null;
            }

            headers[value[..split].Trim()] = value[(split + 1)..];
        }

        return headers;
    }
}

internal sealed class ForwardingListCommand : AsyncCommand<ForwardingListCommand.Settings>
{
    internal sealed class Settings : InstanceSettings
    {
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        using var http = TelemetryExportClient.Create(settings.InstanceName);
        if (http is null)
        {
            return 1;
        }

        var response = await TelemetryExportClient.GetAsync<ForwardingTargetListWire>(http, "/api/forwarding/targets", cancellationToken);
        if (response is null)
        {
            return 1;
        }

        if (response.Targets.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]No managed forwarding targets. Targets in Forwarding:Targets configuration are not listed here - see `flare forwarding status`.[/]");
            return 0;
        }

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Name");
        table.AddColumn("Endpoint");
        table.AddColumn("Signals");
        table.AddColumn("Services");
        table.AddColumn("Enabled");
        table.AddColumn("Id");
        foreach (var target in response.Targets)
        {
            table.AddRow(
                Markup.Escape(target.Name),
                Markup.Escape(target.Endpoint),
                TelemetryExportClient.SignalList(target.Signals),
                target.Services.Count == 0 ? "[grey]all[/]" : Markup.Escape(string.Join(", ", target.Services)),
                target.Enabled ? "yes" : "[grey]no[/]",
                $"[grey]{target.Id}[/]");
        }

        AnsiConsole.Write(table);
        return 0;
    }
}

internal sealed class ForwardingCreateCommand : AsyncCommand<ForwardingCreateCommand.Settings>
{
    internal sealed class Settings : InstanceSettings
    {
        [CommandArgument(0, "<NAME>")]
        [Description("A label for this target, e.g. 'grafana-cloud'. Unique (case-insensitive).")]
        public required string Target { get; init; }

        [CommandOption("--endpoint <URL>")]
        [Description("The destination's OTLP/HTTP base URL (http or https). Required.")]
        public string? Endpoint { get; init; }

        [CommandOption("--header <NAME=VALUE>")]
        [Description("A request header, e.g. Authorization='Bearer ...'. Repeatable.")]
        public string[] Headers { get; init; } = [];

        [CommandOption("--signal <SIGNAL>")]
        [Description("Forward only this signal: logs, traces or metrics. Repeatable; omit for all.")]
        public string[] Signals { get; init; } = [];

        [CommandOption("--service <SERVICE>")]
        [Description("Forward only this service's telemetry. Repeatable; omit for every service.")]
        public string[] Services { get; init; } = [];

        [CommandOption("--ingest-key-id <ID>")]
        [Description("Forward only requests that arrived with this ingest key. Repeatable; omit for any request.")]
        public Guid[] IngestKeyIds { get; init; } = [];

        [CommandOption("--no-gzip")]
        [Description("Send request bodies uncompressed.")]
        public bool NoGzip { get; init; }

        [CommandOption("--disabled")]
        [Description("Create the target switched off.")]
        public bool Disabled { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.Endpoint))
        {
            AnsiConsole.MarkupLine("[red]✗[/] --endpoint is required.");
            return 1;
        }

        var headers = TelemetryExportClient.ParseHeaders(settings.Headers);
        var signals = TelemetryExportClient.ParseSignals(settings.Signals);
        if (headers is null || signals is null)
        {
            return 1;
        }

        using var http = TelemetryExportClient.Create(settings.InstanceName);
        if (http is null)
        {
            return 1;
        }

        var request = new ForwardingTargetRequestWire
        {
            Name = settings.Target,
            Endpoint = settings.Endpoint,
            Enabled = !settings.Disabled,
            Headers = headers,
            Signals = signals,
            Services = settings.Services.ToList(),
            IngestKeyIds = settings.IngestKeyIds.ToList(),
            Gzip = !settings.NoGzip,
        };

        using var response = await TelemetryExportClient.SendAsync(http, HttpMethod.Post, "/api/forwarding/targets", request, cancellationToken);
        if (response is null)
        {
            return 1;
        }

        var created = await response.Content.ReadFromJsonAsync<ForwardingTargetWire>(WireJsonOptions.Instance, cancellationToken);
        AnsiConsole.MarkupLine($"[green]✓[/] Created forwarding target [bold]{Markup.Escape(created?.Name ?? settings.Target)}[/] (id: {created?.Id}).");
        return 0;
    }
}

/// <summary>
/// <c>flare forwarding update &lt;ID&gt;</c> fetches the target and replaces only what an option was passed for
/// (the API's PUT replaces the whole target). A passed <c>--header</c>, <c>--signal</c>, <c>--service</c> or
/// <c>--ingest-key-id</c> list replaces that list; <c>--clear-*</c> empties it. Headers not passed keep their
/// stored values because the masked GET value is sent back.
/// </summary>
internal sealed class ForwardingUpdateCommand : AsyncCommand<ForwardingUpdateCommand.Settings>
{
    internal sealed class Settings : InstanceSettings
    {
        [CommandArgument(0, "<ID>")]
        [Description("The target's id (see `flare forwarding list`).")]
        public required Guid Id { get; init; }

        [CommandOption("--rename <NAME>")]
        public string? Rename { get; init; }

        [CommandOption("--endpoint <URL>")]
        public string? Endpoint { get; init; }

        [CommandOption("--header <NAME=VALUE>")]
        [Description("Set a header (keeps the other existing headers). Repeatable.")]
        public string[] Headers { get; init; } = [];

        [CommandOption("--clear-headers")]
        [Description("Remove every header (combine with --header to start over).")]
        public bool ClearHeaders { get; init; }

        [CommandOption("--signal <SIGNAL>")]
        [Description("Replace the signal filter. Repeatable.")]
        public string[] Signals { get; init; } = [];

        [CommandOption("--all-signals")]
        [Description("Forward all signals.")]
        public bool AllSignals { get; init; }

        [CommandOption("--service <SERVICE>")]
        [Description("Replace the service filter. Repeatable.")]
        public string[] Services { get; init; } = [];

        [CommandOption("--all-services")]
        [Description("Forward every service.")]
        public bool AllServices { get; init; }

        [CommandOption("--ingest-key-id <ID>")]
        [Description("Replace the ingest-key filter. Repeatable.")]
        public Guid[] IngestKeyIds { get; init; } = [];

        [CommandOption("--any-ingest-key")]
        [Description("Forward requests regardless of ingest key.")]
        public bool AnyIngestKey { get; init; }

        [CommandOption("--gzip <BOOL>")]
        public bool? Gzip { get; init; }

        [CommandOption("--enabled <BOOL>")]
        public bool? Enabled { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var newHeaders = TelemetryExportClient.ParseHeaders(settings.Headers);
        var newSignals = TelemetryExportClient.ParseSignals(settings.Signals);
        if (newHeaders is null || newSignals is null)
        {
            return 1;
        }

        using var http = TelemetryExportClient.Create(settings.InstanceName);
        if (http is null)
        {
            return 1;
        }

        var list = await TelemetryExportClient.GetAsync<ForwardingTargetListWire>(http, "/api/forwarding/targets", cancellationToken);
        if (list is null)
        {
            return 1;
        }

        var existing = list.Targets.FirstOrDefault(t => t.Id == settings.Id);
        if (existing is null)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] No forwarding target with id {settings.Id}.");
            return 1;
        }

        var headers = settings.ClearHeaders ? new Dictionary<string, string>() : new Dictionary<string, string>(existing.Headers, StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in newHeaders)
        {
            headers[name] = value;
        }

        var request = new ForwardingTargetRequestWire
        {
            Name = settings.Rename ?? existing.Name,
            Endpoint = settings.Endpoint ?? existing.Endpoint,
            Enabled = settings.Enabled ?? existing.Enabled,
            Headers = headers,
            Signals = settings.AllSignals ? [] : newSignals.Count > 0 ? newSignals : existing.Signals,
            Services = settings.AllServices ? [] : settings.Services.Length > 0 ? settings.Services.ToList() : existing.Services,
            IngestKeyIds = settings.AnyIngestKey ? [] : settings.IngestKeyIds.Length > 0 ? settings.IngestKeyIds.ToList() : existing.IngestKeyIds,
            Gzip = settings.Gzip ?? existing.Gzip,
        };

        using var response = await TelemetryExportClient.SendAsync(http, HttpMethod.Put, $"/api/forwarding/targets/{settings.Id}", request, cancellationToken);
        if (response is null)
        {
            return 1;
        }

        AnsiConsole.MarkupLine($"[green]✓[/] Updated forwarding target [bold]{Markup.Escape(request.Name)}[/] (id: {settings.Id}).");
        return 0;
    }
}

internal sealed class ForwardingDeleteCommand : AsyncCommand<ForwardingDeleteCommand.Settings>
{
    internal sealed class Settings : InstanceSettings
    {
        [CommandArgument(0, "<ID>")]
        [Description("The target's id (see `flare forwarding list`).")]
        public required Guid Id { get; init; }

        [CommandOption("-y|--yes")]
        [Description("Skip the interactive confirmation prompt. Required for non-interactive use.")]
        public bool Yes { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        using var http = TelemetryExportClient.Create(settings.InstanceName);
        if (http is null)
        {
            return 1;
        }

        if (!TelemetryExportClient.ConfirmDelete(settings.Yes, $"forwarding target {settings.Id} (its queued requests are dropped)"))
        {
            return 1;
        }

        using var response = await TelemetryExportClient.SendAsync(http, HttpMethod.Delete, $"/api/forwarding/targets/{settings.Id}", null, cancellationToken);
        if (response is null)
        {
            return 1;
        }

        AnsiConsole.MarkupLine($"[green]✓[/] Deleted forwarding target {settings.Id}.");
        return 0;
    }
}

internal sealed class ForwardingStatusCommand : AsyncCommand<ForwardingStatusCommand.Settings>
{
    internal sealed class Settings : InstanceSettings
    {
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        using var http = TelemetryExportClient.Create(settings.InstanceName);
        if (http is null)
        {
            return 1;
        }

        var status = await TelemetryExportClient.GetAsync<ForwardingStatusWire>(http, "/api/forwarding/status", cancellationToken);
        if (status is null)
        {
            return 1;
        }

        if (status.Targets.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]No forwarding targets.[/]");
            return 0;
        }

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Target");
        table.AddColumn("Source");
        table.AddColumn(new TableColumn("Pending").RightAligned());
        table.AddColumn(new TableColumn("Sent").RightAligned());
        table.AddColumn(new TableColumn("Failed").RightAligned());
        table.AddColumn("Last success");
        table.AddColumn("Last error");
        foreach (var target in status.Targets)
        {
            table.AddRow(
                Markup.Escape(target.Name),
                target.Source,
                target.Pending.ToString(),
                target.Sent.ToString(),
                target.Failed > 0 ? $"[red]{target.Failed}[/]" : "0",
                TelemetryExportClient.Ago(target.LastSuccessAt),
                target.LastError is null ? "[grey]-[/]" : Markup.Escape($"{target.LastError} ({TelemetryExportClient.Ago(target.LastErrorAt)})"));
        }

        AnsiConsole.Write(table);
        return 0;
    }
}

internal sealed class ArchiveShowCommand : AsyncCommand<ArchiveShowCommand.Settings>
{
    internal sealed class Settings : InstanceSettings
    {
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        using var http = TelemetryExportClient.Create(settings.InstanceName);
        if (http is null)
        {
            return 1;
        }

        var archive = await TelemetryExportClient.GetAsync<ArchiveSettingsWire>(http, "/api/archive/settings", cancellationToken);
        if (archive is null)
        {
            return 1;
        }

        if (!archive.Saved)
        {
            AnsiConsole.MarkupLine("[grey]No saved archive settings - the archive follows the worker's Archive configuration. Use `flare archive set` to manage it here.[/]");
            return 0;
        }

        var grid = new Grid().AddColumn().AddColumn();
        grid.AddRow("Enabled", archive.Enabled ? "yes" : "[grey]no[/]");
        grid.AddRow("Endpoint", Markup.Escape(archive.Endpoint));
        grid.AddRow("Access key", Markup.Escape(archive.AccessKey));
        grid.AddRow("Secret key", Markup.Escape(archive.SecretKey));
        grid.AddRow("Prefix", Markup.Escape(archive.Prefix));
        grid.AddRow("Format", archive.Format);
        grid.AddRow("Signals", TelemetryExportClient.SignalList(archive.Signals));
        grid.AddRow("Updated", TelemetryExportClient.Ago(archive.UpdatedAt));
        AnsiConsole.Write(grid);
        return 0;
    }
}

/// <summary>
/// <c>flare archive set</c> saves the S3 archive settings. The first save needs <c>--endpoint</c> and both keys;
/// afterwards any option left out keeps its current value (keys are masked by the API and sent back as such).
/// </summary>
internal sealed class ArchiveSetCommand : AsyncCommand<ArchiveSetCommand.Settings>
{
    internal sealed class Settings : InstanceSettings
    {
        [CommandOption("--endpoint <URL>")]
        [Description("The bucket URL, e.g. https://s3.eu-west-1.amazonaws.com/my-bucket.")]
        public string? Endpoint { get; init; }

        [CommandOption("--access-key <KEY>")]
        public string? AccessKey { get; init; }

        [CommandOption("--secret-key <KEY>")]
        public string? SecretKey { get; init; }

        [CommandOption("--prefix <PREFIX>")]
        [Description("Object key prefix; defaults to 'flare'.")]
        public string? Prefix { get; init; }

        [CommandOption("--format <FORMAT>")]
        [Description("parquet or ndjson.")]
        public string? Format { get; init; }

        [CommandOption("--signal <SIGNAL>")]
        [Description("Archive only this signal: logs, traces or metrics. Repeatable.")]
        public string[] Signals { get; init; } = [];

        [CommandOption("--all-signals")]
        [Description("Archive all signals.")]
        public bool AllSignals { get; init; }

        [CommandOption("--enabled <BOOL>")]
        [Description("Switch the archive on or off. Defaults to on for a first save.")]
        public bool? Enabled { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var signals = TelemetryExportClient.ParseSignals(settings.Signals);
        if (signals is null)
        {
            return 1;
        }

        string? format = null;
        if (settings.Format is not null)
        {
            format = settings.Format.Trim().ToLowerInvariant() switch
            {
                "parquet" => "Parquet",
                "ndjson" => "Ndjson",
                _ => null,
            };
            if (format is null)
            {
                AnsiConsole.MarkupLine("[red]✗[/] --format must be parquet or ndjson.");
                return 1;
            }
        }

        using var http = TelemetryExportClient.Create(settings.InstanceName);
        if (http is null)
        {
            return 1;
        }

        var existing = await TelemetryExportClient.GetAsync<ArchiveSettingsWire>(http, "/api/archive/settings", cancellationToken);
        if (existing is null)
        {
            return 1;
        }

        var endpoint = settings.Endpoint ?? (existing.Saved ? existing.Endpoint : null);
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            AnsiConsole.MarkupLine("[red]✗[/] --endpoint is required for the first save.");
            return 1;
        }

        var request = new ArchiveSettingsRequestWire
        {
            Endpoint = endpoint,
            Enabled = settings.Enabled ?? (existing.Saved ? existing.Enabled : true),
            AccessKey = settings.AccessKey ?? (existing.Saved ? existing.AccessKey : null),
            SecretKey = settings.SecretKey ?? (existing.Saved ? existing.SecretKey : null),
            Prefix = settings.Prefix ?? (existing.Saved ? existing.Prefix : null),
            Format = format ?? (existing.Saved ? existing.Format : null),
            Signals = settings.AllSignals ? [] : signals.Count > 0 ? signals : existing.Saved ? existing.Signals : [],
        };

        using var response = await TelemetryExportClient.SendAsync(http, HttpMethod.Put, "/api/archive/settings", request, cancellationToken);
        if (response is null)
        {
            return 1;
        }

        AnsiConsole.MarkupLine($"[green]✓[/] Saved archive settings ({(request.Enabled == true ? "enabled" : "disabled")}).");
        return 0;
    }
}

internal sealed class ArchiveResetCommand : AsyncCommand<ArchiveResetCommand.Settings>
{
    internal sealed class Settings : InstanceSettings
    {
        [CommandOption("-y|--yes")]
        [Description("Skip the interactive confirmation prompt. Required for non-interactive use.")]
        public bool Yes { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        using var http = TelemetryExportClient.Create(settings.InstanceName);
        if (http is null)
        {
            return 1;
        }

        if (!TelemetryExportClient.ConfirmDelete(settings.Yes, "the saved archive settings (the archive reverts to the worker's Archive configuration)"))
        {
            return 1;
        }

        using var response = await TelemetryExportClient.SendAsync(http, HttpMethod.Delete, "/api/archive/settings", null, cancellationToken);
        if (response is null)
        {
            return 1;
        }

        AnsiConsole.MarkupLine("[green]✓[/] Archive settings reset to configuration.");
        return 0;
    }
}

internal sealed class ArchiveStatusCommand : AsyncCommand<ArchiveStatusCommand.Settings>
{
    internal sealed class Settings : InstanceSettings
    {
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        using var http = TelemetryExportClient.Create(settings.InstanceName);
        if (http is null)
        {
            return 1;
        }

        var status = await TelemetryExportClient.GetAsync<ArchiveStatusWire>(http, "/api/archive/status", cancellationToken);
        if (status is null)
        {
            return 1;
        }

        if (!status.Active)
        {
            AnsiConsole.MarkupLine("[grey]The archive is off (or the alert worker has not reported in).[/]");
            return 0;
        }

        AnsiConsole.MarkupLine($"Archive active, source: {status.Source}.");
        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Table");
        table.AddColumn("Last exported hour");
        table.AddColumn(new TableColumn("Rows").RightAligned());
        table.AddColumn("Last success");
        table.AddColumn("Last error");
        foreach (var t in status.Tables)
        {
            table.AddRow(
                t.Table,
                TelemetryExportClient.Ago(t.LastExportedHour),
                t.LastRows.ToString(),
                TelemetryExportClient.Ago(t.LastSuccessAt),
                t.LastError is null ? "[grey]-[/]" : Markup.Escape($"{t.LastError} ({TelemetryExportClient.Ago(t.LastErrorAt)})"));
        }

        AnsiConsole.Write(table);
        return 0;
    }
}

// ---- Wire DTOs - hand-mirror of Flare.Api's Model/TelemetryExportModels.cs. Enums travel as strings. ----

internal sealed class ForwardingTargetWire
{
    public Guid Id { get; init; }

    public string Name { get; init; } = "";

    public bool Enabled { get; init; } = true;

    public string Endpoint { get; init; } = "";

    public Dictionary<string, string> Headers { get; init; } = [];

    public List<string> Signals { get; init; } = [];

    public List<string> Services { get; init; } = [];

    public List<Guid> IngestKeyIds { get; init; } = [];

    public bool Gzip { get; init; } = true;
}

internal sealed class ForwardingTargetListWire
{
    public List<ForwardingTargetWire> Targets { get; init; } = [];
}

internal sealed class ForwardingTargetRequestWire
{
    public required string Name { get; init; }

    public required string Endpoint { get; init; }

    public bool? Enabled { get; init; }

    public Dictionary<string, string>? Headers { get; init; }

    public List<string>? Signals { get; init; }

    public List<string>? Services { get; init; }

    public List<Guid>? IngestKeyIds { get; init; }

    public bool? Gzip { get; init; }
}

internal sealed class ForwardingTargetStatusWire
{
    public string Key { get; init; } = "";

    public string Name { get; init; } = "";

    public string Source { get; init; } = "";

    public long Pending { get; init; }

    public long Sent { get; init; }

    public long Failed { get; init; }

    public DateTimeOffset? LastSuccessAt { get; init; }

    public string? LastError { get; init; }

    public DateTimeOffset? LastErrorAt { get; init; }
}

internal sealed class ForwardingStatusWire
{
    public List<ForwardingTargetStatusWire> Targets { get; init; } = [];
}

internal sealed class ArchiveSettingsWire
{
    public bool Enabled { get; init; }

    public string Endpoint { get; init; } = "";

    public string AccessKey { get; init; } = "";

    public string SecretKey { get; init; } = "";

    public string Prefix { get; init; } = "flare";

    public string Format { get; init; } = "Parquet";

    public List<string> Signals { get; init; } = [];

    public bool Saved { get; init; }

    public DateTimeOffset? UpdatedAt { get; init; }
}

internal sealed class ArchiveSettingsRequestWire
{
    public required string Endpoint { get; init; }

    public bool? Enabled { get; init; }

    public string? AccessKey { get; init; }

    public string? SecretKey { get; init; }

    public string? Prefix { get; init; }

    public string? Format { get; init; }

    public List<string>? Signals { get; init; }
}

internal sealed class ArchiveTableStatusWire
{
    public string Table { get; init; } = "";

    public DateTimeOffset? LastExportedHour { get; init; }

    public DateTimeOffset? LastSuccessAt { get; init; }

    public long LastRows { get; init; }

    public string? LastError { get; init; }

    public DateTimeOffset? LastErrorAt { get; init; }
}

internal sealed class ArchiveStatusWire
{
    public bool Active { get; init; }

    public string Source { get; init; } = "";

    public List<ArchiveTableStatusWire> Tables { get; init; } = [];
}
