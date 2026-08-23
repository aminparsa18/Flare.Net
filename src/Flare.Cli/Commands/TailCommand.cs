using System.ComponentModel;
using System.Net.Sockets;
using System.Net.WebSockets;
using Flare.Cli.Internal;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Flare.Cli.Commands;

/// <summary>
/// Live tail via Flare.Api's <c>GET /api/logs/tail</c> WebSocket - the CLI-native
/// equivalent of the dashboard's Logs Explorer live-tail. Distinct from `flare logs`,
/// which is raw Docker container stdout, not app-level structured log events.
/// </summary>
internal sealed class TailCommand : AsyncCommand<TailCommand.Settings>
{
    internal sealed class Settings : InstanceSettings
    {
        [CommandOption("-s|--service <NAME>")]
        [Description("Filter by exact service name (e.g. flare-ingest). Repeatable.")]
        public string[] Service { get; init; } = [];

        [CommandOption("-l|--level <LEVEL>")]
        [Description("Filter by severity: trace, debug, info, warn, error, fatal. Repeatable.")]
        public string[] Level { get; init; } = [];

        [CommandOption("--trace-id <ID>")]
        [Description("Exact lower-hex TraceId match.")]
        public string? TraceId { get; init; }

        [CommandOption("--search <TEXT>")]
        [Description("Case-insensitive substring match against the log body.")]
        public string? Search { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var instance = FlareHome.Resolve(settings.InstanceName);

        if (!instance.IsInitialized)
        {
            AnsiConsole.MarkupLine($"[grey]Not initialized yet - run `{instance.StartHint}` first.[/]");
            return 1;
        }

        var severityNumbers = new List<byte>();
        foreach (var level in settings.Level)
        {
            if (!SeverityLevels.TryExpand(level, out var numbers))
            {
                AnsiConsole.MarkupLine($"[red]✗[/] Unknown level '{Markup.Escape(level)}' - expected one of: trace, debug, info, warn, error, fatal.");
                return 1;
            }

            severityNumbers.AddRange(numbers);
        }

        var port = instance.ReadEnvValue("FLARE_API_PORT", "8080");
        var uri = new Uri($"ws://localhost:{port}/api/logs/tail");

        await using var client = new LogTailClient();

        try
        {
            await client.ConnectAsync(uri, cancellationToken);
        }
        catch (Exception ex) when (ex is WebSocketException or HttpRequestException or SocketException)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] Couldn't connect to {uri} - is `api` running? Check `flare status`.");
            return 1;
        }

        // Same gap ComposeRunner's own cleanup exists for: Spectre.Console.Cli's
        // cancellationToken alone isn't a reliable Ctrl+C signal across platforms/
        // versions, so InterruptSignal backstops it with its own SIGINT/SIGTERM
        // handling - here, that means cancelling the receive loop so the `finally`
        // below gets a chance to close the socket cleanly instead of the connection
        // just dying mid-stream.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var interrupt = InterruptSignal.OnInterrupt(() =>
        {
            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        });

        await client.SubscribeAsync(
            new TailFilter
            {
                Services = settings.Service,
                SeverityNumbers = severityNumbers,
                TraceId = settings.TraceId,
                Search = settings.Search,
            },
            cts.Token);

        AnsiConsole.MarkupLine("[grey]Tailing - Ctrl+C to stop.[/]");

        try
        {
            await foreach (var message in client.ReadAllAsync(cts.Token))
            {
                Render(message);
            }
        }
        catch (OperationCanceledException)
        {
            // Ctrl+C - fall through to the clean shutdown below.
        }
        finally
        {
            await client.CloseAsync();
        }

        return 0;
    }

    private static void Render(TailMessage message)
    {
        switch (message)
        {
            case TailMessage.Event e:
                var label = SeverityLevels.LabelFor(e.SeverityNumber).PadRight(5);
                var color = SeverityLevels.ColorFor(e.SeverityNumber);
                var time = e.Timestamp.ToLocalTime().ToString("HH:mm:ss.fff");
                AnsiConsole.MarkupLine($"[grey]{time}[/] [{color}]{label}[/] [bold]{Markup.Escape(e.ServiceName)}[/] {Markup.Escape(e.Body)}");
                break;
            case TailMessage.Dropped d:
                AnsiConsole.MarkupLine($"[yellow]! {d.Count} event(s) dropped - reader falling behind.[/]");
                break;
            case TailMessage.Error err:
                AnsiConsole.MarkupLine($"[red]! {Markup.Escape(err.Message)}[/]");
                break;
        }
    }
}
