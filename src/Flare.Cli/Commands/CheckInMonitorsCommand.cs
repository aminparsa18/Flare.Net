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
/// <c>flare checkin-monitors list/create/update/delete</c> - CRUD for cron and job check-in monitors via Flare.Api's
/// <c>/api/checkin-monitors</c> (<c>src/Flare.Api/Endpoints/CheckInMonitorEndpoints.cs</c>). The list shows each monitor's
/// state, which the API already decided. See <c>docs-internal/adr/0260-cron-job-checkins.md</c>.
/// </summary>
internal sealed class CheckInMonitorsListCommand : AsyncCommand<CheckInMonitorsListCommand.Settings>
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

        CheckInMonitorListResponseWire? response;
        try
        {
            using var httpResponse = await http.GetAsync("/api/checkin-monitors", cancellationToken);
            if (!httpResponse.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[red]✗[/] GET /api/checkin-monitors failed: {(int)httpResponse.StatusCode} {httpResponse.ReasonPhrase}");
                return 1;
            }

            response = await httpResponse.Content.ReadFromJsonAsync<CheckInMonitorListResponseWire>(WireJsonOptions.Instance, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] Couldn't reach the API on localhost:{port} - is `api` running? Check `flare status`.");
            return 1;
        }

        var monitors = response?.Monitors ?? [];
        if (monitors.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]No check-in monitors configured.[/]");
            return 0;
        }

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Name");
        table.AddColumn("Slug");
        table.AddColumn("Schedule");
        table.AddColumn("Grace");
        table.AddColumn("State");
        table.AddColumn("Last ping");
        table.AddColumn("Next expected");
        table.AddColumn("Id");

        foreach (var monitor in monitors)
        {
            table.AddRow(
                Markup.Escape(monitor.Name),
                Markup.Escape(monitor.Slug),
                Markup.Escape(CheckInMonitorFormat.Schedule(monitor)),
                CheckInMonitorFormat.Duration(monitor.GraceSeconds),
                CheckInMonitorFormat.State(monitor.Status),
                CheckInMonitorFormat.Time(monitor.Status?.LastPing),
                CheckInMonitorFormat.Time(monitor.Status?.NextExpected),
                $"[grey]{monitor.Id}[/]");
        }

        AnsiConsole.Write(table);
        return 0;
    }
}

/// <summary>
/// <c>flare checkin-monitors create &lt;NAME&gt; (--cron EXPR | --interval SECONDS)</c> via <c>POST /api/checkin-monitors</c>.
/// Validation (slug shape, exactly one schedule, bounds) is the API's; its 400 detail is surfaced as is.
/// </summary>
internal sealed class CheckInMonitorsCreateCommand : AsyncCommand<CheckInMonitorsCreateCommand.Settings>
{
    internal sealed class Settings : InstanceSettings
    {
        [CommandArgument(0, "<NAME>")]
        [Description("A label for this monitor, e.g. 'Nightly backup'. The slug in the ping URL is derived from it unless --slug is given.")]
        public required string Name { get; init; }

        [CommandOption("--slug <SLUG>")]
        [Description("The name in the ping URL: lower-case letters, digits, '-' and '_', up to 64 characters. Fixed after creation. Derived from NAME when omitted.")]
        public string? Slug { get; init; }

        [CommandOption("--description <DESCRIPTION>")]
        public string? Description { get; init; }

        [CommandOption("--cron <EXPRESSION>")]
        [Description("A five-field cron expression or macro such as '0 2 * * *' or '@daily'. Set this or --interval.")]
        public string? Cron { get; init; }

        [CommandOption("--interval <SECONDS>")]
        [Description("A ping is expected every this many seconds, 60 to 2678400, measured from the last ping. Set this or --cron.")]
        public int? IntervalSeconds { get; init; }

        [CommandOption("--timezone <ZONE>")]
        [Description("An IANA time zone such as Europe/Berlin that --cron is read in. Defaults to UTC.")]
        public string? TimeZone { get; init; }

