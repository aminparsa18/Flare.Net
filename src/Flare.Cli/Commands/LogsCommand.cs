using System.ComponentModel;
using Flare.Cli.Internal;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Flare.Cli.Commands;

internal sealed class LogsCommand : AsyncCommand<LogsCommand.Settings>
{
    internal sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "[SERVICE]")]
        [Description("Which service to show logs for (clickhouse, redis, ingest, api, dashboard). Omit for all.")]
        public string? Service { get; init; }

        [CommandOption("-f|--follow")]
        [Description("Stream logs instead of printing a fixed tail.")]
        public bool Follow { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (!FlareHome.IsInitialized)
        {
            AnsiConsole.MarkupLine("[grey]Not initialized yet - run `flare start` first.[/]");
            return 1;
        }

        var args = new List<string> { "logs", "--tail=200" };
        if (settings.Follow)
        {
            args.Add("-f");
        }

        if (!string.IsNullOrWhiteSpace(settings.Service))
        {
            args.Add(settings.Service);
        }

        // Streamed, not captured: this is meant to be watched (especially with -f), not
        // parsed - console-inherited output, cleanly interruptible with Ctrl+C.
        return await ComposeRunner.RunStreamedAsync(args);
    }
}