        [CommandOption("--grace <SECONDS>")]
        [Description("How long after the expected time a ping may arrive before the job counts as late. Defaults to 300.")]
        public int? GraceSeconds { get; init; }

        [CommandOption("--max-runtime <SECONDS>")]
        [Description("A run that has started and not finished within this time counts as failed. 0 (default) means no limit.")]
        public int? MaxRuntimeSeconds { get; init; }

        [CommandOption("--enabled <BOOL>")]
        [Description("true or false. Defaults to true.")]
        public bool? Enabled { get; init; }
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
            AnsiConsole.MarkupLine("[red]✗[/] NAME is required.");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(settings.Cron) == (settings.IntervalSeconds is null))
        {
            AnsiConsole.MarkupLine("[red]✗[/] Set exactly one of --cron and --interval.");
            return 1;
        }

        var request = new CheckInMonitorRequestWire
        {
            Name = settings.Name,
            Slug = settings.Slug,
            Description = settings.Description,
            CronExpression = settings.Cron,
            IntervalSeconds = settings.IntervalSeconds,
            TimeZone = settings.TimeZone,
            GraceSeconds = settings.GraceSeconds,
            MaxRuntimeSeconds = settings.MaxRuntimeSeconds,
            Enabled = settings.Enabled,
        };

        var port = instance.ReadEnvValue("FLARE_API_PORT", "8080");
        using var http = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };

        CheckInMonitorWire? monitor;
        try
        {
            using var httpResponse = await http.PostAsJsonAsync("/api/checkin-monitors", request, WireJsonOptions.Instance, cancellationToken);
            if (!httpResponse.IsSuccessStatusCode)
            {
                var detail = await NotificationChannelTypeParsing.TryReadProblemDetailAsync(httpResponse, cancellationToken);
                AnsiConsole.MarkupLine($"[red]✗[/] POST /api/checkin-monitors failed: {(int)httpResponse.StatusCode} {Markup.Escape(detail ?? httpResponse.ReasonPhrase ?? "")}");
                return 1;
            }

            monitor = await httpResponse.Content.ReadFromJsonAsync<CheckInMonitorWire>(WireJsonOptions.Instance, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] Couldn't reach the API on localhost:{port} - is `api` running? Check `flare status`.");
            return 1;
        }

        if (monitor is null)
        {
            AnsiConsole.MarkupLine("[red]✗[/] Empty response from /api/checkin-monitors.");
            return 1;
        }

        AnsiConsole.MarkupLine($"[green]✓[/] Created check-in monitor [bold]{Markup.Escape(monitor.Name)}[/] (id: {monitor.Id}).");
        AnsiConsole.MarkupLine($"  Ping it with: /v1/checkins/{Markup.Escape(monitor.Slug)} (add /start or /fail) on the ingest HTTP port.");
        return 0;
    }
}

/// <summary>
/// <c>flare checkin-monitors update &lt;ID&gt; [options]</c> via <c>PUT /api/checkin-monitors/{id}</c>, which replaces the whole
/// monitor. Fetches the existing one first and overrides only the options passed, so <c>update &lt;ID&gt; --enabled false</c>
/// pauses a monitor without re-typing the rest. Passing --cron or --interval switches the schedule kind.
/// </summary>
internal sealed class CheckInMonitorsUpdateCommand : AsyncCommand<CheckInMonitorsUpdateCommand.Settings>
{
    internal sealed class Settings : InstanceSettings
    {
        [CommandArgument(0, "<ID>")]
        [Description("The monitor's id (see `flare checkin-monitors list`).")]
        public required Guid Id { get; init; }

        // Not --name: that is InstanceSettings' -n|--name (target instance), and Spectre rejects the whole
        // command tree when two options on one command share a name.
        [CommandOption("--rename <NAME>")]
        [Description("A new label for the monitor. The slug does not change.")]
        public string? Name { get; init; }

        [CommandOption("--description <DESCRIPTION>")]
        public string? Description { get; init; }

        [CommandOption("--cron <EXPRESSION>")]
        [Description("A five-field cron expression or macro. Replaces an interval schedule.")]
        public string? Cron { get; init; }

        [CommandOption("--interval <SECONDS>")]
        [Description("Seconds between expected pings, 60 to 2678400. Replaces a cron schedule.")]
        public int? IntervalSeconds { get; init; }

        [CommandOption("--timezone <ZONE>")]
        [Description("An IANA time zone that the cron expression is read in.")]
        public string? TimeZone { get; init; }

        [CommandOption("--grace <SECONDS>")]
        public int? GraceSeconds { get; init; }

        [CommandOption("--max-runtime <SECONDS>")]
        [Description("0 means no limit.")]
        public int? MaxRuntimeSeconds { get; init; }

        [CommandOption("--enabled <BOOL>")]
        [Description("true or false.")]
        public bool? Enabled { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var instance = FlareHome.ResolveTarget(settings.InstanceName);
        if (!instance.IsInitialized)
        {
            AnsiConsole.MarkupLine($"[grey]Not initialized yet - run `{instance.StartHint}` first.[/]");
            return 1;
        }

        if (!string.IsNullOrWhiteSpace(settings.Cron) && settings.IntervalSeconds is not null)
        {
            AnsiConsole.MarkupLine("[red]✗[/] Set at most one of --cron and --interval.");
            return 1;
        }

        var port = instance.ReadEnvValue("FLARE_API_PORT", "8080");
        using var http = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };

        try
        {
            using var getResponse = await http.GetAsync($"/api/checkin-monitors/{settings.Id}", cancellationToken);
            if (getResponse.StatusCode == HttpStatusCode.NotFound)
            {
                AnsiConsole.MarkupLine($"[red]✗[/] No check-in monitor with id {settings.Id}.");
                return 1;
            }

            if (!getResponse.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[red]✗[/] GET /api/checkin-monitors/{settings.Id} failed: {(int)getResponse.StatusCode} {getResponse.ReasonPhrase}");
                return 1;
            }

            var existing = await getResponse.Content.ReadFromJsonAsync<CheckInMonitorWire>(WireJsonOptions.Instance, cancellationToken);
            if (existing is null)
            {
                AnsiConsole.MarkupLine("[red]✗[/] Empty response from /api/checkin-monitors.");
                return 1;
            }

            var request = CheckInMonitorFormat.MergeUpdate(existing, settings.Name, settings.Description, settings.Cron, settings.IntervalSeconds,
                settings.TimeZone, settings.GraceSeconds, settings.MaxRuntimeSeconds, settings.Enabled);

            using var putResponse = await http.PutAsJsonAsync($"/api/checkin-monitors/{settings.Id}", request, WireJsonOptions.Instance, cancellationToken);
            if (!putResponse.IsSuccessStatusCode)
            {
                var detail = await NotificationChannelTypeParsing.TryReadProblemDetailAsync(putResponse, cancellationToken);
                AnsiConsole.MarkupLine($"[red]✗[/] PUT /api/checkin-monitors/{settings.Id} failed: {(int)putResponse.StatusCode} {Markup.Escape(detail ?? putResponse.ReasonPhrase ?? "")}");
                return 1;
            }

            AnsiConsole.MarkupLine($"[green]✓[/] Updated check-in monitor [bold]{Markup.Escape(request.Name)}[/].");
            return 0;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] Couldn't reach the API on localhost:{port} - is `api` running? Check `flare status`.");
            return 1;
        }
    }
}

/// <summary><c>flare checkin-monitors delete &lt;ID&gt;</c> via <c>DELETE /api/checkin-monitors/{id}</c>.</summary>
internal sealed class CheckInMonitorsDeleteCommand : AsyncCommand<CheckInMonitorsDeleteCommand.Settings>
{
    internal sealed class Settings : InstanceSettings
    {
        [CommandArgument(0, "<ID>")]
        [Description("The monitor's id (see `flare checkin-monitors list`).")]
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

            if (!AnsiConsole.Confirm($"Delete check-in monitor [bold]{settings.Id}[/]? Pings for its slug are ignored from now on; stored pings stay. Continue?", defaultValue: false))
            {
                AnsiConsole.MarkupLine("[grey]Aborted - nothing was removed.[/]");
                return 1;
            }
        }

        var port = instance.ReadEnvValue("FLARE_API_PORT", "8080");
        using var http = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
        try
        {
            using var response = await http.DeleteAsync($"/api/checkin-monitors/{settings.Id}", cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                AnsiConsole.MarkupLine($"[red]✗[/] No check-in monitor with id {settings.Id}.");
                return 1;
            }

            if (!response.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[red]✗[/] DELETE /api/checkin-monitors/{settings.Id} failed: {(int)response.StatusCode} {response.ReasonPhrase}");
                return 1;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] Couldn't reach the API on localhost:{port} - is `api` running? Check `flare status`.");
            return 1;
        }

        AnsiConsole.MarkupLine($"[green]✓[/] Deleted check-in monitor {settings.Id}.");
        return 0;
    }
}

internal static class CheckInMonitorFormat
{
    /// <summary>The cron expression with its zone, or "every 5 min" for an interval schedule.</summary>
    public static string Schedule(CheckInMonitorWire monitor) =>
        monitor.CronExpression.Length > 0
            ? $"{monitor.CronExpression} ({monitor.TimeZone})"
            : $"every {Duration(monitor.IntervalSeconds)}";

    public static string Duration(int seconds) => seconds switch
    {
        _ when seconds > 0 && seconds % 86400 == 0 => $"{seconds / 86400} d",
        _ when seconds > 0 && seconds % 3600 == 0 => $"{seconds / 3600} h",
        _ when seconds > 0 && seconds % 60 == 0 => $"{seconds / 60} min",
        _ => $"{seconds} s",
    };

    public static string State(CheckInStatusWire? status) => status?.State switch
    {
        "Ok" => "[green]ok[/]",
        "Running" => "[blue]running[/]",
        "Pending" => "[grey]pending[/]",
        "Disabled" => "[grey]disabled[/]",
        "Late" => "[red]late[/]",
        "Failed" => "[red]failed[/]",
        "Overran" => "[red]overran[/]",
        _ => "[grey]unknown[/]",
    };

    public static string Time(DateTimeOffset? time) =>
        time is { } value ? value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'") : "[grey]-[/]";

    /// <summary>
    /// The PUT body for an update: the existing monitor with only the passed options overridden. The schedule is one or
    /// the other, so passing --cron drops an interval and --interval drops a cron expression.
    /// </summary>
    public static CheckInMonitorRequestWire MergeUpdate(
        CheckInMonitorWire existing,
        string? name,
        string? description,
        string? cron,
        int? intervalSeconds,
        string? timeZone,
        int? graceSeconds,
        int? maxRuntimeSeconds,
        bool? enabled)
    {
        var useCron = !string.IsNullOrWhiteSpace(cron) || (intervalSeconds is null && existing.CronExpression.Length > 0);
        return new CheckInMonitorRequestWire
        {
            Name = name ?? existing.Name,
            Description = description ?? existing.Description,
            CronExpression = useCron ? (string.IsNullOrWhiteSpace(cron) ? existing.CronExpression : cron) : null,
            IntervalSeconds = useCron ? null : intervalSeconds ?? existing.IntervalSeconds,
            TimeZone = timeZone ?? existing.TimeZone,
            GraceSeconds = graceSeconds ?? existing.GraceSeconds,
            MaxRuntimeSeconds = maxRuntimeSeconds ?? existing.MaxRuntimeSeconds,
            Enabled = enabled ?? existing.Enabled,
        };
    }
}
